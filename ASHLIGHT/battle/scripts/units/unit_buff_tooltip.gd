class_name UnitBuffTooltip
extends Control

## Buff 描述框（悬停提示）。Step 1 · 6d。
## 鼠标悬停于 buff icon 时显示：左上角 buff 名字（字魂扁桃体 25 / #9c660a / 靠左）
## + 下方说明文字（抖音美好体 16 / 白 / 靠左 / 自动换行 / 行间距 2px）。
## 背景为代码绘制的圆角矩形（左上角不圆角、其余三角 10px 圆角，宽度固定 200px），
## 纯黑不透明、无描边。高度按确定性公式：基础 80px + 每行说明 +30px，向下延展（不裁切）。
## 出现/消失带淡入淡出（默认各 0.1s）。复用实例：淡出后仅 hide，不 free 重建。

const FONT_NAME_PATH: String = "res://assets/fonts/Biantao.ttf"         # 字魂扁桃体
const FONT_DESC_PATH: String = "res://assets/fonts/DouyinSansBold.otf"  # 抖音美好体

## 名字字体大小（字魂扁桃体）
@export var name_font_size: int = 25
## 名字颜色 #9c660a（金）
@export var name_color: Color = Color(0.612, 0.4, 0.039, 1.0)
## 说明文字字体大小（抖音美好体）
@export var desc_font_size: int = 16
## 说明文字行间距（px；换行后两行基线间距）
@export var desc_line_spacing: int = 2
## 说明文字颜色（白）
@export var desc_color: Color = Color(1.0, 1.0, 1.0, 1.0)
## 描述框显示尺寸。宽度固定 200px（不随文本变化）；高度按说明换行行数自动撑高。
@export var box_size: Vector2 = Vector2(200.0, 120.0)
## 文字相对底图内边距（x=左右 / y=上下）
@export var padding: Vector2 = Vector2(16.0, 14.0)
## 背景填充色（圆角矩形；纯黑不透明）
@export var bg_color: Color = Color(0.0, 0.0, 0.0, 1.0)
## 圆角半径（px）。左上角固定不圆角，其余三角取此值。
@export var bg_corner_radius: int = 10
## 框体基础高度（px）。即使只有一行说明也至少为此高度。
@export var base_height: float = 80.0
## 每行说明文字额外增加的高度（px），向下延展。
@export var per_line_extra: float = 30.0
## 淡入时长（秒）
@export var fade_in_duration: float = 0.1
## 淡出时长（秒）
@export var fade_out_duration: float = 0.1

var _name_lbl: Label
var _desc_lbl: Label
var _desc_font: Font
var _name_font: Font
var _tween: Tween
var _is_visible: bool = false

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE  # 描述框自身不挡鼠标，避免悬停闪烁
	_name_font = load(FONT_NAME_PATH) as Font
	_desc_font = load(FONT_DESC_PATH) as Font
	# 名字（字魂扁桃体 / 25 / #9c660a / 靠左靠上）
	_name_lbl = Label.new()
	_name_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_name_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	_name_lbl.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	if _name_font != null:
		var ls := LabelSettings.new()
		ls.font = _name_font
		ls.font_size = name_font_size
		ls.font_color = name_color
		_name_lbl.label_settings = ls
	add_child(_name_lbl)
	# 说明文字（抖音美好体 / 16 / 白 / 靠左 / 自动换行 / 行间距 2px）
	_desc_lbl = Label.new()
	_desc_lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_desc_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	_desc_lbl.vertical_alignment = VERTICAL_ALIGNMENT_TOP
	_desc_lbl.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_desc_lbl.clip_text = false  # 显式按行数设高度，不裁剪
	if _desc_font != null:
		var ls := LabelSettings.new()
		ls.font = _desc_font
		ls.font_size = desc_font_size
		ls.font_color = desc_color
		ls.line_spacing = float(desc_line_spacing)  # Label 本身无 line_spacing（4.6.2 报错），设在 LabelSettings 上跨版本稳定
		_desc_lbl.label_settings = ls
	add_child(_desc_lbl)
	modulate.a = 0.0  # 初始透明，首次出现才淡入
	hide()

## 显示描述框。local_top_left 为描述框左上角在父节点（UnitBuff）局部坐标系的目标位置
## （由调用方在 Node2D 上完成“鼠标全局坐标 → 局部坐标”换算后传入；悬停瞬间定位、不跟随移动）。
## instant=true 时跳过淡入、直接置不透明显示（调试常显用）；否则从当前透明度淡入 fade_in_duration 秒。
## 若描述框已显示（如从相邻 icon 滑入），tween 从当前透明度平滑过渡到 1，不归零重淡入，避免闪烁。
func show_tooltip(buff_name: String, description: String, local_top_left: Vector2, instant: bool = false) -> void:
	_name_lbl.text = buff_name
	_desc_lbl.text = description
	# 定位：左上角锚点直接置于传入的局部坐标
	position = local_top_left
	show()
	queue_redraw()
	# 延迟一帧：等字体/文本就绪后按确定性公式重排框体高度
	call_deferred("_relayout_to_fit")
	_kill_tween()
	if instant:
		modulate.a = 1.0
		_is_visible = true
		return
	_is_visible = true
	# tween 从属性当前值插值到 1：首显从 0 淡入；相邻滑入从半透明平滑补满，均不闪烁
	_tween = create_tween()
	_tween.tween_property(self, "modulate:a", 1.0, fade_in_duration)

## 当前是否处于显示/淡出中（供外部判断，无 tooltip 时零开销）。
func is_showing() -> bool:
	return _is_visible

## 淡出销毁（复用实例：仅 hide，不 free 重建）。淡出 fade_out_duration 秒后隐藏并复位透明度。
## 若淡出途中再次 show_tooltip，会 kill 本 tween 并直接置不透明，故平滑无残留。
func dismiss() -> void:
	if not _is_visible:
		return
	_is_visible = false  # 立即标记，避免重复触发；再次 show 会重新置 true
	_kill_tween()
	_tween = create_tween()
	_tween.tween_property(self, "modulate:a", 0.0, fade_out_duration)
	_tween.tween_callback(_on_dismiss_finished)

func _on_dismiss_finished() -> void:
	# 仅当淡出未被打断（期间未再 show）时才真正隐藏；show 会 kill 本 tween，故打断时不触发
	if _is_visible:
		return
	hide()
	modulate.a = 0.0

## 即时隐藏（调试开关关闭时用，不带淡出）。
func hide_tooltip() -> void:
	_kill_tween()
	_is_visible = false
	hide()
	modulate.a = 0.0

func _kill_tween() -> void:
	if _tween != null and _tween.is_valid():
		_tween.kill()
	_tween = null

## 按确定性公式重排框体高度：基础 base_height + 每行说明 per_line_extra，向下延展。
## 行数用字符数估算（CJK 字形约 1em 宽，不依赖字体度量；headless 下字体度量恒为 0 不可用），
## 再用编辑器真实布局做二次校准，绝不裁切。
func _relayout_to_fit() -> void:
	if not is_instance_valid(self) or not visible:
		return
	var inner_w := box_size.x - padding.x * 2.0
	var name_h := float(name_font_size) + 8.0
	var lines := _estimate_desc_lines(_desc_lbl.text, inner_w)
	var desc_h := float(lines) * (float(desc_font_size) + float(desc_line_spacing))
	var formula_h := base_height + float(lines - 1) * per_line_extra
	var needed := padding.y * 2.0 + name_h + desc_h
	box_size.y = maxf(formula_h, needed)
	_name_lbl.position = Vector2(padding.x, padding.y)
	_name_lbl.size = Vector2(inner_w, name_h)
	_desc_lbl.custom_minimum_size = Vector2(inner_w, desc_h)
	_desc_lbl.position = Vector2(padding.x, padding.y + name_h)
	_desc_lbl.size = Vector2(inner_w, desc_h)
	size = box_size
	queue_redraw()
	# 二次校准：编辑器下 Label.get_minimum_size 返回真实换行高度；headless 为 0 则忽略
	call_deferred("_relayout_pass2")

## 真实布局二次校准：若 Label 实际换行高度大于公式估算，则把框体再向下撑高该差值（不裁切）。
func _relayout_pass2() -> void:
	if not is_instance_valid(self) or not visible:
		return
	var real_h := _desc_lbl.get_minimum_size().y
	if real_h <= 0.0:
		return  # headless：无可靠字体度量，沿用公式高度
	var extra := real_h - _desc_lbl.size.y
	if extra > 0.0:
		box_size.y += extra
		_desc_lbl.size.y = real_h
		size = box_size
		queue_redraw()

## 估算说明文字在给定宽度下的换行行数（字符数 / 每行可容纳字符数，CJK 字形约 1em 宽）。
## 不依赖字体度量，headless 与编辑器结果一致；真实布局由 _relayout_pass2 兜底校准。
func _estimate_desc_lines(p_text: String, p_inner_w: float) -> int:
	var n := p_text.length()
	if n == 0:
		return 1
	var char_w := float(desc_font_size)  # CJK 字形宽度≈字号（1em）
	var per_line := maxi(1, int(floor(p_inner_w / char_w)))
	var lines := ceili(float(n) / float(per_line))
	return maxi(1, lines)

## 绘制圆角矩形背景：左上角不圆角，其余三角取 bg_corner_radius，宽度固定 200px。
## 背景绘制在 Control 自身画布（子 Label 之下），故文字始终浮于背景之上。
func _draw() -> void:
	var sb := StyleBoxFlat.new()
	sb.bg_color = bg_color
	sb.corner_radius_top_left = 0
	sb.corner_radius_top_right = bg_corner_radius
	sb.corner_radius_bottom_right = bg_corner_radius
	sb.corner_radius_bottom_left = bg_corner_radius
	draw_style_box(sb, Rect2(Vector2.ZERO, box_size))
