class_name AimLine
extends Node2D

## 卡牌 → 鼠标的虚线瞄准指示（二次贝塞尔弧度 + 末端箭头）。
## 纯代码绘制（_draw），不依赖任何贴图。父节点(HandManager)为 identity 变换 → 局部坐标即世界坐标；
## 调用方已用 to_local 转换世界点，本节点统一在局部坐标绘制。

@export_group("Style")
@export var line_color: Color = Color(1.0, 1.0, 1.0, 0.9)
@export var line_width: float = 3.0
@export var dash_len: float = 16.0       # 实线段长度(px)
@export var gap_len: float = 11.0        # 段间空隙(px)
@export var curve_factor: float = 0.18   # 弧高 = 起终点距离 × 系数（恒向屏幕上方鼓）
@export var arrow_size: float = 16.0     # 箭头两翼长度(px)
@export var arrow_spread: float = 0.5    # 箭头两翼半张角(rad)
@export var samples: int = 48            # 贝塞尔采样段数（越大越平滑）

var _points: PackedVector2Array = []


func _draw() -> void:
	if _points.size() < 2:
		return
	_draw_dashed()
	_draw_arrow()


## 沿曲线按弧长画虚线（dash+gap 交替）。
func _draw_dashed() -> void:
	var acc := 0.0
	var drawing := true
	for i in range(_points.size() - 1):
		var a := _points[i]
		var b := _points[i + 1]
		var seg := b - a
		var seg_len := seg.length()
		if seg_len < 0.0001:
			continue
		var dir := seg / seg_len
		var pos := 0.0
		while pos < seg_len:
			var budget := (dash_len if drawing else gap_len) - acc
			var step := mini(seg_len - pos, budget)
			if step <= 0.0:
				break
			if drawing:
				var p0 := a + dir * pos
				var p1 := a + dir * (pos + step)
				draw_line(p0, p1, line_color, line_width, true)
			pos += step
			acc += step
			if acc >= (dash_len if drawing else gap_len) - 0.0001:
				acc = 0.0
				drawing = not drawing


## 末端画箭头（chevron 指向运动方向）。
func _draw_arrow() -> void:
	if _points.size() < 2:
		return
	var tip := _points[-1]
	var prev := _points[-2]
	var ang := (tip - prev).angle()
	var back := ang + PI   # 翼从 tip 向后展开，形成指向光标的箭头
	var left := tip + Vector2(cos(back + arrow_spread), sin(back + arrow_spread)) * arrow_size
	var right := tip + Vector2(cos(back - arrow_spread), sin(back - arrow_spread)) * arrow_size
	draw_line(tip, left, line_color, line_width, true)
	draw_line(tip, right, line_color, line_width, true)


## 设置起点/终点并重算曲线。p0/p2 为父节点局部坐标（调用方用 to_local 转换世界点）。
func set_endpoints(p0: Vector2, p2: Vector2) -> void:
	var mid := (p0 + p2) * 0.5
	var d := p2 - p0
	var dist := d.length()
	if dist < 0.0001:
		_points = PackedVector2Array([p0, p2])
		queue_redraw()
		return
	# 垂直方向：取指向上方(-y)的那一侧 → 弧线恒向屏幕上方鼓（"甩出去"观感）
	var perp := Vector2(-d.y, d.x).normalized()
	if perp.y > 0.0:
		perp = -perp
	var ctrl := mid + perp * (dist * curve_factor)
	_points = PackedVector2Array()
	_points.resize(samples + 1)
	for i in range(samples + 1):
		var t := float(i) / float(samples)
		var mt := 1.0 - t
		var x := mt * mt * p0.x + 2.0 * mt * t * ctrl.x + t * t * p2.x
		var y := mt * mt * p0.y + 2.0 * mt * t * ctrl.y + t * t * p2.y
		_points[i] = Vector2(x, y)
	queue_redraw()


## 隐藏指示（清空点集，避免每帧冗余重绘）。
func hide_line() -> void:
	if _points.is_empty():
		return
	_points = PackedVector2Array()
	queue_redraw()
