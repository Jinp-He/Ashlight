shader_type canvas_item;
uniform float white_amount : hint_range(0.0, 1.0) = 1.0;
void fragment() {
	vec4 card = texture(TEXTURE, UV) * COLOR;
	COLOR = vec4(mix(card.rgb, vec3(1.0), white_amount), card.a);
}
