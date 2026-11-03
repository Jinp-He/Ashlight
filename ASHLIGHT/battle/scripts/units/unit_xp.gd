class_name UnitXp
extends Node2D

## 单位经验条组件。与血条（UnitHp）共享位置和视觉风格，但方向相反：0→100（从零到满）。
## 默认隐藏（visible=false），由外部切换显示。
## 居中铁律：本组件内部所有零件以父节点(UnitRoot)原点为基准，整体 x 居中于 0。

const XP_FILL_PATH: String = "res://assets/Unit UI/unit_hud/xp_bar_fill.png"
const XP_BG_PATH: String = "res://assets/Unit UI/unit_hud/hp_bar_bg.png"  # 共用血条空槽作为经验条空槽
const FONT_PATH: String = "res://assets/fonts/DouyinSansBold.otf"

signal xp_changed(new_current: float, new_max: float)
signal level_up(new_level: int)

@export var xp_font_size: int = 20 :
	set(value):
		xp_font_size = value
		_refresh_label_font()

@export var xp_outline_size: int = 6 :
	set(value):
		xp_outline_size = value
		_refresh_label_font()

var _bar: TextureProgressBar
var _xp_text: Label
var _bar_size: Vector2 = Vector2.ZERO

var _max_xp: float = 100.0
var _cur_xp: float = 0.0
var _current_level: int = 1

func _ready() -> void:
	_build()
	visible = false  # 默认隐藏，由外部切换

func _build() -> void:
	var fill_tex := load(XP_FILL_PATH) as Texture2D
	var bg_tex := load(XP_BG_PATH) as Texture2D

	if fill_tex == null or bg_tex == null:
		push_warning("UnitXp: 经验条美术缺失 %s / %s" % [XP_FILL_PATH, XP_BG_PATH])
		return

	# 关键：布局尺寸取【共用空槽 hp_bar_bg】的尺寸(143×47)，且经验条满槽图 xp_bar_fill.png
	# 已重采样为相同尺寸(143×47)。这样空/满/文字三层矩形与血条(UnitHp)逐层严格对齐
	# （空对空/满对满/文字对文字）。注意 Godot 4.6 的 TextureProgressBar 已无 expand 属性，
	# 进度纹理按原生尺寸绘制，故必须让满槽源图与控件尺寸一致才能精确叠在空槽上。
	_bar_size = bg_tex.get_size()

	# 经验条：texture_progress=绿色满值 / texture_under=共用血条空槽
	# 方向 0→100（从左到右填充，value 从 0 增长到 max）
	_bar = TextureProgressBar.new()
	_bar.name = "XpBar"
	_bar.fill_mode = TextureProgressBar.FILL_LEFT_TO_RIGHT
	_bar.texture_progress = fill_tex
	_bar.texture_under = bg_tex
	_bar.min_value = 0.0
	_bar.max_value = _max_xp
	_bar.value = _cur_xp
	_bar.size = _bar_size
	_bar.position = Vector2(-_bar_size.x * 0.5, 0.0)
	add_child(_bar)

	_xp_text = Label.new()
	_xp_text.name = "XpText"
	_xp_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_xp_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_xp_text.text = _xp_label()
	var font := load(FONT_PATH) as Font
	if font != null:
		var ls := LabelSettings.new()
		ls.font = font
		ls.font_size = xp_font_size
		ls.outline_size = xp_outline_size
		ls.outline_color = Color.BLACK
		_xp_text.label_settings = ls
	_xp_text.size = _bar_size
	_xp_text.position = Vector2(-_bar_size.x * 0.5, 2.0)  # 与 HP 文字对齐
	add_child(_xp_text)

func setup_stats(max_xp: float, cur_xp: float, level: int = 1) -> void:
	_max_xp = maxf(max_xp, 1.0)
	_cur_xp = clampf(cur_xp, 0.0, _max_xp)
	_current_level = maxi(level, 1)
	if is_instance_valid(_bar):
		_bar.max_value = _max_xp
		_bar.value = _cur_xp
	if is_instance_valid(_xp_text):
		_xp_text.text = _xp_label()

func add_xp(amount: float) -> void:
	_cur_xp += amount
	if is_instance_valid(_bar):
		_bar.value = _cur_xp
	if is_instance_valid(_xp_text):
		_xp_text.text = _xp_label()
	xp_changed.emit(_cur_xp, _max_xp)

## 检查当前经验是否达到升级阈值（不自动处理升级逻辑，仅返回是否满足）。
func check_level_up() -> bool:
	return _cur_xp >= _max_xp

## 执行升级：等级+1、清零当前经验（保留溢出）、更新显示。
## 返回新等级。
func do_level_up() -> int:
	_current_level += 1
	var overflow := _cur_xp - _max_xp
	_cur_xp = overflow  # 溢出经验滚入下一级
	if is_instance_valid(_bar):
		_bar.value = _cur_xp
	if is_instance_valid(_xp_text):
		_xp_text.text = _xp_label()
	level_up.emit(_current_level)
	return _current_level

func set_max_xp(v: float) -> void:
	_max_xp = maxf(v, 1.0)
	if is_instance_valid(_bar):
		_bar.max_value = _max_xp
		_bar.value = _cur_xp
	if is_instance_valid(_xp_text):
		_xp_text.text = _xp_label()

func get_current_xp() -> float:
	return _cur_xp

func get_max_xp() -> float:
	return _max_xp

func get_level() -> int:
	return _current_level

func _xp_label() -> String:
	return "%d/%d" % [int(_cur_xp), int(_max_xp)]

func _refresh_label_font() -> void:
	if not is_instance_valid(_xp_text) or _xp_text.label_settings == null:
		return
	_xp_text.label_settings.font_size = xp_font_size
	_xp_text.label_settings.outline_size = xp_outline_size
