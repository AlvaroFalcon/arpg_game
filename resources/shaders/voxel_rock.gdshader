// Cave rock (docs/design/art-pipeline.md -> "Cave walls"): the colour comes from
// world space, so one continuous wall mesh shows no seam per tile. Sampled on a
// grid of `voxel` metres to keep the pixel/voxel look of the rest of the scene:
// blotches of three tones from the theme's wall palette, darker sides with
// horizontal strata, a lighter top. Dithers away in front of the player
// like voxel_wall.gdshader (same test, same uniforms driven by VoxelStage).
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform vec4 albedo : source_color = vec4(0.45, 0.4, 0.38, 1.0);
uniform vec4 albedo_dark : source_color = vec4(0.25, 0.22, 0.21, 1.0);
uniform vec4 albedo_light : source_color = vec4(0.6, 0.55, 0.5, 1.0);
uniform float voxel = 0.125;
global uniform vec3 player_pos;
uniform vec2 fade_radii = vec2(1.1, 1.5);
uniform float chest_height = 0.75;

varying vec3 world_pos;
varying vec3 world_normal;

float hash(vec3 p) {
	return fract(sin(dot(p, vec3(127.1, 311.7, 74.7))) * 43758.5453);
}

float value_noise(vec3 p) {
	vec3 i = floor(p);
	vec3 f = fract(p);
	f = f * f * (3.0 - 2.0 * f);
	float n000 = hash(i);
	float n100 = hash(i + vec3(1, 0, 0));
	float n010 = hash(i + vec3(0, 1, 0));
	float n110 = hash(i + vec3(1, 1, 0));
	float n001 = hash(i + vec3(0, 0, 1));
	float n101 = hash(i + vec3(1, 0, 1));
	float n011 = hash(i + vec3(0, 1, 1));
	float n111 = hash(i + vec3(1, 1, 1));
	return mix(mix(mix(n000, n100, f.x), mix(n010, n110, f.x), f.y), mix(mix(n001, n101, f.x), mix(n011, n111, f.x), f.y), f.z);
}

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
	world_normal = normalize((MODEL_MATRIX * vec4(NORMAL, 0.0)).xyz);
}

void fragment() {
	// Snap to the voxel grid (nudged inwards so faces on a grid line don't flicker).
	vec3 q = floor((world_pos - world_normal * 0.01) / voxel) * voxel;
	float blotch = value_noise(q * 2.6) * 0.6 + value_noise(q * 7.0) * 0.4;
	vec3 colour = blotch < 0.36 ? mix(albedo_dark.rgb, albedo.rgb, 0.45) : (blotch > 0.64 ? albedo_light.rgb : albedo.rgb);
	colour *= 0.94 + 0.12 * hash(q);

	if (world_normal.y < 0.6) {
		// Sides: a shade darker, with wavy horizontal strata.
		colour *= 0.82;
		float strata = fract((q.y + 0.25 * value_noise(q * 1.3)) / (voxel * 4.0));
		if (strata < 0.25) {
			colour = mix(colour, albedo_dark.rgb, 0.35);
		}
	} else {
		// Tops: the rock's rim catching the light.
		colour = mix(colour, albedo_light.rgb, 0.2);
	}

	ALBEDO = colour;
	ROUGHNESS = 1.0;

	vec3 player_view = (VIEW_MATRIX * vec4(player_pos + vec3(0.0, chest_height, 0.0), 1.0)).xyz;
	if (VERTEX.z > player_view.z + 0.35) {
		float dist = length((VERTEX.xy - player_view.xy) / fade_radii);
		if (dist < 1.0) {
			float t = 1.0 - smoothstep(0.55, 1.0, dist);
			float noise = fract(sin(dot(floor(FRAGCOORD.xy / 2.0), vec2(12.9898, 78.233))) * 43758.5453);
			if (noise < t * 0.85) {
				discard;
			}
		}
	}
}
