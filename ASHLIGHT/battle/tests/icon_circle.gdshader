shader_type canvas_item;
uniform float border_fraction = 0.01704545; // 白圆85px，外加双侧1.5px黑边；2倍后黑边3px。
void fragment() {
	float radius = length(UV - vec2(0.5));
	float aa = fwidth(radius);
	float alpha = 1.0 - smoothstep(0.5-aa,0.5,radius);
	float border = smoothstep(0.5-border_fraction-aa,0.5-border_fraction,radius);
	COLOR = vec4(vec3(1.0-border),alpha);
}
