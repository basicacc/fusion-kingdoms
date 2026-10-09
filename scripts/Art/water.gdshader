shader_type canvas_item;

// The baked ground, with moving glints on water tiles.
uniform sampler2D water_mask : filter_nearest;
uniform vec2 map_tiles = vec2(128.0, 96.0);

void fragment() {
	vec4 c = texture(TEXTURE, UV);
	float wm = texture(water_mask, UV).r;
	if (wm > 0.0) {
		vec2 px = floor(UV * map_tiles * 16.0);
		float a = sin(px.x * 0.33 + TIME * 1.4 + sin(px.y * 0.47 + TIME * 0.8) * 1.6);
		float b = sin(px.y * 0.61 - TIME * 1.1 + px.x * 0.05);
		float wave = a * b;
		c.rgb += vec3(0.02, 0.035, 0.05) * wave * wm;
		c.rgb += vec3(0.20, 0.26, 0.30) * step(0.88, wave) * wm;
	}
	COLOR = c;
}
