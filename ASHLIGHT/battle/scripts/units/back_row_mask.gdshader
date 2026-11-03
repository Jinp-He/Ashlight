shader_type canvas_item;
uniform float mask_darkness : hint_range(0.0, 1.0) = 0.85;
uniform bool invert_gradient = false;
// 渐变起始高度(UV.y)：越小则压暗覆盖越靠头部、面积越大。
// 0.5 = 仅下半身(腰→脚)，0.4 = 略过腰线往上一截，0.3 = 覆盖到大半身高。
uniform float mask_top : hint_range(0.0, 1.0) = 0.4;

void fragment() {
	vec4 base = texture(TEXTURE, UV);
	// UV.y: 0 = 顶部(头), 1 = 底部(脚)。
	// 默认(非翻转): 脚(黑)→ mask_top(透)；invert_gradient: 头(黑)→ mask_top(透)。
	float p = UV.y;
	float ramp = invert_gradient
		? clamp((mask_top - p) / max(mask_top, 0.001), 0.0, 1.0)
		: clamp((p - mask_top) / max(1.0 - mask_top, 0.001), 0.0, 1.0);
	COLOR = base;
	// 透明像素(step=0)不参与压暗 → 遮罩仅作用于美术剪影，非方形覆盖
	COLOR.rgb = mix(base.rgb, vec3(0.0), ramp * mask_darkness * step(0.001, base.a));
}
