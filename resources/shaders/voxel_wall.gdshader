// Voxel wall that dithers away where it would hide the player (HD-2D view,
// docs/design/art-pipeline.md -> "Voxels"). Only wall that is actually
// between the camera and the player AND overlaps their silhouette on screen
// fades, with a screen-door dither, so you can always see yourself and what
// you are fighting. Both tests run in view space: the camera is orthographic,
// so view x/y is screen position and view z is depth. (The first version
// faded any wall near the player on the camera's side, so a wall you were
// merely standing next to turned to noise.)
shader_type spatial;
render_mode diffuse_lambert, specular_disabled;

uniform vec4 albedo : source_color = vec4(1.0);
// The player's feet, in world space: a global uniform ([shader_globals] in
// project.godot) that VoxelStage sets once per frame for every wall and rock.
global uniform vec3 player_pos;
// Silhouette ellipse around the player's chest, in metres on screen.
uniform vec2 fade_radii = vec2(1.1, 1.5);
uniform float chest_height = 0.75;

void fragment() {
	ALBEDO = albedo.rgb;
	ROUGHNESS = 1.0;
	vec3 player_view = (VIEW_MATRIX * vec4(player_pos + vec3(0.0, chest_height, 0.0), 1.0)).xyz;
	// In front of the player (closer to the camera), with a margin so the wall
	// the player is leaning against isn't counted.
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
