shader_type canvas_item;
render_mode blend_add;

// 单位「闪白」着色器（演出编排器·缓动区内命中闪白）。
// 以单位美术纹理的 alpha 作蒙版：纯白 RGB 填充严格限制在美术轮廓内 → 绝不溢出单位美术图，
// 透明背景区 alpha=0 不参与叠加，故不会出现方块/矩形白斑。
// 强度 u_strength 由外部 Tween 驱动：进入缓动区 0→峰值、离开缓动区 →0。
uniform float u_strength : hint_range(0.0, 1.0) = 0.0;

void fragment() {
	float mask = texture(TEXTURE, UV).a;
	float a = mask * u_strength;
	COLOR = vec4(vec3(1.0), a);
}
