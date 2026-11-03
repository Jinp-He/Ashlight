class_name UnitCountdown
extends Node2D
## 原点是整组底边中心。由UnitRoot的独立偏移定位。
const ICON := preload("res://assets/ui/unit_countdown.png")
const FONT := preload("res://assets/fonts/DouyinSansBold.otf")
## 固定行高，不随“就绪”20px/数字24px变化；保留原数字组的行高基准。
var _row_height: float = 24.0
var icon: TextureRect
var text_label: Label
var _warning_elapsed: float = 0.0

func _ready() -> void:
	icon = TextureRect.new()
	icon.name = "Icon"
	icon.texture = ICON
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.size = Vector2(24, 24)
	icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(icon)
	text_label = Label.new()
	text_label.name = "Value"
	text_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	text_label.add_theme_font_override("font", FONT)
	text_label.add_theme_font_size_override("font_size", 24)
	text_label.add_theme_constant_override("outline_size", 3)
	text_label.add_theme_color_override("font_outline_color", Color.BLACK)
	text_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	add_child(text_label)
	text_label.text = "0"
	_row_height = maxf(24.0, text_label.get_minimum_size().y)
	text_label.text = ""
	visible = false
	set_process(false)

func set_value(value: String, friendly: bool = false) -> void:
	var warning: bool = not friendly and value == "1"
	if warning and not is_processing():
		_warning_elapsed = 0.0
	set_process(warning)
	scale = Vector2.ONE * (1.2 if warning else 1.0)
	visible = not value.is_empty()
	text_label.text = "警告" if warning else value
	var ready: bool = value == "就绪"
	text_label.add_theme_font_size_override("font_size", 20 if ready else 24)
	text_label.add_theme_color_override("font_color", Color.WHITE)
	var size: Vector2 = text_label.get_minimum_size()
	var height: float = _row_height
	var width: float = 24.0 + 4.0 + size.x
	icon.position = Vector2(-width * 0.5, -height + (height - 24) * 0.5)
	text_label.size = Vector2(size.x, height)
	text_label.position = Vector2(-width * 0.5 + 28, -height)

func _process(delta: float) -> void:
	_warning_elapsed += delta
	var shake_x: float = sin(_warning_elapsed * TAU * 8.0) * 1.5
	icon.position.x = -(28.0 + text_label.size.x) * 0.5 + shake_x
	text_label.position.x = icon.position.x + 28.0