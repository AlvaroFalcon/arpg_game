// Liquid surface over the voxel hazard tiles — docs/design/art-pipeline.md ->
// "Liquids". One quad per liquid tile (instanced), animated in world space so the
// pattern flows across tiles without seams: rolling fbm waves displace the
// vertices and bend the normal, bright caustic ridges drift over it, and foam
// laps at the shore. INSTANCE_CUSTOM marks which sides of the tile touch land
// (r = west, g = east, b = north, a = south). The same shader is water, lava
// and ooze through its uniforms.
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

varying vec3 world_pos;
varying vec4 shore;

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

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	shore = INSTANCE_CUSTOM;
	VERTEX.y += (waves(world_pos.xz, TIME) - 0.5) * wave_height;
}

void fragment() {
	float t = TIME;
	vec2 p = world_pos.xz;
	float e = 0.06;
	float h = waves(p, t);
	vec3 n = normalize(vec3((h - waves(p + vec2(e, 0.0), t)) * 5.0, 1.0, (h - waves(p + vec2(0.0, e), t)) * 5.0));
	NORMAL = normalize((VIEW_MATRIX * vec4(n, 0.0)).xyz);

	// Distance to the nearest shore side, in tile units (1 = open water).
	float d = 1.0;
	if (shore.r > 0.5) d = min(d, UV.x);
	if (shore.g > 0.5) d = min(d, 1.0 - UV.x);
	if (shore.b > 0.5) d = min(d, UV.y);
	if (shore.a > 0.5) d = min(d, 1.0 - UV.y);

	vec3 col = mix(shallow_color.rgb, deep_color.rgb, smoothstep(0.0, 0.5, d) * (0.75 + 0.25 * h));
	// Caustic ridges: thin bright lines where a drifting fbm crosses its bands.
	float band = abs(sin(fbm(p * 2.4 + vec2(t * 0.3, -t * 0.22) * flow_speed) * 12.566));
	col += shallow_color.rgb * pow(1.0 - band, 10.0) * caustics;
	// Foam: a lapping line along the shore, broken up by noise.
	float edge = 0.1 + 0.05 * sin(t * 1.8 + (p.x + p.y) * 4.0);
	float foam = (1.0 - smoothstep(0.0, edge, d)) * (0.55 + 0.45 * noise(p * 14.0 + vec2(t * 0.8, 0.0)));
	foam += smoothstep(0.78, 0.9, h) * 0.25;
	col = mix(col, foam_color.rgb, clamp(foam, 0.0, 1.0));

	float fresnel = pow(1.0 - clamp(dot(NORMAL, VIEW), 0.0, 1.0), 3.0);
	ALBEDO = col + vec3(0.12) * fresnel;
	ROUGHNESS = roughness_value;
	METALLIC = 0.0;
	SPECULAR = 0.65;
	EMISSION = col * emission_strength;
}
