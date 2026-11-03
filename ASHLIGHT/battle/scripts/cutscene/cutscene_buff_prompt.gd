class_name CutsceneBuffPrompt
extends Node2D

## 演出编排器「buff/debuff 获取提示」模块（第 58 轮迭代 · 模块化）。
## 与 UnitBuff 的滑入提示同源，但作为编排器插槽内的独立模块管理：
##   - Icon：读取 buff 美术资源图（res://assets/Unit UI/buffs/icons/{name}.png，缺则按 tone 兜底 buff001/buff002）；
##   - Toast：滑入提示条（复用 UnitBuff 的 buff_toast.png + 名称文字）。
## 布局：Icon(左) + 文字(右) 水平成组，Icon 与文字 Y 轴居中、间隔 icon_text_gap(3px)，
##   整组（Icon+文字）居中于 Toast 图案内（Toast 中心 = 模块原点 0,0）——由调用方把模块原点摆到单位美术指定位置。
## 进入缓动区 → setup + play_in（整组从上方滑入+淡入）；离开缓动区 → play_out（淡出销毁）。
## 仅含裸美术演出层，不含血条/层数框等部件（与编排器单位图一致）。

const BUFF_ICON_DIR: String = "res://assets/Unit UI/buffs/icons/"
const BUFF_TOAST_PATH: String = "res://assets/Unit UI/unit_hud/buff_toast.png"
const FONT_PATH: String = "res://assets/fonts/DouyinSansBold.otf"

@export_group("Layout")
@export var icon_size: Vector2 = Vector2(28.0, 28.0)   # 图标显示尺寸（缩放按 icon_scale 再乘）
@export var icon_scale: float = 1.0                    # 图标整体缩放
@export var icon_text_gap: float = 3.0                 # 图标右缘 → 提示文字左缘 的水平间距(px)
@export var toast_font_size: int = 18

var _icon: Sprite2D
var _toast_bg: TextureRect
var _label: Label
var _font: Font

func _ready() -> void:
	_font = load(FONT_PATH) as Font
	_build()

## 构建提示组（Toast 容器：背景图 + Icon + 文字）。Icon 置于文字左侧、与文字 Y 轴居中，
## Icon + 文字 整体水平居中于 Toast 图案内（Toast 中心 = 模块原点(0,0)）。
func _build() -> void:
	var toast := Node2D.new()
	toast.name = "Toast"
	var bg_tex := load(BUFF_TOAST_PATH) as Texture2D
	var sz := bg_tex.get_size() if bg_tex != null else Vector2(165.0, 34.0)
	_toast_bg = TextureRect.new()
	_toast_bg.name = "ToastBg"
	if bg_tex != null:
		_toast_bg.texture = bg_tex
	_toast_bg.size = sz
	_toast_bg.position = -sz * 0.5
	_toast_bg.mouse_filter = Control.MOUSE_FILTER_IGNORE
	toast.add_child(_toast_bg)
	# --- Icon（读取 buff 美术资源图，置于文字左侧、与文字 Y 轴居中）---
	_icon = Sprite2D.new()
	_icon.name = "Icon"
	_icon.centered = true
	_icon.scale = Vector2.ONE * icon_scale
	toast.add_child(_icon)
	# --- 文字（名称，居中于 Toast 图案）---
	_label = Label.new()
	_label.name = "ToastLabel"
	_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	if _font != null:
		var ls := LabelSettings.new()
		ls.font = _font
		ls.font_size = toast_font_size
		ls.font_color = Color(1.0, 1.0, 1.0, 1.0)
		ls.outline_size = 0
		_label.label_settings = ls
	toast.add_child(_label)
	add_child(toast)
	_relayout()

## Icon(左) + 文字(右) 水平成组：两部件 Y 轴居中对齐在 Toast 中心(模块原点 0,0)，
## 整组在 Toast 图案内水平居中（间隔 = icon_text_gap）。
func _relayout() -> void:
	if not is_instance_valid(_toast_bg):
		return
	var sz := _toast_bg.size
	var icon_w: float = icon_size.x * icon_scale
	# 文字实测宽度（取抖音美好体下该名称的渲染宽度；无字体/无文字时回退 toast 宽一半）
	var label_w: float = 0.0
	if _font != null and is_instance_valid(_label):
		label_w = _font.get_string_size(_label.text, HORIZONTAL_ALIGNMENT_CENTER, -1, toast_font_size).x
	if label_w <= 0.0:
		label_w = sz.x * 0.5
	var content_w: float = icon_w + icon_text_gap + label_w
	var start_x: float = -content_w * 0.5
	# Icon：垂直居中于 Toast（y=0），水平位于整组左端
	if is_instance_valid(_icon):
		_icon.position = Vector2(start_x + icon_w * 0.5, 0.0)
	# 文字：宽度撑满 label_w，左上角定位使其水平居中段落在 Toast 中心(y=0)
	if is_instance_valid(_label):
		_label.size = Vector2(label_w, sz.y)
		_label.position = Vector2(start_x + icon_w + icon_text_gap, -sz.y * 0.5)

## 填充数据：图标纹理 + 益害着色 + 提示文字。
## icon_name：指定图标文件名（不含 .png）；留空则按 tone 兜底 buff001(中性)/buff002(有害)。
## 注意：buff_name 是展示标签，不是图标文件名（目录里只有 buff001/buff002 兜底图），故不再拿 buff_name 当文件名去 load，
## 否则对「士气/出血」等真实 buff 会持续刷 Resource file not found 错误。加载前用 ResourceLoader.exists 判定，避免无效 load 报错。
func setup(buff_name: String, tone: int, icon_name: String = "") -> void:
	# 图标纹理：优先 icon_name（显式指定），缺失则按 tone 兜底 buff001/buff002。
	var tex: Texture2D = null
	if icon_name != "":
		var p: String = BUFF_ICON_DIR + icon_name + ".png"
		if ResourceLoader.exists(p):
			tex = load(p) as Texture2D
	if tex == null:
		var fb: String = BUFF_ICON_DIR + ("buff002.png" if tone == 2 else "buff001.png")
		if ResourceLoader.exists(fb):
			tex = load(fb) as Texture2D
	if tex != null and is_instance_valid(_icon):
		_icon.texture = tex
		_icon.self_modulate = _tone_color(tone)
	# 提示条益害着色（柔边黑条叠淡绿/淡红）。
	if is_instance_valid(_toast_bg):
		match tone:
			1: _toast_bg.self_modulate = Color(0.72, 1.0, 0.72, 1.0)
			2: _toast_bg.self_modulate = Color(1.0, 0.72, 0.68, 1.0)
			_: _toast_bg.self_modulate = Color(1.0, 1.0, 1.0, 1.0)
	if is_instance_valid(_label):
		_label.text = buff_name
	_relayout()   # 文字定了再按实测宽度重排 icon+文字 居中位

## 进入缓动区：整组从模块原点上方 slide_dist 滑入 + 淡入（两部件共享模块 modulate，同步）。
## 调用方需先把模块 position 设到目标位（最终静止位），本函数读 position.y 作为落点。
func play_in(slide_dist: float = 20.0, in_time: float = 0.2) -> void:
	if not is_instance_valid(_icon) and not has_node("Toast"):
		return
	var target_y: float = position.y
	position.y = target_y - slide_dist
	modulate = Color(1.0, 1.0, 1.0, 0.0)
	var tw := create_tween().set_parallel(true)
	tw.tween_property(self, "position:y", target_y, in_time).set_ease(Tween.EASE_OUT)
	tw.tween_property(self, "modulate:a", 1.0, in_time)

## 离开缓动区：整组淡出后销毁（Icon + Toast 同随模块 modulate 一起淡出）。
func play_out(out_time: float = 0.14) -> void:
	var tw := create_tween().set_parallel(true)
	tw.tween_property(self, "modulate:a", 0.0, out_time)
	tw.tween_callback(queue_free).set_delay(out_time)

## 益害着色（与 UnitBuff._tone_color 一致）：直接染 icon 自身纹理（乘法 tint，保留图标亮度与细节）。
func _tone_color(tone: int) -> Color:
	match tone:
		1: return Color(0.55, 0.9, 0.5, 1.0)    # 有益：绿色调
		2: return Color(0.95, 0.5, 0.45, 1.0)   # 有害：红色调
		_: return Color(1.0, 1.0, 1.0, 1.0)     # 中性：不染色
