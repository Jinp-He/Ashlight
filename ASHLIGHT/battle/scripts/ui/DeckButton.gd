class_name DeckButton
extends TextureButton

## 牌库 / 弃牌库按钮：当前无功能，仅实现悬停 / 按下视觉反馈。
## 结构：自身 TextureButton 显示背景（texture_normal）；子节点 TextureRect 为图标，
##       层级在背景之上、与背景居中组合（顶左共享锚点居中）。
## 视觉规则：
##   常态  —— 图标 modulate = #bababa、scale = 1.0、原位；
##   悬停  —— 图标放大 1.2、变白；
##   按下  —— 保持 1.2 大小、颜色回 #bababa、向下位移 5px；
##   移开  —— 大小 / 颜色 / 位置全部复位。
## 所有变化经 tween 平滑过渡，不生硬跳变。

const ICON_COLOR_NORMAL: Color = Color(0.729, 0.729, 0.729, 1.0)  # #bababa
const ICON_COLOR_HOVER: Color = Color(1.0, 1.0, 1.0, 1.0)        # 白色
const HOVER_SCALE: float = 1.1
const PRESS_OFFSET_Y: float = 5.0
const TWEEN_TIME: float = 0.12

@onready var _icon: TextureRect = $Icon

## 所属牌堆分组（运行时 add_to_group，供 HandManager 定位其全局中心，作为手牌飞入/飞出点）。
@export var pile_group: StringName = &""

## 图标在按钮局部坐标下的「原位」（居中组合锚点），所有位移 tween 以其为绝对基准，
## 避免快速连续触发时基于实时 position 累积误差。
var _home_pos: Vector2 = Vector2.ZERO

func _ready() -> void:
	if pile_group != &"":
		add_to_group(pile_group)
	mouse_entered.connect(_on_mouse_entered)
	mouse_exited.connect(_on_mouse_exited)
	button_down.connect(_on_button_down)
	button_up.connect(_on_button_up)
	_setup_icon()

func _setup_icon() -> void:
	if _icon == null or _icon.texture == null:
		return
	var bg_size: Vector2 = (texture_normal as Texture2D).get_size()
	var ic_size: Vector2 = (_icon.texture as Texture2D).get_size()
	# 居中组合：图标中心与背景中心重合（顶左共享锚点）。
	_home_pos = (bg_size - ic_size) * 0.5
	_icon.position = _home_pos
	_icon.pivot_offset = ic_size * 0.5   # 围绕中心缩放 / 位移
	_icon.modulate = ICON_COLOR_NORMAL

## 平滑过渡到目标：缩放 / 颜色 / 纵向位移（三者并行）。offset_y 单位为像素。
func _apply_icon(scale_v: float, color: Color, offset_y: float) -> void:
	if _icon == null:
		return
	var tween := create_tween().set_parallel(true)
	tween.tween_property(_icon, "scale", Vector2(scale_v, scale_v), TWEEN_TIME).set_ease(Tween.EASE_OUT)
	tween.tween_property(_icon, "modulate", color, TWEEN_TIME).set_ease(Tween.EASE_OUT)
	tween.tween_property(_icon, "position", _home_pos + Vector2(0.0, offset_y), TWEEN_TIME).set_ease(Tween.EASE_OUT)

func _on_mouse_entered() -> void:
	_apply_icon(HOVER_SCALE, ICON_COLOR_HOVER, 0.0)

func _on_mouse_exited() -> void:
	# 复位：大小 / 颜色 / 位置全部还原。
	_apply_icon(1.0, ICON_COLOR_NORMAL, 0.0)

func _on_button_down() -> void:
	# 保持放大大小，颜色回 #bababa，向下位移 5px 表示按下。
	_apply_icon(HOVER_SCALE, ICON_COLOR_NORMAL, PRESS_OFFSET_Y)

func _on_button_up() -> void:
	# 松开：若光标仍在按钮上 → 回到悬停态；否则复位。
	if is_hovered():
		_apply_icon(HOVER_SCALE, ICON_COLOR_HOVER, 0.0)
	else:
		_apply_icon(1.0, ICON_COLOR_NORMAL, 0.0)
