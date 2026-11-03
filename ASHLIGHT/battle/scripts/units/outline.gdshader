shader_type canvas_item;
uniform vec4 outline_color : source_color = vec4(1.0);
uniform float outline_thickness : hint_range(0.0, 16.0) = 1.5;

void fragment() {
	vec2 texel = outline_thickness / vec2(textureSize(TEXTURE, 0));
	float a = 0.0;
	a = max(a, texture(TEXTURE, UV + vec2(texel.x, 0.0)).a);
	a = max(a, texture(TEXTURE, UV - vec2(texel.x, 0.0)).a);
	a = max(a, texture(TEXTURE, UV + vec2(0.0, texel.y)).a);
	a = max(a, texture(TEXTURE, UV - vec2(0.0, texel.y)).a);
	a = max(a, texture(TEXTURE, UV + texel).a);
	a = max(a, texture(TEXTURE, UV - texel).a);
	a = max(a, texture(TEXTURE, UV + vec2(texel.x, -texel.y)).a);
	a = max(a, texture(TEXTURE, UV + vec2(-texel.x, texel.y)).a);
	float base_a = texture(TEXTURE, UV).a;
	float outline_a = max(a - base_a, 0.0);
	vec4 base = texture(TEXTURE, UV);
	COLOR.a = max(base_a, outline_a);
	COLOR.rgb = mix(base.rgb, outline_color.rgb, outline_a);
}
