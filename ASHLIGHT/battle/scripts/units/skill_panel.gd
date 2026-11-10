extends Node2D
@export var panel_width: float = 200.0
@export var expand_duration: float = 0.18
@export var anchor_offset: Vector2 = Vector2(0, -200)
var is_open: bool = false
var _unit: UnitRoot
var _anchor: Marker2D
var _panel: Panel
var _text: RichTextLabel
var _tween: Tween
var _height: float = 90

func _ready() -> void:
	_unit = get_parent() as UnitRoot
	_anchor = Marker2D.new()
	_anchor.name = "SkillExpandAnchor"
	_unit.add_child(_anchor)
	z_as_relative = false
	z_index = 90
	_panel = Panel.new()
	_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_panel.clip_contents = true
	var style := StyleBoxFlat.new()
	style.bg_color = Color.BLACK
	style.set_corner_radius_all(10)
	_panel.add_theme_stylebox_override("panel", style)
	add_child(_panel)
	_text = RichTextLabel.new()
	_text.bbcode_enabled = true
	_text.scroll_active = false
	_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_text.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_text.add_theme_font_override("normal_font", preload("res://assets/fonts/DouyinSansBold.otf"))
	_text.add_theme_font_override("bold_font", preload("res://assets/fonts/Biantao.ttf"))
	_text.add_theme_font_size_override("normal_font_size", 18)
	_text.add_theme_font_size_override("bold_font_size", 20)
	_text.add_theme_color_override("default_color", Color.WHITE)
	_text.position = Vector2(14, 12)
	_text.size = Vector2(panel_width - 28, 300)
	_panel.add_child(_text)
	_panel.visible = false
	_process(0)

func _process(_delta: float) -> void:
	if not is_instance_valid(_unit) or not is_instance_valid(_unit._countdown_component):
		return
	_anchor.position = _unit._countdown_component.position + anchor_offset
	position = _anchor.position

func show_skill(skill: Dictionary) -> void:
	is_open = true
	_text.text = "[b][color=#%s]%s[/color][/b]\n%s" % [IntentRoot.COLOR_INTENT.to_html(false), skill.name, skill.desc]
	# 换行由宽度约束，面板顶边保持在展开锚点，内容只向下延展。
	_height = maxf(80, float(_text.get_content_height()) + 24)
	_text.size.y = _height - 24
	_panel.visible = true
	if is_instance_valid(_tween): _tween.kill()
	_tween = create_tween()
	_tween.tween_method(_set_width, _panel.size.x if _panel.size.x > 0 else 1.0, panel_width, expand_duration).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)

func close() -> void:
	is_open = false
	if is_instance_valid(_tween): _tween.kill()
	_tween = create_tween()
	_tween.tween_method(_set_width, _panel.size.x, 0.0, expand_duration).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
	_tween.tween_callback(_panel.hide)

func _set_width(width: float) -> void:
	_panel.size = Vector2(width, _height)
	_panel.position = Vector2(-_panel.size.x * 0.5, 0)