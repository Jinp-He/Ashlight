class_name CutsceneRing
extends Node2D

## 演出滑入起手时的「扩散圆环」效果：
##   从半径 0 快速放大，透明度随半径增长而渐隐，到达 max_radius 时恰好消散后自动销毁。
## 由 CutsceneController 在「滑入起手」实例化并 play()；进入缓动区时被施加时间缩放(慢放)，离开恢复。动画结束自动 queue_free。
## 用 _draw + draw_arc 绘制，圆环粗细(ring_thickness)在扩散过程中保持恒定（不受缩放影响）。
## 所有数值均为 @export，可直接在 cutscene_ring.tscn 中调参、配合 cutscene_preview 的「迸发&圆环」按钮孤立预览。

@export_group("扩散圆环")
@export var ring_thickness: float = 4.0        # 圆环粗细(px)——收窄：8→4（更纤细）
@export var ring_spread_speed: float = 320.0   # 圆环扩散速度(px/s，半径增长速度)——调慢：800→320
@export var ring_max_radius: float = 240.0     # 圆环最大半径(px)：扩散到此即消散——加大：120→240（扩散范围更大）
@export var ring_color: Color = Color(1.0, 1.0, 1.0, 1.0)  # 圆环颜色（默认白）

var _radius: float = 0.0
var _active: bool = false
var _time_scale: float = 1.0   # 时间缩放：缓动区内由编排器设为 <1 实现慢放，离开恢复 1.0

func _ready() -> void:
	# 层级：单位美术(z1)/闪白(z2) 之上，buff提示(z6)/伤害数字(z7) 之下。
	z_index = 5

## 开始扩散（半径从 0 起，每帧增长）。
func play() -> void:
	_active = true
	queue_redraw()

## 由编排器在缓动区进入/离开时调用，对存活实例施加/恢复时间缩放（<1=慢放）。
func set_time_scale(f: float) -> void:
	_time_scale = f

func _process(delta: float) -> void:
	if not _active:
		return
	var dt: float = delta * _time_scale
	_radius += ring_spread_speed * dt
	if _radius >= ring_max_radius:
		queue_free()
		return
	queue_redraw()

func _draw() -> void:
	if not _active or _radius <= 0.0:
		return
	# 透明度随半径线性渐隐：半径 0→最大 对应 不透明→全透明，到达 max_radius 时恰好消失。
	var a: float = 1.0 - (_radius / maxf(ring_max_radius, 0.0001))
	a = clampf(a, 0.0, 1.0)
	var col := Color(ring_color.r, ring_color.g, ring_color.b, ring_color.a * a)
	var segs: int = maxi(int(_radius * 0.5), 16)
	# draw_arc(center, radius, start, end, point_count, color, width, antialiased)
	draw_arc(Vector2.ZERO, _radius, 0.0, TAU, segs, col, ring_thickness, true)
