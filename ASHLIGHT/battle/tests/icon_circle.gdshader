shader_type canvas_item;
uniform float border_fraction = 0.04062839; // 3 / 73.84：父节点2倍后，黑边显示3px。
void fragment() {
	float radius = length(UV - vec2(0.5));
	float aa = fwidth(radius);
	float alpha = 1.0 - smoothstep(0.5-aa,0.5,radius);
	float border = smoothstep(0.5-border_fraction-aa,0.5-border_fraction,radius);
	COLOR = vec4(vec3(1.0-border),alpha);
}
