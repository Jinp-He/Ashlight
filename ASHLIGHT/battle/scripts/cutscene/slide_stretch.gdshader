shader_type canvas_item;

// 演出编排器「滑入形变」着色器（仅形变A：整体横向缩放）。
// 单位从屏外滑入/滑出时按离缓动区距离做横向拉伸：越靠近缓动区越接近原比例(1.0)，越远拉伸越大(→u_stretch)。
// 形变系数由上层(CutsceneController)以「整组锚点 _center_x 离缓动区中心的距离」统一计算并写入 u_stretch，
// 整组同步形变；仅作用于单位美术本身，buff 提示组 / 伤害数字为子节点、自有材质不受影响。
// （形变B·运动拖尾拉伸已移除，按用户要求只保留整体缩放。）

uniform float u_stretch : hint_range(1.0, 3.0) = 1.0;  // 横向拉伸倍数（1.0 = 原比例）

void vertex() {
	// 围绕中心整体横向放大（横向拉长）
	VERTEX.x *= u_stretch;
}

void fragment() {
	// Godot 4 画布规范：自定义 fragment 时直接把贴图赋给 COLOR（勿乘内建 COLOR，否则 RGB 被抹黑成黑剪影）。
	COLOR = texture(TEXTURE, UV);
}
