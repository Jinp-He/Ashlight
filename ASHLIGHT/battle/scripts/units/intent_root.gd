class_name IntentRoot
extends Node2D

## 单位意图组组件：读取怪物行动逻辑后的视觉效果。
## 由三组构成，均以 IntentRoot 局部原点为中心对齐：
##   ① 底图(Base)             —— 单张意图底板 intent_base.png
##   ② 意图类型组(TypeGroup)  —— 全部类型图标 type_{id}.png，统一着色为 COLOR_INTENT；活动图标居中于底图，数值文字(NxM)位于其下方并上移40px（与图标同色9c660a、同样居中于底图）
## 显示逻辑：仅当单位阵营为敌人(enemy)且已设置意图时显示，否则隐藏（见 set_faction / _apply_visibility）。

const INTENT_DIR: String = "res://assets/Unit UI/intent/"
## 数值文字字体（Biantao 边桃体，已导入）。
const FONT_PATH: String = "res://assets/fonts/Biantao.ttf"        # 扁桃体（关键数值）
const FONT_DOUYIN_PATH: String = "res://assets/fonts/DouyinSansBold.otf"   # 抖音体（详细文本正文，与卡牌效果同款）

## 底图文件名（单张意图底板）。
const BASE_FILE: String = "intent_base.png"
## 类型图标 id 列表（与美术 type_{id}.png 对应）。供随机组合按钮直接引用。
const TYPE_IDS: Array[String] = ["att", "shield", "state", "think"]
## 美术资产固定：monomer=点(target_monomer.png)、aoe=线(target_aoe.png)。

## 各目标组合的激活元素（取 COLOR_INTENT；另一元素取 COLOR_INACTIVE）。

## 不随单体/群体切换；群体(scope=群体)时数字后追加 +(含全场 ⏳A+)，+ 后缀语义 = 打击面。


## 悬停敌方单位时，指向它当前锁定的目标。
@export var telegraph_enabled: bool = true

## 意图类型图标 / 目标激活元素统一色（9c660a = 0.61176, 0.4, 0.03922）。
const COLOR_INTENT: Color = Color(0.61176, 0.4, 0.03922)
## 目标非激活元素色（3f4447 = 0.24706, 0.26667, 0.27843）。
const COLOR_INACTIVE: Color = Color(0.24706, 0.26667, 0.27843)

const CARD_HAND_SCALE: float = 0.42

## 三组定位偏移：均以 IntentRoot 局部原点为中心对齐。
@export var base_offset: Vector2 = Vector2(0, 5) :
	set(v):
		base_offset = v
		_rebuild_layout()
@export var type_offset: Vector2 = Vector2.ZERO :
	set(v):
		type_offset = v
		_rebuild_layout()
## 意图目标组默认上移 37px（中心对齐后再上移：初版 15px → +17px → +3px → +2px）。
## 整个意图组（底图+类型组+目标组）相对 IntentRoot 锚点的整体位移。
## 默认上移 120px（相对 UnitRoot 给出的 intent_offset_y 锚点）。
@export var group_offset: Vector2 = Vector2(0.0, -120.0) :
	set(v):
		group_offset = v
		_rebuild_layout()
## 数值文字（格式 NxM，N=次数/M=伤害）字号与描边。颜色固定 COLOR_INTENT（与类型图标一致）。
@export var value_font_size: int = 24 :
	set(v):
		value_font_size = v
		_refresh_value_font()
@export var value_outline_size: int = 4 :
	set(v):
		value_outline_size = v
		_refresh_value_font()

## ── 悬停展开详情面板 ──
## 鼠标直接悬停意图气泡时：原地展开一个代码绘制的黑色圆角矩形（宽固定 150，高随文字自增），
## 左侧显示意图类型图标（裁剪到字形区域，紧凑显示），右侧为换行说明文字；展开动画为以意图中心左右展开。
@export_group("Hover Panel")
@export var hover_panel_width: float = 150.0        # 面板固定宽度（不随文字改变）
@export var hover_panel_min_height: float = 70.0    # 面板最小高度（文字少时的基准高度）
@export var hover_panel_radius: int = 10 :
	set(v):
		hover_panel_radius = v
		_apply_hover_style()
@export var hover_expand_time: float = 0.22         # 左右展开时长
@export var hover_content_fade_time: float = 0.15   # 展开后内容淡入时长
@export var hover_panel_padding: float = 9.0        # 面板内边距
@export var hover_icon_box: float = 40.0            # 左侧图标正方容器边长
@export var hover_icon_gap: float = 8.0             # 图标与文字间距
@export var hover_text_font_size: int = 12          # 右侧说明文字字号
@export var hover_name_font_size: int = 16          # 图标下方行为名称字号（攻击/状态/防御…）
@export var hover_name_gap: float = 4.0             # 图标与名称间距
@export var hover_keyval_font_size: int = 24        # 关键数值(扁桃体放大)字号，与卡牌关键数值放大一致
@export var hover_panel_bg: Color = Color(0.0, 0.0, 0.0, 1.0) :
	set(v):
		hover_panel_bg = v
		_apply_hover_style()
@export var hover_panel_top_offset: float = 0.0     # 面板顶边相对缩略图顶边的微调（默认对齐）

## 类型图标在 62x88 源图内的实际字形区域（用于悬停面板左侧紧凑显示；ArtSource 尺寸变化时回退整图）。
const TYPE_GLYPH_REGION: Dictionary = {
	"att": Rect2(9, 20, 45, 22),
	"shield": Rect2(9, 16, 44, 30),
	"state": Rect2(12, 17, 39, 27),
	"think": Rect2(19, 24, 25, 40),
}

## 菱形中心相对气泡上沿的微调。
@export var card_badge_offset: Vector2 = Vector2(0, -9):
	set(value):
		card_badge_offset = value
		_rebuild_layout()
var _card_badge: Node2D
var _card_badge_fill: ColorRect
var _card_badge_number: Label

var _base: NinePatchRect
var _type_group: Node2D
var _type_icons: Dictionary = {}        # id: String -> TextureRect
var _active_type: String = ""
var _active_value: String = ""
var _active_value_base: String = ""   # buff 前的基准数值（仅作 ▲/▼ 方向比较用；空串=无基准/不显示箭头）
var _active_display: String = ""      # 意图数据自带的原话文案（display）；非空时悬停说明直接用它，不再走类型模板。
var _active_target_faction: String = ""
var _active_scope: String = "单体"
var _value_label: Label
var _faction: String = "player"

## ── 悬停详情面板运行时状态 ──
var _hover_panel: Panel                  # 黑色圆角矩形容器（代码绘制）
var _hover_style: StyleBoxFlat           # 面板样式（纯黑 + 10px 圆角）
var _hover_icon: TextureRect             # 面板左侧图标（裁剪到字形区域）
var _hover_text: RichTextLabel           # 面板右侧换行说明文字（bbcode 混排：抖音体正文 + 扁桃体关键数值 + 红▲/绿▼）
var _hover_name: Label                   # 面板左下：行为名称（攻击/状态/防御…），位于图标正下方
var _hover_panel_open: bool = false      # 当前是否处于展开态
var _hover_tween: Tween                  # 展开/收起补间
var _hover_panel_h: float = 0.0          # 本次展开的面板高度（内容决定）
var _hover_text_w: float = 0.0           # 文字可用宽度
var _base_native_size: Vector2 = Vector2(62.0, 88.0)  # 缩略图原始尺寸（纵向拉伸的基准）

## 意图抛物线预警（攻击范围可视化）：敌人→范围内各目标的弧线 + 箭头 + 拖尾。
var _telegraph: IntentTelegraph
var _is_hovering: bool = false      # 鼠标是否正悬停意图区域（持续循环播放）

func _ready() -> void:
	_anchor_position = position
	var owner_unit := get_parent() as UnitRoot
	if is_instance_valid(owner_unit):
		var phase: float = float(posmod(owner_unit.unit_id.hash(), 997)) / 997.0
		_float_elapsed = phase * float_period
	_build()
	visible = false  # 默认隐藏，set_intent + 阵营为敌人后显示

func _build() -> void:
	# ① 底图（NinePatchRect：非悬停时按原始尺寸 1:1 显示；悬停时仅纵向拉伸中段、保留顶部圆角与底部尖角）
	_base = NinePatchRect.new()
	_base.name = "IntentBase"
	_base.mouse_filter = Control.MOUSE_FILTER_IGNORE
	add_child(_base)
	_try_load_base()
	# ② 意图类型组（含全部类型图标，统一着色 COLOR_INTENT）
	_type_group = Node2D.new()
	_type_group.name = "IntentTypeGroup"
	add_child(_type_group)
	for id in TYPE_IDS:
		var tr := TextureRect.new()
		tr.name = "Type_" + id
		tr.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_try_load(tr, "type_" + id + ".png", Color(0.3, 0.6, 1.0, 0.6))
		tr.modulate = COLOR_INTENT   # 覆盖颜色为 9c660a
		tr.visible = false
		_type_group.add_child(tr)
		_type_icons[id] = tr
	# 数值文字（格式 NxM：N=次数/M=伤害）：挂到类型组，活动图标正下方、水平居中于底图；空串时隐藏。
	_value_label = Label.new()
	_value_label.name = "IntentValue"
	_value_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_value_label.visible = false
	_value_label.add_theme_color_override("font_color", COLOR_INTENT)
	var font := load(FONT_PATH) as Font
	if font != null:
		var ls := LabelSettings.new()
		ls.font = font
		ls.font_size = value_font_size
		ls.outline_size = value_outline_size
		ls.outline_color = Color(0.0, 0.0, 0.0, 1.0)
		ls.font_color = COLOR_INTENT   # 字体颜色与类型图标一致（9c660a），LabelSettings 优先于 theme 覆盖
		_value_label.label_settings = ls
	else:
		push_warning("IntentRoot: 数值字体加载失败 %s，将用默认字体" % FONT_PATH)
	_type_group.add_child(_value_label)
	_telegraph = IntentTelegraph.new()
	_telegraph.name = "IntentTelegraph"
	_telegraph.z_index = -1
	_telegraph.visible = false
	add_child(_telegraph)
	# ⑤ 悬停展开详情面板（代码绘制黑圆角矩形 + 左图标 + 右换行文字）—— 最后加入以绘制在缩略图之上
	_build_card_badge()
	_build_hover_panel()
	_rebuild_layout()

## 构建悬停详情面板节点（Panel + StyleBoxFlat 圆角 + 左图标 + 右文字）。
func _build_card_badge() -> void:
	_card_badge = Node2D.new()
	_card_badge.name = "预色组"
	_card_badge.z_index = 11
	_card_badge.scale = Vector2.ONE * CARD_HAND_SCALE * 0.8
	_card_badge.visible = false
	add_child(_card_badge)
	for side in [CardRoot.COST_SIZE + CardRoot.COST_OUTLINE_SIZE * 2.0, CardRoot.COST_SIZE]:
		var layer := ColorRect.new()
		layer.size = Vector2.ONE * side
		layer.position = -layer.size * 0.5
		layer.pivot_offset = layer.size * 0.5
		layer.rotation = PI * 0.25
		layer.color = Color.BLACK
		layer.mouse_filter = Control.MOUSE_FILTER_IGNORE
		_card_badge.add_child(layer)
		_card_badge_fill = layer
	_card_badge_number = Label.new()
	_card_badge_number.size = Vector2(90, 63)
	_card_badge_number.position = -_card_badge_number.size * 0.5
	_card_badge_number.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_card_badge_number.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_card_badge_number.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_card_badge_number.add_theme_font_override("font", preload(CardRoot.FONT_PATH))
	_card_badge_number.add_theme_font_size_override("font_size", 75)
	_card_badge_number.add_theme_color_override("font_outline_color", Color.BLACK)
	_card_badge_number.add_theme_constant_override("outline_size", 10)
	_card_badge.add_child(_card_badge_number)

func _build_hover_panel() -> void:
	_hover_panel = Panel.new()
	_hover_panel.name = "IntentHoverPanel"
	_hover_panel.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_hover_panel.z_index = 10
	_hover_panel.visible = false
	_hover_panel.size = Vector2(hover_panel_width, hover_panel_min_height)
	_hover_style = StyleBoxFlat.new()
	_apply_hover_style()
	_hover_panel.add_theme_stylebox_override("panel", _hover_style)
	add_child(_hover_panel)

	_hover_icon = TextureRect.new()
	_hover_icon.name = "HoverIcon"
	_hover_icon.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_hover_icon.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_hover_icon.stretch_mode = TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	_hover_icon.modulate = COLOR_INTENT
	_hover_panel.add_child(_hover_icon)

	_hover_text = RichTextLabel.new()
	_hover_text.name = "HoverText"
	_hover_text.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_hover_text.bbcode_enabled = true
	_hover_text.fit_content = false
	_hover_text.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	_hover_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_LEFT
	_hover_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_hover_text.clip_contents = false
	# 正文：抖音体 + 白字（与卡牌效果同款字体；黑底面板下白字保证可读）。
	var body_font: Font = load(FONT_DOUYIN_PATH) as Font
	_hover_text.add_theme_font_override("normal_font", body_font)
	_hover_text.add_theme_font_size_override("normal_font_size", hover_text_font_size)
	_hover_text.add_theme_color_override("default_color", Color(1.0, 1.0, 1.0, 1.0))
	_hover_panel.add_child(_hover_text)

	## 图标下方行为名称（攻击/状态/防御…），居中于图标正下方，白字。
	_hover_name = Label.new()
	_hover_name.name = "HoverName"
	_hover_name.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_hover_name.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hover_name.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	var nls := LabelSettings.new()
	nls.font = load(FONT_PATH) as Font
	nls.font_size = hover_name_font_size
	nls.font_color = COLOR_INTENT   # 行为名称颜色与意图类型图标一致（9c660a）
	_hover_name.label_settings = nls
	_hover_panel.add_child(_hover_name)

## 应用面板样式（纯黑底 + 四角圆角弧度）。export 改动即时生效。
func _apply_hover_style() -> void:
	if _hover_style == null:
		return
	_hover_style.bg_color = hover_panel_bg
	_hover_style.corner_radius_top_left = hover_panel_radius
	_hover_style.corner_radius_top_right = hover_panel_radius
	_hover_style.corner_radius_bottom_left = hover_panel_radius
	_hover_style.corner_radius_bottom_right = hover_panel_radius

## 设置单位阵营（由 UnitRoot 转发）。仅敌人(enemy)显示意图组。
func set_faction(faction: String) -> void:
	_faction = faction
	_apply_visibility()

## 类型组仅显示活动类型图标（着色 COLOR_INTENT，居中于底图），数值文字(NxM)位于类型图标下方并上移40px、同样居中于底图（字体色同 COLOR_INTENT）。
func set_intent(type_id: String, value: String = "", target_faction: String = "", scope: String = "单体", value_base: String = "", display_text: String = "", card_color: int = -1, card_number: int = 0) -> void:
	_active_type = type_id
	_active_value = value
	_active_value_base = value_base
	_active_display = display_text
	if is_instance_valid(_card_badge):
		_card_badge.visible = card_color >= 0 and card_color < CardRoot.COST_COLORS.size()
		if _card_badge.visible:
			_card_badge_fill.color = CardRoot.COST_COLORS[card_color]
			_card_badge_number.text = str(card_number)
	_active_target_faction = target_faction
	_active_scope = scope
	for id in TYPE_IDS:
		if _type_icons.has(id):
			_type_icons[id].visible = (id == type_id)
	# 数值文字：空串隐藏；think 类型下强制隐藏（环境/思考类意图不显示数值）。
	if is_instance_valid(_value_label):
		_value_label.text = value
		_value_label.visible = (value != "") and (type_id != "think")
		if value != "":
			_value_label.size = _value_label.get_minimum_size()
	_apply_visibility()
	_rebuild_layout()
	# 新意图生成 → 重算预警目标集并按类型着色；未悬停查看时播放一次（悬停时由 _process 循环播放）。
	_refresh_telegraph()

## 显隐规则：仅当阵营为敌人且已设置意图类型时显示整个意图组。
func _apply_visibility() -> void:
	visible = (_faction == "enemy") and (_active_type != "")

func get_intent() -> Dictionary:
	return {"type": _active_type, "value": _active_value,
		"target_faction": _active_target_faction, "scope": _active_scope}

## 用战斗已经锁定的单个目标，悬停期间循环预览。
func _refresh_telegraph() -> void:
	if not is_instance_valid(_telegraph):
		return
	_telegraph.stop()
	if not telegraph_enabled or not _is_hovering or not visible:
		return
	var br := get_tree().get_first_node_in_group("battle_root")
	var caster := get_parent() as UnitRoot
	if not is_instance_valid(br) or not is_instance_valid(caster):
		return
	var card: Dictionary = br._v2_intents.get(caster.unit_id, {})
	if caster.unit_faction == "enemy" and card.get("stat") == "damage":
		return
	var target: UnitRoot = br._find_unit(str(card.get("target", "")))
	if not is_instance_valid(target) or target == caster or target.get_current_hp() <= 0.0:
		return
	var targets: Array[UnitRoot] = [target]
	_telegraph.setup(caster, targets, _telegraph_color())
	_telegraph.play_loop()
## 预警弧颜色按意图类型区分（att 红 / shield 蓝 / state 紫；无目标类兜底暖黄）。
func _telegraph_color() -> Color:
	match _active_type:
		"att": return Color(1.0, 0.35, 0.25, 1.0)
		"shield": return Color(0.35, 0.6, 1.0, 1.0)
		"state": return Color(0.7, 0.4, 1.0, 1.0)
		_: return Color(1.0, 0.8, 0.3, 1.0)

##   ① 意图图标区域（to_global(group_offset) 矩形）
##   ② 所属单位组（战场 UnitRoot 激活区，160x330）
## 仅敌人且意图可见时生效。
@export var float_amplitude: float = 3.0
@export var float_period: float = 2.0
var _float_elapsed: float = 0.0
var _anchor_position: Vector2 = Vector2.ZERO

func set_anchor_position(anchor: Vector2) -> void:
	_anchor_position = anchor
	position = anchor

func _process(delta: float) -> void:
	if visible and _faction == "enemy":
		var period: float = maxf(float_period, 0.01)
		_float_elapsed = fposmod(_float_elapsed + delta, period)
		position = _anchor_position + Vector2(0.0, sin(_float_elapsed / period * TAU) * float_amplitude)
	if _faction != "enemy" or not visible:
		if _is_hovering:
			_is_hovering = false
			if telegraph_enabled:
				_telegraph.stop()
		_set_hover_panel(false)
		return
	var m: Vector2 = get_global_mouse_position()
	var hover_unit: bool = _is_mouse_over_owner_unit(m)
	if hover_unit != _is_hovering:
		_is_hovering = hover_unit
		_refresh_telegraph()
	_set_hover_panel(hover_unit)

## 鼠标是否落在所属单位组（战场 UnitRoot 激活区 160x330）内。
func _is_mouse_over_owner_unit(m: Vector2) -> bool:
	var caster: UnitRoot = get_parent()
	if is_instance_valid(caster):
		return caster.is_point_inside(m)
	return false


## 安全加载：仅当资源已导入（存在 .import）才 load()，避免未导入 PNG 在 headless 下
## 触发 Godot 即时导入——导入需写临时文件再 rename，而 A: 盘 OS 锁禁止 rename 时会崩溃。
## 未导入则回退占位色块，待用户释放锁、编辑器正常导入后自动显示真实美术。
## 加载缩略图底图（NinePatchRect）。九宫格边距：顶部 14px 保留顶部圆角/凸起、底部 18px 保留底部圆角+尖角，
## 仅中间直身段参与纵向拉伸 —— 故悬停时拉高只向下延伸、不改变顶部与尖角形状、宽度不变。
func _try_load_base() -> void:
	var full := INTENT_DIR + BASE_FILE
	var tex: Texture2D = null
	if FileAccess.file_exists(full + ".import"):
		tex = load(full) as Texture2D
	if tex != null:
		_base.texture = tex
		_base_native_size = tex.get_size()
		_base.modulate = Color(1.0, 1.0, 1.0, 1.0)
	else:
		_base.texture = null
		_base_native_size = Vector2(62.0, 88.0)
		_base.modulate = Color(0.6, 0.6, 0.6, 0.35)
	_base.size = _base_native_size
	_base.patch_margin_top = 14
	_base.patch_margin_bottom = 18
	_base.patch_margin_left = 0
	_base.patch_margin_right = 0

func _try_load(rect: TextureRect, file: String, placeholder: Color, fallback_size: Vector2 = Vector2(32.0, 32.0)) -> void:
	var full := INTENT_DIR + file
	if FileAccess.file_exists(full + ".import"):
		var tex := load(full) as Texture2D
		if tex != null:
			rect.texture = tex
			rect.size = tex.get_size()
			rect.modulate = Color(1.0, 1.0, 1.0, 1.0)
			return
	rect.texture = null
	rect.size = fallback_size
	rect.modulate = placeholder

## 刷新数值文字字号/描边（F6 改 export 即时生效）。
func _refresh_value_font() -> void:
	if is_instance_valid(_value_label) and _value_label.label_settings != null:
		_value_label.label_settings.font_size = value_font_size
		_value_label.label_settings.outline_size = value_outline_size
		if _active_value != "":
			_value_label.size = _value_label.get_minimum_size()
		_rebuild_layout()

## 布局：三组均以各自 offset 为原点居中。
func _rebuild_layout() -> void:
	if is_instance_valid(_card_badge):
		_card_badge.position = base_offset + group_offset + Vector2(0, -_base_native_size.y * 0.5) + card_badge_offset
	if is_instance_valid(_base):
		if not _hover_panel_open:
			_base.size = _base_native_size   # 非悬停：恢复原始尺寸
			_base.position = -_base.size * 0.5 + base_offset + group_offset
	if is_instance_valid(_type_group):
		_type_group.position = type_offset + group_offset
		_layout_type_unit()
	# 悬停展开态：重算内容并按当前展开宽度重新套用布局（缩略图随之纵向拉伸）。
	if _hover_panel_open:
		_prepare_hover_content()
		_apply_hover_layout(hover_panel_width)

## 类型组：活动图标居中于组原点（即与底图中心对齐）；数值文字(NxM)位于图标正下方、水平居中于底图中心。
## 类型图标与数值文字各自独立居中（X 均对齐底图中心），不互相并排。无数值时空 label 隐藏、仅图标居中。
func _layout_type_unit() -> void:
	var icon: TextureRect = _type_icons.get(_active_type)
	if icon == null or not icon.visible:
		return
	# 类型图标居中（中心对齐组原点 = 底图中心）
	icon.position = Vector2(-icon.size.x * 0.5, -icon.size.y * 0.5)
	# 数值文字：位于图标正下方，水平居中于底图中心（X=0 即组原点 X）；再整体上移 40px 与图标收紧
	if is_instance_valid(_value_label) and _value_label.visible and _active_value != "":
		var val_size: Vector2 = _value_label.get_minimum_size()
		var gap_v: float = 4.0
		_value_label.position = Vector2(-val_size.x * 0.5, icon.size.y * 0.5 + gap_v - 44.0)



## scope=群体 时数字后追加 +(含全场 ⏳A+)；单体不追加。icon+数字 统一白(白底 PNG 直接白显) + 5px 黑描边。


## ── 悬停详情面板：展开 / 收起 / 内容 / 几何 ──

## 展开或收起面板（幂等：状态未变直接返回）。展开=以意图中心向左右拉开 + 内容随后淡入；
## 收起=内容先淡出再收拢宽度。宽度变化时同步回缩缩略图的纵向拉伸（t = width / 面板满宽）。
func _set_hover_panel(open: bool) -> void:
	if open == _hover_panel_open:
		return
	if _hover_tween != null and _hover_tween.is_valid():
		_hover_tween.kill()
	_hover_panel_open = open
	if open:
		_prepare_hover_content()
		_hover_panel.visible = true
		_hover_icon.modulate.a = 0.0
		_hover_text.modulate.a = 0.0
		if is_instance_valid(_hover_name):
			_hover_name.modulate.a = 0.0
		_apply_hover_layout(0.0)
		_hover_tween = create_tween()
		_hover_tween.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		_hover_tween.tween_method(Callable(self, "_apply_hover_layout"), 0.0, hover_panel_width, hover_expand_time)
		_hover_tween.tween_property(_hover_icon, "modulate:a", 1.0, hover_content_fade_time)
		_hover_tween.parallel().tween_property(_hover_text, "modulate:a", 1.0, hover_content_fade_time)
		if is_instance_valid(_hover_name):
			_hover_tween.parallel().tween_property(_hover_name, "modulate:a", 1.0, hover_content_fade_time)
	else:
		_hover_tween = create_tween()
		_hover_tween.set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN)
		_hover_tween.tween_property(_hover_icon, "modulate:a", 0.0, hover_content_fade_time * 0.6)
		_hover_tween.parallel().tween_property(_hover_text, "modulate:a", 0.0, hover_content_fade_time * 0.6)
		if is_instance_valid(_hover_name):
			_hover_tween.parallel().tween_property(_hover_name, "modulate:a", 0.0, hover_content_fade_time * 0.6)
		_hover_tween.tween_method(Callable(self, "_apply_hover_layout"), hover_panel_width, 0.0, hover_expand_time)
		_hover_tween.tween_callback(Callable(self, "_finish_hover_close"))

## 收起动画结束：隐藏面板并恢复缩略图原始尺寸/位置。
func _finish_hover_close() -> void:
	if is_instance_valid(_hover_panel):
		_hover_panel.visible = false
	if is_instance_valid(_base):
		_base.size = _base_native_size
		_base.position = -_base.size * 0.5 + base_offset + group_offset

## 计算本次展开的内容与面板高度（图标纹理、文字、量测换行高度）。展开时与意图数据变化时调用。
func _prepare_hover_content() -> void:
	if not is_instance_valid(_hover_icon) or not is_instance_valid(_hover_text):
		return
	var pad := hover_panel_padding
	_hover_icon.texture = _hover_icon_texture()
	_hover_text.text = _compose_detail_text()
	if is_instance_valid(_hover_name):
		_hover_name.text = _type_display_name()
		if _hover_name.label_settings != null:
			_hover_name.label_settings.font_size = hover_name_font_size
	var box: float = hover_icon_box
	var min_h: float = maxf(hover_panel_min_height, box + pad * 2.0)
	var text_w: float = maxf(20.0, hover_panel_width - pad * 2.0 - box - hover_icon_gap)
	_hover_text_w = text_w
	# 使用实际富文本换行高度，短说明保留气泡原始高度。
	_hover_text.size.x = text_w
	var text_h: float = float(_hover_text.get_content_height())
	var left_h: float = box + hover_name_gap + hover_name_font_size + 4.0
	var resting_h: float = _base_native_size.y - hover_panel_top_offset
	_hover_panel_h = maxf(maxf(resting_h, min_h), maxf(left_h, text_h + pad * 2.0))

## 按给定宽度套用面板几何：面板宽度=width（左右对称展开）、缩略图纵向拉伸随展开进度 t 过渡。
## 注意：缩略图(_base)顶边固定在原缩略图顶边（悬停时不移动），仅展开面板顶边叠加 hover_panel_top_offset（相对缩略图下偏）。
func _apply_hover_layout(width: float) -> void:
	if not is_instance_valid(_hover_panel):
		return
	var cx := _hover_center_x()
	var thumb_top: float = base_offset.y + group_offset.y - _base_native_size.y * 0.5   # 缩略图顶边（悬停时保持不动）
	var panel_top: float = thumb_top + hover_panel_top_offset   # 展开组顶边（默认与气泡对齐）
	var t: float = 0.0 if hover_panel_width <= 0.0 else clampf(width / hover_panel_width, 0.0, 1.0)
	var stretched_h: float = maxf(_base_native_size.y, _hover_panel_h + hover_panel_top_offset)
	var bubble_h: float = lerpf(_base_native_size.y, stretched_h, t)
	_hover_panel.size = Vector2(width, bubble_h - hover_panel_top_offset)
	_hover_panel.position = Vector2(cx - width * 0.5, panel_top)
	if is_instance_valid(_base):
		_base.size = Vector2(_base_native_size.x, bubble_h)
		_base.position = Vector2(cx - _base_native_size.x * 0.5, thumb_top)
	_layout_hover_content_box()

## 面板内元素布局：左侧列（图标在上、行为名称紧接其下）整体垂直居中；右侧文字换行、不超出面板。
func _layout_hover_content_box() -> void:
	if not is_instance_valid(_hover_icon) or not is_instance_valid(_hover_text):
		return
	var pad := hover_panel_padding
	var box := hover_icon_box
	var name_h: float = float(hover_name_font_size) + 4.0
	var left_h: float = box + hover_name_gap + name_h        # 左侧列总高（图标 + 间距 + 名称）
	var icon_y: float = (maxf(_hover_panel_h, left_h) - left_h) * 0.5   # 列整体垂直居中（图标上移给名称腾位）
	_hover_icon.size = Vector2(box, box)
	_hover_icon.position = Vector2(pad, icon_y)
	if is_instance_valid(_hover_name):
		_hover_name.position = Vector2(pad, icon_y + box + hover_name_gap)
		_hover_name.size = Vector2(box, name_h)
	_hover_text.position = Vector2(pad + box + hover_icon_gap, pad)
	_hover_text.size = Vector2(maxf(1.0, _hover_text_w), maxf(1.0, _hover_panel_h - pad * 2.0))

## 面板中心 X（与缩略图同心）。
func _hover_center_x() -> float:
	return base_offset.x + group_offset.x

func _hover_icon_texture() -> Texture2D:
	var src: Texture2D = null
	if _active_type != "" and _active_type != "?" and TYPE_GLYPH_REGION.has(_active_type):
		var tr := _type_icons.get(_active_type) as TextureRect
		if tr != null:
			src = tr.texture
	if src == null:
		var full := INTENT_DIR + "type_think.png"
		if FileAccess.file_exists(full + ".import"):
			return load(full) as Texture2D
		return null
	if src.get_size() != Vector2(62.0, 88.0):
		return src   # 源图尺寸变化：回退整图，避免裁错
	var at := AtlasTexture.new()
	at.atlas = src
	var region: Rect2 = TYPE_GLYPH_REGION[_active_type]
	at.region = region
	return at

## 详细意图说明（bbcode：抖音体正文 + 扁桃体关键数值 + 红▲/绿▼）。
## 行为名称（攻击/状态/防御…）已显示在图标下方，故此处不再重复。
func _compose_detail_text() -> String:
	# 优先使用意图数据自带的原话文案（display）；无则回落类型模板。
	if _active_display != "":
		var txt: String = _active_display
		if txt.contains("%VALUE%"):
			txt = txt.replace("%VALUE%", _format_value_bbcode(_active_value, _active_value_base))
		return txt
	match _active_type:
		"?":
			return "未知意图，行动未揭晓"
		"think":
			return "本次不采取行动"
		_:
			var sc: String = _active_scope          # 单体 / 群体
			var val: String = _format_value_bbcode(_active_value, _active_value_base)
			match _active_type:
				"att":
					return "%s，造成 %s 点伤害" % [sc, val]
				"shield":
					return "%s，提供 %s 点护盾" % [sc, val]
				"state":
					return "%s，施加 %s 层状态" % [sc, val]
				_:
					return "—"

## 关键数值 bbcode：扁桃体字体 + 放大字号 + 前后留空位（与卡牌一致）；
## 当 base_val 非空且与当前值不同（数值被士气等 buff 修正）时，按方向补红▲(上升)/绿▼(下降)。
## 字体/字号/箭头着色统一交由 EffectTextFormat.key_number（与卡牌效果文字同一套格式规格）。
func _format_value_bbcode(raw_val: String, base_val: String) -> String:
	if raw_val == "":
		return "?"
	var disp: String = raw_val.replace("*", "×")
	var arrow: String = ""
	if base_val != "" and base_val != raw_val:
		var cur: float = _value_magnitude(raw_val)
		var basem: float = _value_magnitude(base_val)
		if cur != basem:
			arrow = "▲" if cur > basem else "▼"
	return EffectTextFormat.key_number(disp, arrow, hover_keyval_font_size)

## 取值数值的「量级」用于 ▲/▼ 方向比较：支持 "N"（层数/护盾）与 "N*M"（连击*伤害，相乘）。
func _value_magnitude(s: String) -> float:
	if s == "":
		return 0.0
	var txt: String = s.replace("×", "*")
	var parts: PackedStringArray = txt.split("*")
	var m: float = 1.0
	for p in parts:
		var v: float = float(p.strip_edges())
		m *= v if v != 0.0 else 1.0
	return m

## 剥离 bbcode 标签（量测换行高度用：标签不参与可见文本宽度/行数）。
func _strip_bbcode(s: String) -> String:
	var re := RegEx.new()
	re.compile("\\[/?[^\\]]*\\]")
	return re.sub(s, "", true)

func _type_display_name() -> String:
	match _active_type:
		"att": return "攻击"
		"shield": return "防御"
		"state": return "状态"
		"think": return "思考"
		"?": return "未知"
		_: return ""

func _value_display() -> String:
	if _active_value == "":
		return "?"
	return _active_value.replace("*", "×")

## 生成随机意图数值文字（格式 N*M：N=连击次数 1~3、M=每击伤害，基线 2）。
## 用户 2026-09-21 确立：意图数值改为「连击*伤害」(如 1*2 = 1连击·2伤害；3*2 = 3连击·2伤害)，
## 气泡直接显示紧凑的 "1*2"，分辨率 = N×M。state/shield 基线数值 = 2（层数/护盾值）。
## 用于尚未接入真实「单位战斗意图」数据前临时填充；接入后应由意图数据算出具体值并经 set_intent 传入。
static func random_intent_value(type_id: String = "") -> String:
	if type_id == "think":
		return ""
	if type_id == "att":
		var combo := randi_range(1, 3)   # 连击次数 1~3
		var dmg_per := 2                  # 每击伤害（基线常量）
		return "%d*%d" % [combo, dmg_per] # 格式 N*M
	# state / shield：基线数值 2（士气/出血层数 或 护盾值）
	return "2"
