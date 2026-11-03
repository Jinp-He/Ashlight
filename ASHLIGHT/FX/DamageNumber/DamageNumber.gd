class_name DamageNumber
extends Node2D

signal hold_finished
@export_range(0.1, 3.0, 0.05) var duration_multiplier: float = 1.0
@export var popup_peak_scale: float = 1.2
@export var popup_rise: float = 15.0
@export var popup_rise_time: float = 0.2
@export var popup_settle_time: float = 0.2
@export var popup_hold_time: float = 1.0
@export var popup_fade_time: float = 0.2
var popup_anchor: Vector2
var popup_delay: float = 0.0
var popup_fading: bool = false
var popup_settled: bool = false
var _popup_tween: Tween

func blocking_duration() -> float:
	return (popup_rise_time + popup_settle_time) * duration_multiplier

func play_on_unit(unit: Node2D, delay: float = 0.0) -> void:
	var used: Array[Vector2] = []
	for popup in get_tree().get_nodes_in_group("animated_damage_numbers"):
		if popup.get_meta("target_instance", 0) == unit.get_instance_id():
			used.append(popup.get_meta("target_anchor"))
	var local_anchor := pick_anchor(unit, used)
	set_meta("target_instance", unit.get_instance_id())
	set_meta("target_anchor", local_anchor)
	play_popup((get_parent() as Node2D).to_local(unit.to_global(local_anchor)), delay)

## 所有标签是本节点的子节点，同一段的实际值/无效值一起弹出。
func play_popup(anchor: Vector2, delay: float = 0.0) -> void:
	if is_instance_valid(_popup_tween):
		_popup_tween.kill()
	popup_anchor = anchor
	popup_delay = delay
	popup_fading = false
	popup_settled = false
	position = anchor + Vector2(0, popup_rise)
	scale = Vector2.ONE
	modulate.a = 1.0
	visible = delay <= 0.0
	add_to_group("blocking_damage_numbers")
	add_to_group("animated_damage_numbers")
	_popup_tween = create_tween()
	if delay > 0:
		_popup_tween.tween_interval(delay)
	_popup_tween.tween_callback(show)
	_popup_tween.tween_property(self, "position", anchor, popup_rise_time * duration_multiplier).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	_popup_tween.parallel().tween_property(self, "scale", Vector2.ONE * popup_peak_scale, popup_rise_time * duration_multiplier).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	_popup_tween.tween_property(self, "scale", Vector2.ONE, popup_settle_time * duration_multiplier).set_trans(Tween.TRANS_QUAD).set_ease(Tween.EASE_OUT)
	_popup_tween.tween_callback(_begin_hold)
	_popup_tween.tween_interval(popup_hold_time * duration_multiplier)
	_popup_tween.tween_callback(_begin_fade)
	_popup_tween.tween_property(self, "modulate:a", 0.0, popup_fade_time * duration_multiplier)
	_popup_tween.tween_callback(queue_free)

func _begin_hold() -> void:
	popup_settled = true
	remove_from_group("blocking_damage_numbers")

func _begin_fade() -> void:
	popup_fading = true
	hold_finished.emit()

## 锚点只在判定框顶部170px内；各段中心至少相隔30px，与贴图尺寸无关。
static func pick_anchor(unit: Node2D, used: Array[Vector2]) -> Vector2:
	var area := Rect2(unit.activation_center - unit.activation_size * 0.5,
		Vector2(unit.activation_size.x, minf(170.0, unit.activation_size.y)))
	for attempt in 512:
		var candidate := area.position + Vector2(randf() * area.size.x, randf() * area.size.y)
		if _anchor_clear(candidate, used):
			return candidate
	# 随机采样拥挤时，扫描剩余空位，避免退化为重叠。
	for y in range(int(area.position.y), int(area.end.y) + 1, 2):
		for x in range(int(area.position.x), int(area.end.x) + 1, 2):
			var candidate := Vector2(x, y)
			if _anchor_clear(candidate, used):
				return candidate
	push_error("数字锚点区域已满，无法保持30px间距")
	return area.get_center()

static func _anchor_clear(point: Vector2, used: Array[Vector2]) -> bool:
	for existing in used:
		if point.distance_to(existing) < 30.0:
			return false
	return true

## 伤害数字组组件。最新已确认时序与无效规则见 docs/伤害数字动画.md。
## 两行结构：第一行=类型/补充信息标签(14px 恒定居中) + 第二行=数字(34/54px)。
## 真·外轮廓描边：Label.outline_size + outline_color（描边=黑85%×字色15%，颜色表 §3）。
## 普通伤害仅显数字不挂标签；非普通暴击放大加!保色；任何被挡住的数值统一显示「无效 -N」。
## spawn构建静态内容，play_on_unit/play_popup启动统一弹起动画。

const FONT_PATH: String = "res://assets/fonts/Biantao.ttf"           # 字魂扁桃体（README 指定，正式构建需确认商用授权）
const FONT_FALLBACK: String = "res://assets/fonts/DouyinSansBold.otf" # 回退字体

# 颜色表（字色 / 描边色，已定稿 §3）。描边 = 黑85% × 字色15%。
const C_NORMAL:   Color = Color(0.886, 0.149, 0.000)   # #e22600 普通伤害（暖红）
const C_NORMAL_O: Color = Color(0.133, 0.024, 0.000)   # #220600
const C_CRIT:     Color = Color(1.000, 0.514, 0.027)   # #ff8307 暴击（橙）
const C_CRIT_O:   Color = Color(0.149, 0.078, 0.004)   # #261401
const C_FAIL:     Color = Color(0.639, 0.659, 0.706)   # #A3A8B4 失效类（浅灰）
const C_FAIL_O:   Color = Color(0.094, 0.098, 0.106)   # #18191B
const C_TICK:     Color = Color(0.780, 0.608, 1.000)   # #C79BFF 结算·持续伤害（邪紫）
const C_TICK_O:   Color = Color(0.118, 0.090, 0.149)   # #1E1726
const C_HEAL:     Color = Color(0.361, 0.776, 0.000)   # #5cc600 治疗（翠绿）
const C_HEAL_O:   Color = Color(0.055, 0.118, 0.000)   # #0E1E00
const C_SHIELD:   Color = Color(0.188, 0.576, 0.796)   # #3093cb 护盾（天蓝）
const C_SHIELD_O: Color = Color(0.027, 0.086, 0.118)   # #07161E

const SIZE_NUM: int = 42      # 普通/结算/治疗/护盾 数字基准字号（2026-10-04 由34放大）
const SIZE_CRIT: int = 64     # 暴击放大字号（2026-10-04 由54放大）
const SIZE_LABEL: int = 25    # 标签 / 吸收 / 无效 小字（2026-10-04 由18放大到25）
const OUTLINE_BASE: int = 10  # 基础描边厚度（2026-10-04 加到10px）
const OUTLINE_CRIT: int = 10  # 暴击描边厚度（2026-10-04 统一到10px）
const LINE_GAP: float = 1.0       # 行高系数（2026-10-04 由1.15缩小，整体行距更紧凑）
const LABEL_SQUEEZE: float = 0.8 # 标签行额外收紧系数（使其更贴近下方数字）

var _font: Font = null
var content_height: float = 0.0   # 实际渲染总高（_build_layout 计算），供上层做多段间距用

func _ready() -> void:
	_font = load(FONT_PATH) as Font
	if _font == null:
		_font = load(FONT_FALLBACK) as Font

## 按 payload 构建伤害数字组。payload 字段见 README §9：
## {type, value, is_crit, source, absorbed_amount, blocked}
## type ∈ normal | crit | miss | absorb | tick | heal | shield
func spawn(payload: Dictionary) -> void:
	var type: String = String(payload.get("type", "normal"))
	var value: int = int(payload.get("value", 0))
	var is_crit: bool = bool(payload.get("is_crit", false))
	var source: String = String(payload.get("source", ""))
	var invalid: int = maxi(0, int(payload.get("invalid_amount", payload.get("blocked_amount", payload.get("absorbed_amount", 0)))))
	if is_crit and type == "normal":
		type = "crit"

	# 解析颜色 / 字号
	var tc: Color
	var oc: Color
	var num_size: int = SIZE_NUM
	var num_outline: int = OUTLINE_BASE
	match type:
		"crit":   tc = C_CRIT;   oc = C_CRIT_O
		"miss":   tc = C_FAIL;   oc = C_FAIL_O
		"absorb": tc = C_NORMAL; oc = C_NORMAL_O   # 穿透血量的真实伤害用原色
		"tick":   tc = C_TICK;   oc = C_TICK_O
		"heal":   tc = C_HEAL;   oc = C_HEAL_O
		"shield": tc = C_SHIELD; oc = C_SHIELD_O
		_:        tc = C_NORMAL; oc = C_NORMAL_O

	# 非普通类型 + 暴击 → 数字放大加!保色（标签恒定 14px 不放大、描边不变化）
	var big: bool = type == "crit" or (is_crit and type != "normal")
	if big:
		num_size = SIZE_CRIT
		num_outline = OUTLINE_CRIT

	# 第一行标签（仅非普通类型；普通伤害不挂标签）
	var label_text: String = ""
	match type:
		"crit":   label_text = "暴击"
		"tick":   label_text = source if source != "" else "持续"
		"heal":   label_text = "治疗"
		"shield": label_text = "护盾"
		"miss":   label_text = "未命中"
		_:        label_text = ""

	# 第二行数字（带正负号：伤害类为负、治疗/护盾为正；miss 无数字）
	var num_text: String = ""
	if type != "miss":
		var sign: String = "+" if (type == "heal" or type == "shield") else "-"
		num_text = sign + str(absi(value))
		if big:
			num_text += "!"

	if value == 0 and invalid > 0:
		label_text = ""
		num_text = ""
	_build_layout(label_text, num_text, tc, oc, num_size, num_outline, invalid)


## 行盒高度：基础按 LINE_GAP 缩放；标签行(贴数字)额外按 LABEL_SQUEEZE 收紧。
func _line_box(size: int, is_label_line: bool) -> float:
	var h: float = float(size) * LINE_GAP
	if is_label_line:
		h *= LABEL_SQUEEZE
	return h

## 组装标签行 + 数字行（+ 吸收/无效 浅灰小字行），整组以本节点原点(0,0)为垂直中心。
func _build_layout(label_text: String, num_text: String, tc: Color, oc: Color,
		num_size: int, num_outline: int, invalid: int) -> void:
	var lines: Array[Dictionary] = []

	if label_text != "":
		lines.append({"text": label_text, "size": SIZE_LABEL, "color": tc, "outline": OUTLINE_BASE, "ocolor": oc})
	if num_text != "":
		lines.append({"text": num_text, "size": num_size, "color": tc, "outline": num_outline, "ocolor": oc})

	if invalid > 0:
		lines.append({"text": "无效 -" + str(invalid), "size": SIZE_LABEL, "color": C_FAIL, "outline": OUTLINE_BASE, "ocolor": C_FAIL_O})

	# 计算总高，整组以原点为垂直中心堆叠
	# 行高系数 LINE_GAP 收紧整体行距；标签行(首行)额外 LABEL_SQUEEZE 使其更贴近数字
	var total_h: float = 0.0
	for i in lines.size():
		total_h += _line_box(lines[i]["size"], i == 0 and label_text != "")
	content_height = total_h   # 记录实际高度，供多段间距计算

	var y: float = -total_h * 0.5
	for i in lines.size():
		var ln: Dictionary = lines[i]
		var ltext: String = ln["text"]
		var lsize: int = ln["size"]
		var lcolor: Color = ln["color"]
		var loutline: int = ln["outline"]
		var locolor: Color = ln["ocolor"]
		var lh: float = _line_box(lsize, i == 0 and label_text != "")
		var lbl := Label.new()
		lbl.name = "Line%d" % i
		lbl.text = ltext
		lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
		lbl.size = Vector2(600.0, lh)
		lbl.position = Vector2(-300.0, y)
		if _font != null:
			var ls := LabelSettings.new()
			ls.font = _font
			ls.font_size = lsize
			ls.font_color = lcolor
			ls.outline_size = loutline
			ls.outline_color = locolor
			lbl.label_settings = ls
		add_child(lbl)
		y += lh
