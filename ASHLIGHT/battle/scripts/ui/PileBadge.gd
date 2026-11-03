class_name PileBadge
extends Control

## 牌库 / 弃牌堆角标：25px 圆形（#00a1ff 填充 + 3px 黑描边），圆心显示剩余张数。
## 张数由牌库系统接入后调用 set_count(n) 实时刷新；当前牌库系统未实现，初始显示 0 占位。
## 自包含：_ready 内创建 Label 子节点（DouyinSansBold 13px 白），无需外部 .tscn 子节点。

const BADGE_SIZE: float = 25.0
const FILL_COLOR: Color = Color(0.0, 0.631, 1.0, 1.0)   # #00a1ff
const STROKE_COLOR: Color = Color(0.0, 0.0, 0.0, 1.0)   # 3px 黑描边
const STROKE_W: float = 3.0
const FONT_PATH: String = "res://assets/fonts/DouyinSansBold.otf"
const TEXT_COLOR: Color = Color(1.0, 1.0, 1.0, 1.0)     # 白色
const TEXT_SIZE: int = 13

var _label: Label = null

func _ready() -> void:
	custom_minimum_size = Vector2(BADGE_SIZE, BADGE_SIZE)
	size = Vector2(BADGE_SIZE, BADGE_SIZE)
	var lbl := Label.new()
	lbl.name = "Label"
	lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	lbl.size = size
	var f := load(FONT_PATH) as Font
	if f != null:
		lbl.add_theme_font_override("font", f)
	lbl.add_theme_font_size_override("font_size", TEXT_SIZE)
	lbl.add_theme_color_override("font_color", TEXT_COLOR)
	add_child(lbl)
	_label = lbl
	set_count(0)
	queue_redraw()

func _draw() -> void:
	var c := Vector2(BADGE_SIZE * 0.5, BADGE_SIZE * 0.5)
	var r_outer := BADGE_SIZE * 0.5
	# 黑描边底圆（整圆，含 3px 边）
	draw_circle(c, r_outer, STROKE_COLOR)
	# 蓝色填充圆：半径减去半描边宽，露出 3px 黑边
	draw_circle(c, r_outer - STROKE_W * 0.5, FILL_COLOR)

## 更新角标数字（牌库系统接入后实时调用）。
func set_count(n: int) -> void:
	if _label != null:
		_label.text = str(n)
