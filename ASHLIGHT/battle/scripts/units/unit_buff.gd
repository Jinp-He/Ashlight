class_name UnitBuff
extends Node2D

## 单位 Buff/Debuff 框体组件（Step 1 · 6c）。
## 图标左→右排布、满行换行、最新最左；层数角标左上角；益害着色；获取新 buff 时下方滑入提示。
## 作为 UnitRoot 的 BuffRoot 子节点挂接；向上通信经方法调用，不直接向上 get_parent。

## buff 集合变化信号（add/remove/clear 后 emit），供 UnitRoot 转发 → BattleRoot 实时刷新手牌士气数值等。
signal buffs_changed

const BUFF_ICON_DIR: String = "res://assets/Unit UI/buffs/icons/"
## buff_id（语义，如 poison/loot/up_poison）→ 美术文件名（美术组按 buff{nnn} 编号产出）。
## 缺省走 buff_id + ".png"；此处仅把语义 id 映射到美术编号文件，美术资源名保持原始编号不变。
const ICON_OVERRIDE: Dictionary = {
	"poison": "buff003",
	"loot": "buff004",
	"up_poison": "buff005",
}
const BUFF_TOAST_PATH: String = "res://assets/Unit UI/unit_hud/buff_toast.png"
const FONT_PATH: String = "res://assets/fonts/DouyinSansBold.otf"

## 框体显示开关（关闭后隐藏整个图标框，但滑入提示仍照常）
@export var show_buff_box: bool = true :
	set(value):
		show_buff_box = value
		if is_instance_valid(_icon_container):
			_icon_container.visible = value
		queue_redraw()

@export var buff_icon_size: Vector2 = Vector2(24.0, 24.0) :
	set(value):
		buff_icon_size = value
		queue_redraw()
@export var buff_icon_gap: float = 8.0 :
	set(value):
		buff_icon_gap = value
		queue_redraw()
## 一行排满数量（换行阈值）；≤0 视为不换行（单行）
@export var buff_icons_per_row: int = 5 :
	set(value):
		buff_icons_per_row = value
		queue_redraw()
## 新 buff 排第一位（最左）
@export var new_buff_at_front: bool = true :
	set(value):
		new_buff_at_front = value
		queue_redraw()
@export var stack_label_color: Color = Color(1.0, 1.0, 1.0, 1.0)
## 滑入提示停留秒数（滑入后保持时长，默认 0.5s）
@export var toast_duration: float = 0.5
## toast 组整体垂直偏移（相对 BuffRoot 原点；负值=上移）。用于把提示抬离框体。
@export var toast_offset_y: float = -120.0

## 【调试】虚线框：标出 buff 框体占位范围（未绑定 buff 时也可见）。正式上线前关掉。
@export var debug_show_frame: bool = true :
	set(value):
		debug_show_frame = value
		queue_redraw()
@export var debug_frame_color: Color = Color(0.4, 0.8, 1.0, 0.85) :
	set(value):
		debug_frame_color = value
		queue_redraw()
## 【调试】勾选后：① 无视悬停、在固定位置常显首个 buff 的描述框（验证“渲染是否坏了”）；
## ② 悬停进入/退出任意 icon 时往 Output 打印日志（验证 “mouse_entered 是否触发”）。
## 用于区分「描述框渲染坏了」还是「悬停没触发」。正式上线前关掉。
@export var debug_tooltip: bool = false :
	set(value):
		debug_tooltip = value
		if value:
			_force_show_debug_tooltip()
		elif is_instance_valid(_tooltip):
			_tooltip.hide_tooltip()
## 框体固定尺寸（不随 buff 数量变化；调试虚线框即按此大小绘制）
@export var buff_box_size: Vector2 = Vector2(140.0, 96.0) :
	set(value):
		buff_box_size = value
		queue_redraw()
## 框体中心相对 BuffRoot 原点的固定偏移（不随 buff 数量变化，位置锁死）
@export var buff_box_center_offset: Vector2 = Vector2(0.0, 41.0) :
	set(value):
		buff_box_center_offset = value
		queue_redraw()

class BuffInstance:
	var buff_id: String
	var stack: int
	var tone: int  # 0未定 / 1有益 / 2有害
	var display_name: String
	var description: String
	func _init(id: String, s: int, t: int, name: String = "", desc: String = "") -> void:
		buff_id = id
		stack = s
		tone = t
		display_name = name
		description = desc

var _entries: Array[BuffInstance] = []
var _icon_container: Node2D
var _toast_root: Node2D
var _tooltip: UnitBuffTooltip
var _font: Font

func _ready() -> void:
	_icon_container = Node2D.new()
	_icon_container.name = "IconContainer"
	_icon_container.visible = show_buff_box
	add_child(_icon_container)
	_toast_root = Node2D.new()
	_toast_root.name = "ToastContainer"
	_toast_root.z_index = 21
	_toast_root.position = Vector2(0.0, toast_offset_y)
	add_child(_toast_root)
	_tooltip = UnitBuffTooltip.new()
	_tooltip.name = "BuffTooltip"
	_tooltip.z_index = 30
	add_child(_tooltip)
	_font = load(FONT_PATH) as Font
	if debug_tooltip:
		_force_show_debug_tooltip()

## 新增/叠加一个 buff（tone: 0未定/1有益/2有害；display_name 取自数据表名称，留空则回退 buff_id）。
## 相同 buff_id 已在框内 → 仅叠加层数（不生成新 icon）；新 buff_id 才新增一格。
## 文案统一来源：display_name / description 留空时，由 KeywordGlossary 按 buff_id 解析，
## 与卡牌快照词条面板共用同一份文字，避免双份真源日后分叉。
func add_buff(buff_id: String, stack: int, tone: int, display_name: String = "", description: String = "") -> void:
	var resolved_name := display_name if display_name != "" else KeywordGlossary.name_of(buff_id)
	var resolved_desc := description if description != "" else KeywordGlossary.desc_of(buff_id)
	for e in _entries:
		if e.buff_id == buff_id:
			e.stack += maxi(stack, 1)
			if tone != 0:
				e.tone = tone
			if resolved_name != "":
				e.display_name = resolved_name
			if resolved_desc != "":
				e.description = resolved_desc
			_rebuild()
			_play_toast(e)
			if debug_tooltip:
				_force_show_debug_tooltip()
			buffs_changed.emit()
			return
	var entry := BuffInstance.new(buff_id, maxi(stack, 1), tone, resolved_name, resolved_desc)
	if new_buff_at_front:
		_entries.push_front(entry)
	else:
		_entries.push_back(entry)
	_rebuild()
	_play_toast(entry)
	if debug_tooltip:
		_force_show_debug_tooltip()
	buffs_changed.emit()

func remove_buff(buff_id: String) -> void:
	for i in _entries.size():
		if _entries[i].buff_id == buff_id:
			_entries.remove_at(i)
			break
	_rebuild()
	buffs_changed.emit()

## 减少指定 buff_id 的层数（最小 0）；归零则从框内移除该条目。
## 供【出血】每次队列推进 -1 层 /【中毒】护盾 1:1 抵消层数 使用。
func remove_buff_stacks(buff_id: String, amount: int) -> void:
	for i in _entries.size():
		if _entries[i].buff_id == buff_id:
			_entries[i].stack -= amount
			if _entries[i].stack <= 0:
				_entries.remove_at(i)
			_rebuild()
			buffs_changed.emit()
			return

func clear_buffs() -> void:
	_entries.clear()
	_rebuild()
	buffs_changed.emit()

## 返回指定 buff_id 的当前总层数（无该 buff 则返回 0）。供士气等关键数值加成读取。
func get_buff_stacks(buff_id: String) -> int:
	for e in _entries:
		if e.buff_id == buff_id:
			return e.stack
	return 0

func _rebuild() -> void:
	if not is_instance_valid(_icon_container):
		return
	for child in _icon_container.get_children():
		child.queue_free()
	var step_x := buff_icon_size.x + buff_icon_gap
	var step_y := buff_icon_size.y + buff_icon_gap
	var per_row := maxi(buff_icons_per_row, 1)
	for i in _entries.size():
		var e := _entries[i] as BuffInstance
		var col := i % per_row
		var row := i / per_row
		var icon := TextureRect.new()
		icon.name = "Buff_" + e.buff_id
		icon.size = buff_icon_size
		icon.position = Vector2(col * step_x, row * step_y)
		var icon_file: String = ICON_OVERRIDE.get(e.buff_id, e.buff_id)
		var icon_path: String = BUFF_ICON_DIR + icon_file + ".png"
		var tex: Texture2D = load(icon_path) as Texture2D if ResourceLoader.exists(icon_path) else null
		if tex == null:
			# 专属图标缺失 → 兜底用通用占位图（buff001=中性 / buff002=有害风格），保证框内有图标可验证；
			# 正式图标请放入 res://assets/Unit UI/buffs/icons/{buff_id}.png（如 bleed.png / morale.png）。
			tex = load(BUFF_ICON_DIR + ("buff002.png" if e.tone == 2 else "buff001.png")) as Texture2D
		if tex != null:
			icon.texture = tex
		else:
			push_warning("UnitBuff: 图标缺失 %s" % (BUFF_ICON_DIR + e.buff_id + ".png"))
		# 益害着色：直接染 icon 自身纹理（self_modulate 只作用于 TextureRect 自身，不污染子节点层数文字）
		icon.self_modulate = _tone_color(e.tone)
		# 层数角标（右上方，白字，抖音美好体不加粗）
		if e.stack > 1:
			var lbl := Label.new()
			lbl.text = str(e.stack)
			lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_RIGHT
			lbl.vertical_alignment = VERTICAL_ALIGNMENT_TOP
			if _font != null:
				var ls := LabelSettings.new()
				ls.font = _font
				ls.font_size = 15
				ls.outline_size = 7
				ls.outline_color = Color(0.0, 0.0, 0.0, 1.0)
				ls.font_color = stack_label_color
				lbl.label_settings = ls
			# 标签铺满图标、右对齐 → 角标落在图标右上角；整体再往右上角挪 5px（x+5 右 / y-5 上）
			lbl.size = buff_icon_size
			lbl.position = Vector2(5.0, -5.0)
			lbl.mouse_filter = Control.MOUSE_FILTER_IGNORE  # 角标不挡鼠标，事件穿透到 icon
			icon.add_child(lbl)
		icon.mouse_filter = Control.MOUSE_FILTER_STOP  # 确保能接收鼠标进入/退出
		icon.mouse_entered.connect(_on_icon_hover.bind(e, icon))
		icon.mouse_exited.connect(_on_icon_exit)
		_icon_container.add_child(icon)
	# 图标从框体左上角起排（框体大小/位置固定，不随 buff 数量变化）
	var rect := _compute_box_rect()
	_icon_container.position = rect.position
	queue_redraw()

func _tone_color(tone: int) -> Color:
	# 用于 self_modulate 直接染 icon（乘法 tint，保留图标亮度与细节；不透明度=1）
	match tone:
		1: return Color(0.55, 0.9, 0.5, 1.0)    # 有益：绿色调
		2: return Color(0.95, 0.5, 0.45, 1.0)   # 有害：红色调
		_: return Color(1.0, 1.0, 1.0, 1.0)     # 中性：不染色

## 调试虚线框：标出 buff 框体固定占位范围（不随 buff 数量变化；尺寸/位置锁死）。
func _draw() -> void:
	if not debug_show_frame:
		return
	var margin := 2.0
	var rect := _compute_box_rect().grow(margin)
	_draw_dashed_rect(rect, debug_frame_color, 6.0, 4.0)

func _compute_box_rect() -> Rect2:
	# 固定框体：尺寸与位置均由导出参数锁定，与 buff 数量无关
	return Rect2(buff_box_center_offset - buff_box_size * 0.5, buff_box_size)

func _draw_dashed_rect(rect: Rect2, color: Color, dash: float, gap: float) -> void:
	var pts := PackedVector2Array()
	pts.append(rect.position)
	pts.append(rect.position + Vector2(rect.size.x, 0.0))
	pts.append(rect.position + rect.size)
	pts.append(rect.position + Vector2(0.0, rect.size.y))
	pts.append(rect.position)  # 闭合
	for i in pts.size() - 1:
		_draw_dashed_segment(pts[i], pts[i + 1], color, dash, gap)

func _draw_dashed_segment(a: Vector2, b: Vector2, color: Color, dash: float, gap: float) -> void:
	var dist := a.distance_to(b)
	if dist < 0.0001:
		return
	var dir := (b - a) / dist
	var pos := 0.0
	while pos < dist:
		var end := mini(pos + dash, dist)
		draw_line(a + dir * pos, a + dir * end, color, 1.0, false)
		pos += dash + gap

## 获取新 buff：提示从上方滑入(淡入) → 停留 → 原地淡出，播放一次后销毁（独立于框体开关）
func _play_toast(entry: BuffInstance) -> void:
	if not is_instance_valid(_toast_root):
		return
	var toast := Node2D.new()
	toast.name = "Toast_" + entry.buff_id
	var bg := TextureRect.new()
	var bg_tex := load(BUFF_TOAST_PATH) as Texture2D
	if bg_tex != null:
		bg.texture = bg_tex
		bg.size = bg_tex.get_size()
	else:
		bg.size = Vector2(120.0, 32.0)
		bg.modulate = Color(0.0, 0.0, 0.0, 0.7)  # 占位：无美术时用半透明黑块
	toast.add_child(bg)
	# 名称文字（居中于 toast 图，抖音美好体/20/白/无描边）
	var name_lbl := Label.new()
	name_lbl.text = entry.display_name if entry.display_name != "" else entry.buff_id
	name_lbl.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	name_lbl.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	if _font != null:
		var ls := LabelSettings.new()
		ls.font = _font
		ls.font_size = 20
		ls.outline_size = 0
		ls.font_color = Color(1.0, 1.0, 1.0, 1.0)
		name_lbl.label_settings = ls
	name_lbl.size = bg.size
	name_lbl.position = Vector2(0.0, 0.0)
	toast.add_child(name_lbl)
	_toast_root.add_child(toast)
	# 动画：起点在最终位置上方 20px，淡入滑入 0.2s → 停留 toast_duration → 原地淡出 0.1s → 销毁
	var slide_dist := 20.0
	toast.position = Vector2(-bg.size.x * 0.5, -slide_dist)
	toast.modulate = Color(1.0, 1.0, 1.0, 0.0)
	var tween := create_tween()
	tween.parallel().tween_property(toast, "position:y", 0.0, 0.2).set_ease(Tween.EASE_OUT)
	tween.parallel().tween_property(toast, "modulate:a", 1.0, 0.2)
	tween.tween_interval(toast_duration)
	tween.tween_property(toast, "modulate:a", 0.0, 0.1)
	tween.tween_callback(toast.queue_free)

## 调试：无视悬停、在固定位置常显首个 buff 的描述框，用于验证“渲染管线是否完好”。
## 勾选 debug_tooltip 时由 setter / _ready 调用；空 buff 时打印引导（需先点按钮加 buff）。
func _force_show_debug_tooltip() -> void:
	if _entries.is_empty():
		print("UnitBuff: [DEBUG] 当前无 buff，无法常显描述框 —— 请先点顶部「随机获得一层 Buff」按钮添加。")
		return
	if not is_instance_valid(_tooltip):
		return
	# 选说明文字最长的 buff 展示，确保调试时直观看到自动换行效果（短文字单行放下不换行属正常）
	var e := _entries[0] as BuffInstance
	for cand in _entries:
		if (cand as BuffInstance).description.length() > e.description.length():
			e = cand as BuffInstance
	var name := e.display_name if e.display_name != "" else e.buff_id
	# 固定位置：buff 框体正上方（不依赖鼠标，纯渲染验证）
	var local := _compute_box_rect().position + Vector2(0.0, -130.0)
	_tooltip.show_tooltip(name, e.description, local, true)  # instant：调试常显不带淡入

## 鼠标进入 buff icon → 显示描述框（名字 + 说明文字，靠左排版）
func _on_icon_hover(entry: BuffInstance, icon: TextureRect) -> void:
	if debug_tooltip:
		print("UnitBuff: [HOVER] 进入 icon buff_id=%s（若描述框也出现 = 悬停正常）" % entry.buff_id)
	if not is_instance_valid(_tooltip):
		return
	# 描述框锚定到鼠标位置（右下方 10px）：在 Node2D 上完成全局→局部换算后传给描述框
	var mouse_global := get_global_mouse_position()
	var local_top_left := to_local(mouse_global) + Vector2(10.0, 10.0)
	_tooltip.show_tooltip(entry.display_name if entry.display_name != "" else entry.buff_id, entry.description, local_top_left)

## 鼠标离开 buff icon → 淡出销毁描述框（复用实例）
func _on_icon_exit() -> void:
	if debug_tooltip:
		print("UnitBuff: [HOVER] 离开 icon")
	if is_instance_valid(_tooltip):
		_tooltip.dismiss()

