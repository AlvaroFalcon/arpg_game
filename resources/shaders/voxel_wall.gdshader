// Voxel wall that dithers away where it would hide the player (HD-2D view,
// docs/design/art-pipeline.md -> "Voxels"). Walls between the camera and the
// player (in front of them on screen) fade with a screen-door dither so you
// can always see yourself and what you are fighting.
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform vec4 albedo : source_color = vec4(1.0);
uniform vec3 player_pos = vec3(0.0, -100.0, 0.0);
uniform float fade_radius = 2.8;

varying vec3 world_pos;

void vertex() {
	world_pos = (MODEL_MATRIX * vec4(VERTEX, 1.0)).xyz;
}

void fragment() {
	ALBEDO = albedo.rgb;
	ROUGHNESS = 1.0;
	vec2 d = world_pos.xz - player_pos.xz;
	float dist = length(vec2(d.x, d.y * 0.6));
	if (world_pos.z > player_pos.z - 0.2 && world_pos.y > 0.3 && dist < fade_radius) {
		float t = 1.0 - smoothstep(fade_radius * 0.55, fade_radius, dist);
		float noise = fract(sin(dot(floor(FRAGCOORD.xy / 2.0), vec2(12.9898, 78.233))) * 43758.5453);
		if (noise < t * 0.8) {
			discard;
		}
	}
}
