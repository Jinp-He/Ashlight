class_name TriggerPrompt
extends Control

## 触发提示组（Trigger Prompt）：黑色左右虚化横条背景 + 单位格02插图 + 触发文本。
## 黑条尺寸自动贴合内容（单位格02 49x30 原尺寸 + 文字 + 内边距），不再固定巨大尺寸。
## 阵营决定滑入方向：我方(player) 从左侧滑入；敌方(enemy) 从右侧滑入（内容镜像右对齐，从右缘窥入）。
## 停留 hold_duration 后 fade_duration 淡出并自毁。
## 目前由 BattleRoot 的 DEV 测试按钮随机触发；后续可由真实「触发效果」逻辑调用（传入 unit_id / 文本 / 阵营）。

const BAR_TEX := preload("res://assets/ui/TriggerPromptBar.png")
const BAR_FEATHER: float = 50.0   # 与 TriggerPromptBar.png 羽化深度一致（九宫格左右边块）
const UNIT_ART_DIR := "res://assets/unit_portraits/"
const FALLBACK_UNIT := "Zhouzhou"

## 触发条件类型：决定提示组默认文本（未来可挂配色/图标）。
enum TriggerType { PASSIVE, BLEED, POISON }

const DEFAULT_TEXT := {
	TriggerType.PASSIVE: "被动！",
	TriggerType.BLEED: "出血！",
	TriggerType.POISON: "中毒！",
}

@export var neck_left: float = 40.0          # 左滑时：条左侧「颈」宽，滑入后留在屏幕外，使内容恰好窥入
@export var pad_right: float = 24.0          # 左滑时：内容右侧内边距；右滑时复用为右侧「颈」（离屏）
@export var fixed_bar_height: float = 36.0   # 黑条高度固定，不随文字字号放大而变高；文字放大后相对条垂直居中（可上下溢出）。较旧 44 再压薄。
@export var screen_y: float = 540.0          # 条顶边到屏幕顶距离（设计分辨率 1600x900）。原 140→向下900=1040 越底出屏；现相对越界位置向上500=540，屏幕内可见（中下部）。同时作为堆叠顶部基准线（最新提示组停留处）。
@export var stack_gap: float = 44.0           # 新增触发时，同阵营已存在的提示组各自向下位移的距离（堆叠间隔 = 黑条高 36 + 留白 8）
@export var slide_in_duration: float = 0.35
@export var hold_duration: float = 0.5
@export var fade_duration: float = 0.3

var _unit_id: String = FALLBACK_UNIT
var _text: String = "被动！"
var _faction: String = "player"
var _trigger_type: TriggerType = TriggerType.PASSIVE

## 配置触发提示组：unit_id 取该单位 02 美术；trigger_type 决定默认文本（被动！/出血！）；faction 决定滑入方向（我方左/敌方右）。
func setup(unit_id: String, trigger_type: TriggerType = TriggerType.PASSIVE, faction: String = "player") -> void:
	_unit_id = unit_id
	_trigger_type = trigger_type
	_text = DEFAULT_TEXT.get(trigger_type, "被动！")
	_faction = faction

func setup_card(unit_id: String, card_name: String, faction: String = "player") -> void:
	_unit_id = unit_id
	_trigger_type = TriggerType.PASSIVE   # 仅占位（文本由 card_name 决定，不走默认文本表）
	_text = card_name
	_faction = faction

## 返回该提示组阵营（player=左滑入 / enemy=右滑入），供同级堆叠时按阵营独立计数（左/右两路互不干扰）。
func get_faction() -> String:
	return _faction

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE

	# 堆叠：新增触发时，已存在的同类提示组各自向下位移一个间隔，自身占据顶部基准线(screen_y)。
	_shift_existing_down()

	# 内容：单位格02插图 + 触发文本（HBox，自动尺寸贴合子节点）。
	var content := HBoxContainer.new()
	content.name = "Content"
	content.mouse_filter = Control.MOUSE_FILTER_IGNORE
	content.alignment = BoxContainer.ALIGNMENT_CENTER
	content.add_theme_constant_override("separation", 14)
	add_child(content)

	var icon := TextureRect.new()
	icon.name = "UnitArt"
	var tex := load(UNIT_ART_DIR + "Img_%s02.png" % _unit_id)
	if tex == null:
		tex = load(UNIT_ART_DIR + "Img_%s02.png" % FALLBACK_UNIT)
	icon.texture = tex
	icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.custom_minimum_size = Vector2(49.0, 30.0)   # 原始尺寸(回声格 49x30)，不改尺寸（项目铁规则）
	icon.size_flags_vertical = Control.SIZE_SHRINK_CENTER
	content.add_child(icon)

	var label := Label.new()
	label.name = "Text"
	label.text = _text
	label.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	label.add_theme_font_size_override("font_size", 72)
	label.add_theme_color_override("font_color", Color.WHITE)
	var ls := LabelSettings.new()
	ls.outline_size = 8              # 黑描边，保证白色文本在黑条上可读
	ls.outline_color = Color.BLACK
	label.label_settings = ls
	content.add_child(label)

	# 条宽随内容（文字+图标）自适应；条高固定（fixed_bar_height），不随字号放大而变高。
	var cmin := content.get_combined_minimum_size()
	var bar_w := cmin.x + neck_left + pad_right
	var bar_h := fixed_bar_height
	custom_minimum_size = Vector2(bar_w, bar_h)
	size = custom_minimum_size

	# 背景条（左右虚化）：九宫格，左右边块=BAR_FEATHER 羽化、中间实心横向拉伸，任意宽都不吃掉中段。
	var bar := NinePatchRect.new()
	bar.name = "Bar"
	bar.texture = BAR_TEX
	bar.patch_margin_left = BAR_FEATHER
	bar.patch_margin_right = BAR_FEATHER
	bar.patch_margin_top = 0.0
	bar.patch_margin_bottom = 0.0
	bar.size = size
	bar.position = Vector2.ZERO
	bar.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(bar)
	move_child(bar, 0)   # 黑条移到子节点首位 = 最底层，必须落在内容(icon+文字)之下

	# 阵营决定滑入方向与内容在条内的对齐侧。
	var vw := get_viewport_rect().size.x   # 设计分辨率宽（CanvasLayer 坐标）
	var from_right: bool = (_faction == "enemy")
	if from_right:
		# 敌方：从右侧滑入，内容右对齐（右侧「颈」离屏 pad_right，内容从右缘窥入）。
		content.position = Vector2(bar_w - pad_right - cmin.x, (bar_h - cmin.y) * 0.5)
		position = Vector2(vw + bar_w, screen_y)   # 起点：整条在屏幕右侧外
		var target_x := vw - bar_w + pad_right     # 右颈离屏 pad_right，内容右缘恰好入屏
		_animate_to(target_x)
	else:
		# 我方：从左侧滑入，内容左对齐（左侧「颈」离屏 neck_left，内容从左缘窥入）。
		content.position = Vector2(neck_left, (bar_h - cmin.y) * 0.5)
		position = Vector2(-bar_w, screen_y)       # 起点：整条在屏幕左侧外
		var target_x := -neck_left                 # 左颈离屏 neck_left，内容左缘恰好入屏
		_animate_to(target_x)

## 通用滑入→停留→淡出→自毁动画（加进 CanvasLayer 后 position 即屏幕坐标）。
func _animate_to(target_x: float) -> void:
	modulate.a = 1.0
	var tw := create_tween()
	tw.tween_property(self, "position:x", target_x, slide_in_duration).set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
	tw.tween_interval(hold_duration)
	tw.tween_property(self, "modulate:a", 0.0, fade_duration)
	tw.tween_callback(queue_free)

## 让同级「同阵营」已存在的触发提示组各自向下位移一个堆叠间隔，为自身(最新)腾出顶部位置。
## 不同阵营（左=player / 右=enemy）各自独立堆叠，互不挤动。
## 自身固定在 screen_y 基准线（slide-in 只移动 position.x），不在此处位移。
## 每个兄弟用独立 Tween 并行下移，避免与各自的滑入/淡出动画(操作 position.x / modulate)冲突。
func _shift_existing_down() -> void:
	var parent := get_parent()
	if parent == null:
		return
	for child in parent.get_children():
		if child == self or not child is TriggerPrompt:
			continue
		var tp := child as TriggerPrompt
		if tp.get_faction() != _faction:   # 异侧提示组不 participation 本侧堆叠
			continue
		var tw := create_tween()
		tw.tween_property(tp, "position:y", tp.position.y + stack_gap, 0.25) \
			.set_ease(Tween.EASE_OUT).set_trans(Tween.TRANS_CUBIC)
