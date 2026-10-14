// Liquid surface over the voxel hazard tiles — docs/design/art-pipeline.md ->
// "Liquids". One quad per liquid tile (instanced), animated in world space so the
// pattern flows across tiles without seams: rolling fbm waves displace the
// vertices and bend the normal, bright caustic ridges drift over it, and foam
// laps at the shore. The shoreline comes from a blurred mask of the liquid
// tiles, so it is smooth and rounded rather than following the square tiles.
// The same shader is water, lava and ooze through its uniforms; `style` switches lava to drifting crust plates
// over glowing cracks with popping bubbles, and ooze to slow fat bubbles.
shader_type spatial;
render_mode cull_back, specular_schlick_ggx;

uniform vec4 deep_color : source_color = vec4(0.03, 0.14, 0.28, 1.0);
uniform vec4 shallow_color : source_color = vec4(0.1, 0.42, 0.58, 1.0);
uniform vec4 foam_color : source_color = vec4(0.82, 0.94, 1.0, 1.0);
uniform float emission_strength = 0.0;
uniform float flow_speed = 1.0;
uniform float wave_height = 0.035;
uniform float roughness_value = 0.06;
uniform float caustics = 0.4;
// 0 water, 1 lava (drifting crust plates over glowing cracks), 2 ooze (bubbling).
uniform int style = 0;
uniform vec4 crack_color : source_color = vec4(1.0, 0.8, 0.3, 1.0);
// The blurred liquid mask (one texel per tile) and how it maps onto world x/z.
uniform sampler2D shore_mask : filter_linear, repeat_disable;
uniform vec2 mask_origin = vec2(0.0);
uniform vec2 mask_step = vec2(1.0);
uniform vec2 mask_size = vec2(1.0);

varying vec3 world_pos;

float hash(vec2 p) { return fract(sin(dot(p, vec2(127.1, 311.7))) * 43758.5453); }

float noise(vec2 p) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	vec2 u = f * f * (3.0 - 2.0 * f);
	return mix(mix(hash(i), hash(i + vec2(1.0, 0.0)), u.x), mix(hash(i + vec2(0.0, 1.0)), hash(i + vec2(1.0, 1.0)), u.x), u.y);
}

float fbm(vec2 p) {
	float v = 0.0;
	float a = 0.5;
	for (int i = 0; i < 4; i++) {
		v += a * noise(p);
		p = p * 2.03 + vec2(1.7, 9.2);
		a *= 0.5;
	}
	return v;
}

float waves(vec2 p, float t) {
	return fbm(p * 1.1 + vec2(0.35, 0.2) * t * flow_speed) * 0.6
		+ fbm(p * 2.6 - vec2(0.25, -0.3) * t * flow_speed) * 0.4;
}

// Cellular noise: distance to the nearest and second-nearest drifting point.
vec2 cells(vec2 p, float t) {
	vec2 i = floor(p);
	vec2 f = fract(p);
	float d1 = 8.0;
	float d2 = 8.0;
	for (int y = -1; y <= 1; y++) {
		for (int x = -1; x <= 1; x++) {
			vec2 o = vec2(float(x), float(y));
			vec2 h = vec2(hash(i + o), hash(i + o + vec2(17.3, 5.1)));
			vec2 pt = o + 0.5 + 0.38 * sin(t * 0.35 + 6.2831 * h);
			float d = length(pt - f);
			if (d < d1) { d2 = d1; d1 = d; } else if (d < d2) { d2 = d; }
		}
	}
	return vec2(d1, d2);
}

// Bubbles: on a coarse grid, each cell swells a bubble that pops on its own clock.
float bubbles(vec2 p, float t, float density) {
	vec2 g = floor(p);
	vec2 f = fract(p);
	float rnd = hash(g);
	if (rnd > density) return 0.0;
	float phase = fract(t * (0.25 + 0.3 * rnd) + rnd * 7.0);
	vec2 c = vec2(0.3 + 0.4 * hash(g + 3.1), 0.3 + 0.4 * hash(g + 8.7));
	float r = 0.08 + 0.22 * phase;
	float ring = smoothstep(r, r - 0.05, length(f - c)) * (1.0 - smoothstep(0.75, 1.0, phase));
	return ring;
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	VERTEX.y += (waves(world_pos.xz, TIME) - 0.5) * wave_height;
}

void fragment() {
	float t = TIME;
	vec2 p = world_pos.xz;
	float e = 0.06;
	float h = waves(p, t);
	vec3 n = normalize(vec3((h - waves(p + vec2(e, 0.0), t)) * 5.0, 1.0, (h - waves(p + vec2(0.0, e), t)) * 5.0));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// The shoreline: the blurred liquid mask (one texel per tile, see
	// VoxelStage.AddLiquidSurface) crosses one half along a smooth, rounded curve
	// instead of the tiles' square steps; world-space noise wobbles it a little.
	// The land side is cut away (the floor is drawn beneath).
	vec2 texel = (p - mask_origin) / mask_step;
	float field = texture(shore_mask, (texel + 0.5) / mask_size).r;
	float shoreLine = 0.5 + 0.14 * (noise(p * 2.3 + vec2(3.7, 1.1)) - 0.5);
	if (field < shoreLine) {
		discard;
	}

	// How far into the liquid (0 at the shore, 1 well inside), for depth, foam and the lava rim.
	float d = clamp((field - shoreLine) * 3.0, 0.0, 1.0);

	vec3 col = mix(shallow_color.rgb, deep_color.rgb, smoothstep(0.0, 0.5, d) * (0.75 + 0.25 * h));
	float glow = 0.0;
	if (style == 1) {
		// Lava: dark cooling crust plates drift apart over a molten core; the
		// cracks between them burn bright and the whole surface breathes.
		vec2 flow = vec2(t * 0.05, t * 0.03) * flow_speed;
		vec2 c = cells(p * 0.75 + flow + vec2(h * 0.25), t * flow_speed);
		float edge = c.y - c.x;
		float crack = 1.0 - smoothstep(0.02, 0.07 + 0.03 * sin(t * 1.3 + p.x), edge);
		float halo = 1.0 - smoothstep(0.05, 0.3, edge);
		float crust = 0.55 + 0.45 * fbm(p * 2.2 + flow * 2.0);
		float pulse = 0.75 + 0.25 * sin(t * 2.1 + fbm(p * 0.7) * 6.0);
		vec3 molten = mix(shallow_color.rgb, crack_color.rgb, 0.5 + 0.5 * sin(t * 3.0 + p.y * 2.0));
		// Plates are dark rock warmed red near their edges; the cracks are molten.
		vec3 plate = deep_color.rgb * crust + shallow_color.rgb * halo * 0.35 * pulse;
		col = mix(plate, molten, crack);
		float pop = bubbles(p * 1.6 + flow, t, 0.3);
		col = mix(col, crack_color.rgb, pop * 0.85);
		// Shores cool into a darker rim.
		col = mix(col, deep_color.rgb * 0.5, (1.0 - smoothstep(0.0, 0.12, d)) * 0.6);
		glow = (crack * 2.0 + halo * 0.4 + 0.1 + pop * 1.4) * pulse;
	} else {
		// Caustic ridges: thin bright lines where a drifting fbm crosses its bands.
		float band = abs(sin(fbm(p * 2.4 + vec2(t * 0.3, -t * 0.22) * flow_speed) * 12.566));
		col += shallow_color.rgb * pow(1.0 - band, 10.0) * caustics;
		// Foam: a lapping line along the shore, broken up by noise.
		float edge = 0.1 + 0.05 * sin(t * 1.8 + (p.x + p.y) * 4.0);
		float foam = (1.0 - smoothstep(0.0, edge, d)) * (0.55 + 0.45 * noise(p * 14.0 + vec2(t * 0.8, 0.0)));
		foam += smoothstep(0.78, 0.9, h) * 0.25;
		if (style == 2) {
			// Ooze: slow fat bubbles swelling and popping.
			foam += bubbles(p * 1.8 + vec2(t * 0.04), t * 0.7, 0.45) * 0.9;
		}
		col = mix(col, foam_color.rgb, clamp(foam, 0.0, 1.0));
		glow = 1.0;
	}

	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = col + vec3(0.12) * fresnel;
	ROUGHNESS = roughness_value;
	METALLIC = 0.0;
	SPECULAR = 0.65;
	EMISSION = col * emission_strength * glow;
}
