class_name HandManager
extends Node2D

## 公共手牌、EP 连锁选择与现有卡牌动画。出牌及整备时钟由 BattleRoot 统一处理。
const ROLES: Array[String] = ["Zhouzhou", "Irene", "Rocket"]

## 按角色分牌库（抽牌堆+弃牌堆）。惰性初始化：首次抽牌时从 BattleRoot.Units 取玩家角色建库。
var deck: DeckManager = null
## 最近一次抽牌/生成闪回的玩家角色，供「打空爆发抽牌」从对应牌库补抽。
## 抽牌顺序：按【槽位】从右→左（炮炮→艾琳→周周）。
## 独立演出组（每次出牌 spawn 一个、播完自毁），取代 Battle.tscn 静态 CutsceneController/CutsceneInsert。
const CUTSCENE_GROUP_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_group.gd")
## 卡牌 → 鼠标 虚线瞄准指示（纯代码绘制，二次贝塞尔弧度 + 末端箭头）。
const AIM_LINE_SCRIPT := preload("res://battle/scripts/cards/aim_line.gd")
## 卡牌快照右侧「词条解释」面板（复用 buff 描述框视觉）：锚点卡进入托盘时构建。
const COMBO_GLOSSARY_SCRIPT := preload("res://battle/scripts/cards/combo_glossary.gd")


@export_group("Layout")
@export var hand_scale: float = 0.42
@export var hand_y: float = 810.0            # 手牌基准 Y
@export var hand_offset_x: float = -40.0        # 整个手牌组水平偏移（正=右移，负=左移）
@export var regular_spacing: float = 70.0   # 常规间距：单排相邻卡中心基准间距(px)，随张数系数与悬停放大在其上浮动
@export var hover_lift: float = 75.0          # 悬停时卡牌上移(px)
@export var hover_scale: float = 1.4

# ── 灵活自动间距：参考 Demo 工程的 resize_table 自动化，按卡数算「随张数变化的间距系数」──
# 低数量→系数>1(间距放大)；数量增多→系数趋近 1(常规)→继续增则<1(间距缩小)；clamp 到 [min,max]。
@export var auto_fit: bool = true            # 总开关：false 则固定 regular_spacing 不变
@export var hand_margin: float = 40.0        # 手牌区左右留白(px)：可用宽 = 视口宽 - 2*hand_margin
@export var min_fit_scale: float = 0.45      # 随张数系数下限：防多卡缩太狠完全重叠看不见
@export var max_fit_scale: float = 1.6       # 随张数系数上限：低数量时最大放大到 1.6 倍常规间距
# ── 悬停查看：高亮卡左右两侧间距各额外放大 hover_boost，使非高亮卡与高亮卡留出更多间距 ──
@export var hover_boost: float = 48.0        # 悬停卡与其左/右邻卡之间额外增加的间距(px)

@export_group("Hand Rule 2 - Draw")
@export var opening_draw_count: int = 5       # 开局从公共卡池(全部角色)随机抽几张

@export_group("Animation")
@export var anim_dur: float = 0.35
@export var stagger_ms: float = 80.0
@export var hover_anim_dur: float = 0.15      # 悬停散开时长：比 anim_dur 更短，且悬停时禁用 stagger → 齐刷刷同时散开
@export var cancel_reset_dur: float = 0.15     # 取消选牌后复位动画时长：比 anim_dur 更短、且禁用 stagger → 卡牌齐刷刷快速飞回原位
@export var sound_lead_ms: float = 60.0        # 抽牌音相对卡牌飞入的前置量：让"抽到"的听感抢在视觉之前，消除滞后感（需 < stagger_ms 以免声音挤成一团）

@export_group("Test")

@export_group("Combo Tray (1+1+1 visual)")
## 复用 cardtest 场景「封装3D」按钮的伪3D 思路：首张卡牌的 CardRoot 渲进离屏 SubViewport，
## 正面/背面各一个 fake3d 显示层(背面 y_rot 偏移 -180°，cull_back 自动剔除背对面)，得到「真3D 翻面」快照。
## 复用「旋转卡牌」按钮：每次 +1 给该快照注入旋转初速度(刷新式，连击续转)，角速度衰减后弹簧回正到正面。
## 仅首张(anchor)做伪3D 封装 + 旋转；+1 的卡牌只淡出隐藏、不进托盘、不旋转。
@export var combo_tray_margin: float = 200.0    # 托盘右缘距屏幕右边缘(px)：更小=更靠右（已调回原值，快照不再特意让位）；词条面板会在右侧放不下时自动改放快照左侧空白区
@export var combo_tray_lift: float = 70.0       # 托盘相对手牌基准Y上移量：更小=更靠下（置于手牌右下方）
@export var combo_snap_scale: float = 0.5       # 托盘内伪3D快照缩放
@export var combo_fade_dur: float = 0.05        # 原卡淡出时长(需求指定 0.05)
@export var combo_move_dur: float = 0.12        # 快照滑入托盘时长
@export var combo_layout_dur: float = 0.08      # 剩余手牌重排时长（与淡出同样迅速）
## 连锁快照（托盘）相对默认落点的额外偏移：x 负=左移，y 负=上移（用于微调预览位置）。
@export var combo_snap_offset: Vector2 = Vector2(-40.0, -20.0)   # 左移40 / 上移20
## 旋转「刷新冲量」参数（镜像 cardtest 旋转卡牌，纯视觉手感可调）：
@export var combo_spin_omega: float = 95.0      # 每次+1注入的旋转初速度(rad/s)
@export var combo_spin_drag: float = 5.0        # 角速度衰减率(1/s)
@export var combo_omega_threshold: float = 8.0  # 角速度下限(rad/s)→切弹簧回正
@export var combo_settle_stiffness: float = 72.0  # 回正弹簧刚度（越大拉回越猛、过冲越明显）
@export var combo_settle_damping: float = 12.0    # 回正阻尼（越大越黏、过冲越小）
@export var combo_shift_y: float = 80.0
## 伪3D 倾斜：复用 cardtest 的「x_rot 常驻微倾斜 + 鼠标跟随」——保证封装卡透视始终可见（否则只写 y_rot 会显扁平，像丢掉了伪3D）。
@export var combo_tilt_base: float = 12.0         # 常驻微倾斜(度)：封装卡始终带透视、不显扁平（加大→形变更明显）
@export var combo_tilt_max: float = 20.0           # 鼠标悬停显示层时额外跟随倾斜幅度(度)：越大=查看范围越大

@export_group("Debug")
## 调试预览：非空(如 "Zhouzhou")→强制该角色组为「激活」(原色+展开)、其余组显示「未激活」压暗，

@export_group("Zones (debug)")
@export var show_zones: bool = true


var _cards: Array[HandCard] = []
var _hovered: HandCard = null


## 每张卡当前活跃的位移/缩放 tween（值为普通 Array，可同时存「位置 tween」与「缩放回弹 tween」）。
## 严格模式下 Dictionary 不能存 typed Array[Tween]，故 kill 时逐元素 as Tween 转换。
## 切换悬停/弃牌前 kill 全部，避免多个 tween 抢同一属性导致位置/缩放被覆盖。
var _card_tweens: Dictionary = {}
## 高亮框内玩家单位对应角色（仅驱动 _refresh_inactive 压暗；间距由 layout 的随张数系数与悬停放大决定）。
## 防重入：重新发牌/回收动画进行中不重复触发 redraw。
var _redrawing: bool = false

## ── 选牌状态机（点击打出流程的过程层；效果实装由外部接 card_used 信号）──
## 当前选中的卡（非 null = 已进入「选靶」态）。
var _selected: HandCard = null
## 选为友方技能目标的手牌（接不上的牌）。
var _skill_card: HandCard = null
## 1+1 组合链（无上限）：按点击顺序的已选卡；_selected 恒为「最新按下的卡」= 当前链末(上一手)。
## 满足条件(与链末 同色 或 同费用)方可加入；不满足条件者不参与组合、仍可点击加入（视觉上原卡淡出进托盘、其余手牌重排）。
var _combo_chain: Array[HandCard] = []
## 组合视觉（复用 cardtest 封装3D + 旋转卡牌）：仅首张(anchor)做伪3D 封装快照 → 右侧托盘，+1 刷新其旋转冲量。
## anchor 的 CardRoot 被 reparent 进 front_viewport 渲染；背面设计渲进 back_viewport；两个 fake3d 显示层叠放。
const FAKE3D_SHADER: Shader = preload("res://battle/scripts/cards/fake3d.gdshader")
const COMBO_CARD_W: float = 400.0   # 与正面卡牌底图一致(card_base/card_frame 400x576)，卡背对齐
const COMBO_CARD_H: float = 576.0
const COMBO_VP_SCALE: float = 2.0   # 视口取卡 2x 边长，卡在视口内保持原生 scale=1、只占中心 50%，给透视过扫留边距
var _combo_tray: Node2D = null
var _combo_anchor: HandCard = null    # 首张（伪3D 封装来源 + 旋转目标）
var _combo_front_vp: SubViewport = null
var _combo_back_vp: SubViewport = null
var _combo_front_disp: TextureRect = null
var _combo_back_disp: TextureRect = null
var _combo_card_back: Node2D = null   # 卡背节点（渲染进 back_vp）
var _combo_glossary: ComboGlossary = null   # 快照右侧词条解释面板（构建于 _combo_package，销毁于 _combo_unpackage）
## 旋转物理状态机：IDLE(静止) → SPINNING(角速度衰减中) → SETTLING(弹性回正中)
enum _ComboSpin { IDLE, SPINNING, SETTLING }
var _spin_state: int = _ComboSpin.IDLE
var _spin_theta: float = 0.0          # 当前翻面角(rad)，连击时连续累积不归零（刷新冲量而非重置）
var _spin_omega: float = 0.0          # 当前角速度(rad/s)
var _settle_target: float = 0.0       # 回正锁定目标角：进入 SETTLING 算一次后固定，杜绝无限回弹
## 伪3D 跟随倾斜（复用 cardtest 鼠标跟随 + 常驻微倾斜，保证 3D 始终可见）
var _combo_pointer_inside: bool = false
var _combo_cur_x: float = 0.0
var _combo_cur_y: float = 0.0
## _combo_ring_count = 当前链环数（= 链长-1，锚点不计）；_combo_pending_boost = 已加链但文字尚未在「背面」替换。
## 加链时置 pending；_combo_spin_integrate 在旋转至背面(正面不可见)时一次性重建原子效果文案，正面转回时无缝显示新值。
var _combo_ring_count: int = 0
var _combo_pending_boost: bool = false
## 当前伪3D 快照卡中心（全局坐标）：_combo_unpackage 时记录，供「取消飞回手牌 / 打出飞弃牌」都从快照原位起步，
## 消除「快照销毁 → 卡牌在手牌原位闪现」的断点（直接从快照停过的位置接飞）。
var _combo_snap_center_global: Vector2 = Vector2.ZERO
## 卡牌 → 鼠标 虚线瞄准指示节点（选靶态显示，纯代码绘制）。
var _aim_line: AimLine = null

## 特殊开关：允许点击「未激活」手牌组。默认 false（未激活组不可选）；选第一张后临时置 true，
## 使 1+1 的第二张同费卡可来自任意组(含未激活)；clear_selection / redraw 时复位 false。
var allow_inactive_selection: bool = false

## 卡牌被选中（已进入选靶态）。
signal card_selected(hc: HandCard)
## 选靶取消（右键取消 / 打出后重置），手牌临时位移应取消。
signal card_deselected()
## 选中卡对单位碰撞箱完成「使用」点击（当前无效果，仅过程；后续接效果在此信号）。
signal card_used(hc: HandCard, unit: UnitRoot)

## 选中高亮框（呼吸灯）配色：按该卡费用色（蓝/橙/绿，索引 CardRoot.COST_COLORS）区分；
## 第二张统一灰色。WHITE_* 仅作历史调色板常量保留（当前第一张已改用费用色），无害不影响逻辑。
const COLOR_SELECT_WHITE_A: Color = Color(1.0, 1.0, 1.0, 1.0)
const COLOR_SELECT_WHITE_B: Color = Color(0.45, 0.85, 1.0, 1.0)
const COLOR_SELECT_GRAY_A: Color = Color(0.62, 0.62, 0.66, 1.0)
const COLOR_SELECT_GRAY_B: Color = Color(0.35, 0.5, 0.95, 1.0)


func _ready() -> void:
	add_to_group("hand_manager")
	_build_combo_tray()
	_build_aim_line()


## 卡牌 → 鼠标 虚线瞄准指示节点：挂在本节点下、z 高于组合托盘(2000)，纯代码绘制。
func _build_aim_line() -> void:
	_aim_line = AIM_LINE_SCRIPT.new()
	_aim_line.name = "AimLine"
	_aim_line.z_index = 3000   # 高于托盘(2000) 与手牌(z≤1012)，盖在战场之上
	add_child(_aim_line)


## 当前快照卡中心（全局）：选靶进行中优先取实时伪3D 显示层中心（= 托盘里那张快照卡的真实位置）；
## 兜底用 _last_combo_tray_center_global()。供虚线指示起点使用。
func _combo_live_center_global() -> Vector2:
	if is_instance_valid(_combo_front_disp) and _combo_front_disp.visible:
		return _combo_front_disp.global_position + _combo_front_disp.size * 0.5
	return _last_combo_tray_center_global()


## 取某牌堆分组按钮的全局中心（手牌飞入/飞出锚点）。无对应按钮时返回 ZERO（调用方退化处理）。
func _pile_center(group: StringName) -> Vector2:
	var ns := get_tree().get_nodes_in_group(group)
	if ns.is_empty():
		return Vector2.ZERO
	var c := ns[0] as Control
	if c == null:
		return Vector2.ZERO
	return c.global_position + c.size * 0.5


## 组合托盘：固定在手牌上方右侧的 Node2D 容器，承载组合快照（z 高于手牌区）。
func _build_combo_tray() -> void:
	_combo_tray = Node2D.new()
	_combo_tray.name = "ComboTray"
	var vsize := get_viewport_rect().size
	_combo_tray.position = Vector2(vsize.x - combo_tray_margin, hand_y - combo_tray_lift) + combo_snap_offset
	_combo_tray.z_index = 2000
	add_child(_combo_tray)


## ── 重新发牌：先回收当前所有手牌(飞向弃牌区)，待回收动画结束后再从抽牌矩形重新发牌（顺序化，避免新旧并发叠飞）──
## 整个事务（回收 + 发牌动画）期间 _redrawing=true，手牌交互（悬停查看 / 点击选牌）均锁定。
func redraw() -> void:
	var br := _get_battle_root()
	if is_instance_valid(br):
		br.draw_action()


## 牌库惰性初始化（首次抽牌时按场景玩家角色建堆）；抽牌堆空则洗弃牌堆回补。
func draw_opening() -> void:
	_draw_common_pool(opening_draw_count)

func _ensure_decks() -> void:
	if deck != null:
		return
	deck = DeckManager.new()
	var br := _get_battle_root()
	var roles: Array[String] = []
	if is_instance_valid(br):
		var units := br.get_node_or_null("Units") as Node2D
		if units != null:
			for u in units.get_children():
				if u is UnitRoot and u.unit_faction == "player":
					if not roles.has(u.unit_id):
						roles.append(u.unit_id)
	if roles.is_empty():
		roles = ROLES
	deck.init_decks(roles)
	print("[HM] 牌库初始化完成：角色=%s | %s" % [roles, deck.debug_status()])

## 由 BattleRoot 显式调用：用指定玩家角色列表（从场景 Units 收集）初始化各角色牌库（抽/弃牌堆）。
## 始终重建（覆盖惰性 _ensure_decks 可能产生的占位），确保牌库与当前战斗阵容一致。
func init_decks(roles: Array[String]) -> void:
	deck = DeckManager.new()
	deck.init_decks(roles)
	print("[HM] 牌库显式初始化：角色=%s | %s" % [roles, deck.debug_status()])

## 【闪回】生成临时副本：从 pool_scope 指定池(仅周周/全角色)随机取 count 张，复制出独立 CardData，
func generate_flashback(owner_role: String, pool_scope: String, count: int) -> void:
	if not ROLES.has(owner_role) or count <= 0:
		return
	var pool: Array[CardData]
	match pool_scope:
		"all":
			pool = CardDatabase.new().get_all()
		_:
			pool = CardDatabase.new().get_by_role("Zhouzhou")
	if pool.is_empty():
		return
	pool.shuffle()
	var drawn: Array[HandCard] = []
	for i in count:
		if i >= pool.size():
			break
		var cd: CardData = pool[i].duplicate()   # 临时副本：独立实例，不影响原卡/牌库
		var hc := _spawn_card_for_role(cd, owner_role, true)
		hc.scale = Vector2.ONE * hand_scale * 0.5
		hc.position = _flyin_origin()
		hc.flyin_lock = true
		_cards.append(hc)
		drawn.append(hc)
	layout(true, true, drawn)
	_refresh_inactive()
	print("[HM-flashback] %s 生成 %d 张闪回副本(池=%s)" % [owner_role, drawn.size(), pool_scope])


## 由 CardData 构造一张 HandCard 入树（STATE_HAND），不做定位（由调用方负责位置与 _cards 登记）。
func _spawn_card_for_role(cd: CardData, role: String, is_flashback: bool = false) -> HandCard:
	var card := CardRoot.new()
	var base_number: int = cd.roll_number()
	card.apply_data(cd, base_number)
	var hc := HandCard.new()
	hc.setup(card, _cards.size(), role)
	hc.data = cd                            # 关联静态数据，打出时解析用
	hc.base_number = base_number
	hc.cost = base_number
	card.card_effect = _effect_text(hc)
	hc.is_flashback = is_flashback         # 闪回副本标记
	add_child(hc)            # 保留原卡牌构建和动画
	hc.state = HandCard.STATE_HAND
	return hc


## 仅作视觉抽牌（复用 _draw_one_for_role 随机取该角色池牌，飞入 + 抽牌音；其余手牌静默重排）。

func discard_random_for_role(role: String, count: int) -> void:
	if not ROLES.has(role) or count <= 0:
		return
	var hand: Array[HandCard] = []
	for c in _cards:
		if is_instance_valid(c) and c.state == HandCard.STATE_HAND and c.owner_group == role:
			hand.append(c)
	if hand.is_empty():
		return
	hand.shuffle()
	var n := mini(count, hand.size())
	for i in n:
		var hc: HandCard = hand[i]
		if not is_instance_valid(hc):
			continue
		# 非闪回卡：进对应角色弃牌堆；闪回副本直接销毁（不污染牌库）。
		if deck != null and not hc.is_flashback and is_instance_valid(hc.data):
			deck.discard_card(hc.owner_group, hc.data)
		_cards.erase(hc)
		recycle_card(hc, float(i) * 0.05, false)
		print("[HM-loot] 赃物惩罚：%s 随机弃 %d 张（共 %d）" % [role, n, hand.size()])
	# 弃牌后剩余手牌重排（避免空位残留）。
	layout(true, false, [], false)

## 双轨抽牌·角色池主动抽：供「角色使用某效果(如带抽牌/闪回关键词的卡结算时)」调用，
## 触发源(具体哪张卡/哪个关键词)待接入卡牌效果系统后接线。

## 手牌中是否存在该角色(owner_group==role)的在手牌。

## 当前在手(STATE_HAND)卡牌数——用于判定「手牌打空」以触发公共池爆发抽牌。
func _hand_count() -> int:
	var n := 0
	for c in _cards:
		if is_instance_valid(c) and c.state == HandCard.STATE_HAND:
			n += 1
	return n

## 飞入出发点：牌库按钮(组 "deck_pile")中心；无按钮时退化为左下角兜底。
func _flyin_origin() -> Vector2:
	var c := _pile_center(&"deck_pile")
	if c != Vector2.ZERO:
		return c
	return Vector2(130.0, hand_y)


## 步长向右为正、右卡层级高(顶层)；悬停/选中抬升由 _hover_card/_raise_card 处理。 ──
## 随张数变化的间距系数（灵活自动间距核心）：
## 以 regular_spacing 为锚算「理想跨度」，与可用宽比较 → coeff = 可用宽 / 理想跨度。
## 低数量(理想跨度 ≪ 可用宽)→ coeff 被 clamp 到 max_fit_scale(>1，间距放大)；
## 适中(恰好放得下)→ coeff=1.0(常规间距)；高数量(放不下)→ coeff<1(间距缩小，下限 min_fit_scale)。
func _count_fit_coeff(n: int) -> float:
	if not auto_fit:
		return 1.0
	if n <= 0:
		return 1.0
	var avail: float = get_viewport_rect().size.x - 2.0 * hand_margin
	var card_w := _native_width() * hand_scale
	var ideal_span := card_w + float(n - 1) * regular_spacing
	return clampf(avail / ideal_span, min_fit_scale, max_fit_scale)

## 实际间距 = regular_spacing * 随张数系数；悬停卡左右另 +hover_boost。悬停/选中抬升由 _hover_card/_raise_card 处理。
## hover_transition=true → 不论进入还是离开悬停，所有卡都用更短时长(hover_anim_dur)且禁用 stagger → 齐刷刷同时散开/收回。
## reset_dur>=0 → 取消选牌复位专用：覆盖 dur 为更短时长且禁用 stagger（齐刷刷快速飞回，不含 combo/悬停语义）。
func layout(animated: bool = true, play_sounds: bool = true, sound_cards: Array[HandCard] = [], hover_transition: bool = false, reset_dur: float = -1.0) -> void:
	# combo 进行中：已在组合里的卡已在手牌淡出隐藏，不再参与手牌排布（由托盘快照呈现）。
	var active: Array[HandCard] = _cards.filter(func(c: HandCard): return is_instance_valid(c) and c.state == HandCard.STATE_HAND and not _combo_chain.has(c))
	if active.is_empty():
		return
	var vsize := get_viewport_rect().size
	var card_w := _native_width() * hand_scale

	# 排序：同角色相邻（按 ROLES 顺序），同角色内按 费用类型→费用值。
	active.sort_custom(func(a: HandCard, b: HandCard) -> bool:
		var ra := ROLES.find(a.owner_group)
		var rb := ROLES.find(b.owner_group)
		if ra != rb:
			return ra < rb
		if a.card.cost_type != b.card.cost_type:
			return a.card.cost_type < b.card.cost_type
		return a.card.cost_value < b.card.cost_value)

	var n := active.size()
	var coeff := _count_fit_coeff(n)             # 随张数变化的间距系数(低数量放大/高数量缩小)
	var base_step: float = regular_spacing * coeff   # 常规(数量调整后)间距，保留卡清晰度仅改间距
	# 逐间隙计算：悬停卡与其左/右邻卡之间的间隙额外 +hover_boost，使高亮卡左右留出更多空间。
	var gaps: Array[float] = []
	gaps.resize(n)
	for i in n:
		var g := base_step
		if i > 0 and (_hovered == active[i] or _hovered == active[i - 1]):
			g += hover_boost
		gaps[i] = g
	var span: float = card_w
	for i in n:
		if i > 0:
			span += gaps[i]
	var start_x: float = (vsize.x - span) * 0.5 + card_w * 0.5 + hand_offset_x
	var cursor: float = start_x
	for i in n:
		if i > 0:
			cursor += gaps[i]
		var c := active[i] as HandCard
		var x: float = cursor
		_kill_card_tween(c)   # 杀旧 tween(含悬停抬升)，避免与本次定位 tween 抢 position
		# 索引大=右侧；层级从右往左（右卡盖左卡，叠放时右卡在上）；悬停卡置顶。
		c.base_z = 10 + i
		c.z_index = 1000 if c == _hovered else c.base_z
		c.home_pos = Vector2(x, hand_y)
		c.set_hit_params(hand_scale, hover_lift)
		# 悬停路径(进入或离开)：所有卡同时(无 stagger)用更短时长(hover_anim_dur)散开/收回 → 齐刷刷；
		# 非悬停(发牌/重抽)走原 anim_dur + stagger。hover_transition 同时覆盖「进入悬停」与「离开悬停」两种情形。
		# combo 进行中：剩余手牌用更短时长(combo_layout_dur)、无 stagger 快速重排（与淡出同样迅速）。
		var combo_active: bool = _combo_chain.size() > 0
		var fast: bool = hover_transition or combo_active
		var dur: float = combo_layout_dur if combo_active else (hover_anim_dur if fast else anim_dur)
		if reset_dur >= 0.0:
			dur = reset_dur           # 取消复位：用更短时长
		var anim_delay: float = 0.0 if (fast or combo_active or reset_dur >= 0.0) else float(i) * stagger_ms / 1000.0
		var target_pos: Vector2 = Vector2(x, hand_y)
		# 接不上牌堆顶的牌固定下移，一眼区分可出 / 不可出；选为技能目标的牌保持抬起。
		if c == _skill_card:
			target_pos = Vector2(x, hand_y - hover_lift)
		elif not is_playable(c):
			target_pos = Vector2(x, hand_y + combo_shift_y)
		if animated:
			var tw := create_tween().set_parallel(true)
			# 仅列出的卡(新抽)立即飞入、与抽牌音同步；其余卡按 stagger 静默重排。
			if not sound_cards.is_empty() and sound_cards.has(c):
				anim_delay = 0.0
			tw.tween_property(c, "position", target_pos, dur).set_delay(anim_delay)
			tw.tween_property(c, "scale", Vector2.ONE * hand_scale, dur).set_delay(anim_delay)
			# 抽牌音随飞入错峰播放：play_sounds 且（sound_cards 空=全量发牌/redraw 全响；非空=仅列出的卡）响。
			if play_sounds and (sound_cards.is_empty() or sound_cards.has(c)):
				var snd_delay: float = maxf(0.0, anim_delay - sound_lead_ms / 1000.0)
				tw.tween_callback(SfxManager.play_random_draw).set_delay(snd_delay)
			_card_tweens[c] = [tw]
			if c.flyin_lock:
				# 飞入完成 → 解锁；允许后续 layout(false) 正常瞬间定位（避免 flyin_lock 卡死）
				tw.tween_callback(_unlock_flyin.bind(c))
		else:
			# 飞入锁定中的卡：禁止瞬间定位（让其正在进行的飞入 tween 继续带它到 home），否则飞入被瞬杀不可见。
			if not c.flyin_lock:
				c.position = target_pos
				c.scale = Vector2.ONE * hand_scale


## 高亮框(第0格)玩家单位变化时：该单位对应角色手牌组间距 +20px，其余组 -20px；非玩家则复原。
## uid 即角色名（与 ROLES 一致）。重排生效。
func _unlock_flyin(hc: HandCard) -> void:
	hc.flyin_lock = false   # 飞入 tween 完成后由 layout 调用，解除「禁止 layout(false) 瞬移」锁定


func _refresh_inactive() -> void:
	for card in _cards:
		if not (is_instance_valid(card) and is_instance_valid(card.card) and card.card.get_parent() == card):
			continue
		if _hand_locked or (not is_playable(card) and card != _skill_card):
			card.card.set_inactive()
		else:
			card.card.set_dim(0.0)


## 回收：错峰 delay 后飞向弃牌矩形并缩小，结束后移除（先收完再重发由 redraw 的 await 保证顺序）。
## fade_in=true：调用方已把卡牌挪到「快照原位」并显形，此处先透明再淡入 → 从快照处无缝接飞（消除手牌硬闪现）。
func recycle_card(hc: HandCard, delay: float = 0.0, fade_in: bool = false) -> void:
	if not is_instance_valid(hc):
		return
	if _hovered == hc:
		_hovered = null
	hc.state = HandCard.STATE_PLAYED
	_kill_card_tween(hc)
	var dest := _pile_center(&"discard_pile")
	if dest == Vector2.ZERO:
		dest = Vector2(get_viewport_rect().size.x - 130.0, hand_y)
	hc.modulate = Color(1.0, 1.0, 1.0, 0.0) if fade_in else Color.WHITE   # fade_in：先透明再淡入（飞弃牌时无缝接快照）
	var tw := create_tween().set_parallel(true)
	tw.tween_property(hc, "position", dest, anim_dur).set_delay(delay)
	tw.tween_property(hc, "scale", Vector2.ONE * hand_scale * 0.3, anim_dur).set_delay(delay)
	if fade_in:
		tw.tween_property(hc, "modulate:a", 1.0, 0.15).set_delay(delay)
	# 关键：parallel 模式下 tween_callback 默认 delay=0 会在 t=0 立即触发 → 卡牌当帧 queue_free，
	# 回收动画根本来不及播（表现=手牌瞬间消失、随后才重发）。必须显式延迟到「该卡动画结束」才 free。
	tw.tween_callback(hc.queue_free).set_delay(delay + anim_dur)
	_card_tweens[hc] = [tw]   # 跟踪以便后续 redraw 杀掉（卡 free 后 tween 失效，由 is_instance_valid 兜底）

## ── 悬停：每帧权威命中，仅变化时刷新（规避 mouse_exited 漏发残留）──
func _process(_delta: float) -> void:
	if _redrawing or _cutscene_playing or _hand_locked:
		return
	_combo_spin_integrate(_delta)   # 首张伪3D快照旋转（连击刷新力度，不依赖悬停态）
	_combo_apply_spin_to_display(_delta)  # 把翻面角 + 倾斜写入伪3D 显示层（正面/背面）
	var gp := get_global_mouse_position()
	if _selected != null:
		# 虚线瞄准指示：起点=托盘快照卡中心，终点=鼠标；仅「需手选目标」的卡(单体蓝卡)显示，自动结算卡(全场/自身/执行/引导)不显示。
		var anchor := _combo_chain[0] if not _combo_chain.is_empty() else _selected
		if is_instance_valid(anchor) and _needs_manual_target(anchor):
			var p0g := _combo_live_center_global()
			if p0g != Vector2.ZERO:
				_aim_line.set_endpoints(_aim_line.to_local(p0g), _aim_line.to_local(gp))
			else:
				_aim_line.hide_line()
		else:
			_aim_line.hide_line()
		# 选靶(组合)中：悬停态不冻结「匹配卡」——满足连锁条件的卡(含已入链)仍可被悬停放大查看，
		# 与第一张卡牌(anchor)的「单卡悬停放大」情景一致；仅不匹配 anchor 的卡固定下移、不响应悬停放大。
		var hit := _pick_combo_hoverable(gp)
		if hit != _hovered:
			_set_hover_combo(hit)
		return
	_aim_line.hide_line()   # 非选靶态：确保指示隐藏
	var hit: HandCard = null
	for c in _cards:
		if is_instance_valid(c) and c.state == HandCard.STATE_HAND and c.is_point_inside(gp):
			if hit == null or c.base_z > hit.base_z:
				hit = c
	if hit != _hovered:
		_set_hover(hit)


func _set_hover(hc: HandCard) -> void:
	_hovered = hc
	layout(true, false, [], true)
	if is_instance_valid(hc):
		_hover_card(hc)


## 选靶态悬停命中：任意「在手牌 + 未选中 + 命中」的最上层卡可放大查看。
func _pick_combo_hoverable(global_pos: Vector2) -> HandCard:
	var best: HandCard = null
	for c in _cards:
		if not (is_instance_valid(c) and c.state == HandCard.STATE_HAND and not _combo_chain.has(c)):
			continue
		if not c.is_point_inside(global_pos):
			continue
		if best == null or c.base_z > best.base_z:
			best = c
	return best


## 组合态悬停切换：仅给「匹配卡」施加放大/置顶（复用单卡 _hover_card），与第一张卡牌的悬停情景一致；
func _set_hover_combo(hc: HandCard) -> void:
	_hovered = hc
	# 重新布局：高亮(匹配)卡左右额外放大 hover_boost，其余匹配卡留原位、非匹配卡保持下移。
	layout(true, false, [], true)
	if is_instance_valid(hc):
		_hover_card(hc)


## 杀掉该卡当前活跃 tween（切换悬停/弃牌/重抽前调用），杜绝多个 tween 抢同一属性。
func _kill_card_tween(hc: HandCard) -> void:
	if _card_tweens.has(hc):
		# Dictionary 值本质是普通 Array（严格模式下不能存/转 typed Array[Tween]），逐元素 as Tween 才是安全写法。
		for t in _card_tweens[hc]:
			var tw := t as Tween
			if is_instance_valid(tw):
				tw.kill()
		_card_tweens.erase(hc)


## 悬停：仅被悬停的卡牌抬到最前(z=1000)、放大并上移 hover_lift(px)。
## 邻卡保持原位、不推动——悬停卡压在最前完整可见，鼠标移开即回到原位。
## 先 kill 该卡其它 tween（尤其是发牌 deal tween），保证上移/放大不被同属性的发牌 tween 覆盖。
func _hover_card(hc: HandCard) -> void:
	_kill_card_tween(hc)
	# 位置：独立 tween 平滑抬升，与缩放回弹并发。
	var pos_tw := create_tween()
	pos_tw.tween_property(hc, "position", Vector2(hc.home_pos.x, hand_y - hover_lift), 0.18)
	# 缩放：两段式手动过冲（先冲过 ~15% 再落定），纯 EASE_OUT 只过冲一次、不回振，幅度介于 BACK(10%) 与 ELASTIC(30%) 之间。
	var scale_tw := create_tween()
	var over_scale := Vector2.ONE * hand_scale * hover_scale * 1.15
	var settle_scale := Vector2.ONE * hand_scale * hover_scale
	scale_tw.tween_property(hc, "scale", over_scale, 0.14).set_ease(Tween.EASE_OUT)
	scale_tw.tween_property(hc, "scale", settle_scale, 0.2).set_ease(Tween.EASE_OUT)
	_card_tweens[hc] = [pos_tw, scale_tw]
	hc.z_index = 1000


## 选中/配对卡的统一「抬起」视觉：上移 hover_lift、放大 hover_scale、置顶 z(1000)。
## 第一张与第二张同费卡共用，使两张被选中的卡都清晰可见（放大比例 / 位移距离与悬停一致）。
## 高亮框由调用方在 _raise_card 之后按「第一张/第二张」分别 set_select_frame；此处先清旧高亮框防御。
func _raise_card(hc: HandCard) -> void:
	hc.clear_select_frame()
	_kill_card_tween(hc)
	var pos_tw := create_tween()
	pos_tw.tween_property(hc, "position", Vector2(hc.home_pos.x, hand_y - hover_lift), 0.18)
	# 缩放：选牌不抖——平滑线性放大到设定比例（无 ELASTIC 回弹）。
	var scale_tw := create_tween()
	scale_tw.tween_property(hc, "scale", Vector2.ONE * hand_scale * hover_scale, 0.18)
	_card_tweens[hc] = [pos_tw, scale_tw]
	hc.z_index = 1000


## 复位单卡到「待选」基准态：回到 home_pos、基准缩放、基准 z。并清选中高亮框。
## 取消选靶 / 打出后、以及切换配对卡时，把卡牌从「抬起」态还原（含缩放复位，避免 hover_scale 残留）。
func _lower_card(c: HandCard) -> void:
	_kill_card_tween(c)
	c.visible = true                 # 复位：组合淡出隐藏过的卡取消时重新可见
	c.modulate = Color.WHITE        # 复位：清除淡出残留的 alpha=0
	var pos_tw := create_tween()
	pos_tw.tween_property(c, "position", c.home_pos, 0.2)
	# 缩放：取消选牌不抖——平滑回基准缩放（无 ELASTIC 回弹）。
	var scale_tw := create_tween()
	scale_tw.tween_property(c, "scale", Vector2.ONE * hand_scale, 0.2)
	_card_tweens[c] = [pos_tw, scale_tw]
	c.z_index = c.base_z
	c.clear_select_frame()


## ── 选牌 / 选靶流程（过程层，无效果实装）──
## 鼠标输入：左键点已激活组卡牌=选中；选靶中左键忽略(留给 BattleRoot 点单位)；右键=取消。
func _input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	var mouse := event as InputEventMouseButton
	if not mouse.pressed or _redrawing or _cutscene_playing or _hand_locked:
		return
	if mouse.button_index == MOUSE_BUTTON_RIGHT:
		clear_selection()
		return
	if mouse.button_index != MOUSE_BUTTON_LEFT:
		return
	# 使用本次点击坐标，不依赖上一帧悬停缓存。
	var point: Vector2 = get_canvas_transform().affine_inverse() * mouse.position
	if _selected != null:
		# 选中后点另一张牌 = 改选；一次只打一张。
		var candidate := _pick_chain_candidate(point)
		if is_instance_valid(candidate):
			clear_selection()
			select_card(candidate)
		return
	var clicked := _pick_topmost_card(point)
	if is_instance_valid(clicked):
		if clicked == _skill_card:
			clear_skill_card()
		else:
			select_card(clicked)

## 能否接上当前牌堆顶（同色或点数相同）。由 BattleRoot 的 ChainRules 判定。
func is_playable(hc: HandCard) -> bool:
	var br := _get_battle_root()
	if not (is_instance_valid(br) and is_instance_valid(hc)):
		return false
	return br.rules.can_play(hc.cost_color, hc.base_number)

## 刷新所有手牌的效果文字（角色倒下等），并重算可出 / 不可出。
func refresh_numbers() -> void:
	for c in _cards:
		if not (is_instance_valid(c) and is_instance_valid(c.card) and is_instance_valid(c.data)):
			continue
		c.card.cost_value = c.base_number
		c.card._apply_cost()
		c.card.set_effect_text(_effect_text(c))
	if _selected == null:
		layout(true, false, [], true)
	_refresh_inactive()

## 卡面效果文字：单次触发的效果值由点数决定；所属角色已倒下时注明无效果。
func _effect_text(hc: HandCard) -> String:
	var text: String = hc.data.effect_text(hc.base_number, _morale_bonus_for(hc.owner_group))
	var br := _get_battle_root()
	var owner := br._find_unit(hc.owner_group) if is_instance_valid(br) else null
	if is_instance_valid(br) and (not is_instance_valid(owner) or owner.get_current_hp() <= 0):
		text = "（角色已倒下，仅可接牌）"
	return text

## 取鼠标全局坐标命中的「最上层」在手牌（与 _process 悬停命中同规则），供配对点击复用。
func _pick_topmost_card(global_pos: Vector2) -> HandCard:
	var hit: HandCard = null
	for c in _cards:
		if is_instance_valid(c) and c.state == HandCard.STATE_HAND and c.is_point_inside(global_pos):
			if hit == null or c.base_z > hit.base_z:
				hit = c
	return hit


## 组合态(选靶中)点击拾取：任意「鼠标命中 + 未入链 + 在手牌」的最上层(base_z 最高)卡（🔥 不足由 _add_to_chain 拒绝）。
## 组合态悬停已冻结(无 z=1000)，卡牌大量重叠(间距<卡宽)、base_z 右偏，若按 _pick_topmost_card 会常把
## 右侧重叠卡当命中 → 加链被拒 → 啥也不发生。这里直接取最上层命中卡，由 _add_to_chain 统一判 🔥。
func _pick_chain_candidate(global_pos: Vector2) -> HandCard:
	var best: HandCard = null
	for c in _cards:
		if not (is_instance_valid(c) and c.state == HandCard.STATE_HAND and not _combo_chain.has(c)):
			continue
		if not c.is_point_inside(global_pos):
			continue
		if best == null or c.base_z > best.base_z:
			best = c
	return best


## 选中一张卡：起链(anchor)，生成该卡快照进右侧托盘、原卡淡出隐藏、其余手牌快速重排，广播 card_selected；
## 进入选靶态等待玩家点单位确认。即时卡(蓝)按攻击范围盖黑非目标(须点符合目标)；引导/执行卡(橙/绿)不盖黑幕(点任意目标即可打出)。
## 所有卡型在有🔥时都可先加链环再确认——不再对引导/执行卡立即自动打出。
func select_card(hc: HandCard) -> void:
	if not is_instance_valid(hc) or _hand_locked or _cutscene_playing or _redrawing:
		return
	var br := _get_battle_root()
	if br._battle_ended or br.clock_busy:
		return
	clear_skill_card()
	if not is_playable(hc):
		_select_skill_card(hc)
		return
	var caster := br._find_unit(hc.owner_group)
	_combo_chain = [hc]
	_selected = hc
	allow_inactive_selection = true
	_on_card_joined_combo(hc)
	card_selected.emit(hc)
	var candidates := br.v2_targets(hc.data, caster)
	for unit in br._get_units():
		unit.set_dimmed(not candidates.has(unit))

## 接不上的牌：选为技能目标，抬起并高亮；只有能对它用技能的友方保持亮起，点击该友方即可使用。
func _select_skill_card(hc: HandCard) -> void:
	var br := _get_battle_root()
	_skill_card = hc
	_raise_card(hc)
	hc.set_select_frame(false, COLOR_SELECT_WHITE_A, COLOR_SELECT_WHITE_B)
	_refresh_inactive()
	for unit in br._get_units():
		unit.set_dimmed(not br.skill_ready(unit.unit_id, hc))

func get_skill_card() -> HandCard:
	return _skill_card if is_instance_valid(_skill_card) else null

func clear_skill_card() -> void:
	if not is_instance_valid(_skill_card):
		_skill_card = null
		return
	var hc := _skill_card
	_skill_card = null
	hc.clear_select_frame()
	_refresh_inactive()
	if _cards.has(hc):
		_lower_card(hc)
	var br := _get_battle_root()
	if is_instance_valid(br):
		br.clear_all_dim()
	layout(true, false, [], true)

func get_selected() -> HandCard:
	return _selected


## 选中卡对单位碰撞箱完成「使用」点击（BattleRoot 检测后调用）。
func notify_used_on_unit(unit: UnitRoot) -> void:
	if _cutscene_playing or _hand_locked or _redrawing or _combo_chain.is_empty():
		return
	var br := _get_battle_root()
	if br.clock_busy or br._battle_ended:
		return
	var chain: Array[HandCard] = _combo_chain.duplicate()
	var anchor: HandCard = chain[0]
	var caster := br._find_unit(anchor.owner_group)
	if not br.v2_targets(anchor.data, caster).has(unit):
		return
	var payload := br.v2_payload(anchor, unit)
	card_used.emit(anchor, unit)
	for card in chain:
		_cards.erase(card)
		if _hovered == card:
			_hovered = null
		card.state = HandCard.STATE_PLAYED
		_kill_card_tween(card)
		_pending_discard.append(card)
	clear_selection(true)
	_flush_pending_discard()
	layout(true, false)
	await br.play_card(payload)

var _cutscene_playing: bool = false
## 锁定期禁止选牌 / 加链 / 悬停检视，从根本上杜绝「单位演出动画期间被快速选中并二次打出」。
var _hand_locked: bool = false
## 打出后「飞入弃牌库」的组合卡：选牌阶段已淡出隐藏(visible=false)，
## 此处仅从 _cards 移除并暂存，真正的回收在 notify_used_on_unit 内「与演出同时」调用 _flush_pending_discard 触发——复用 recycle_card 飞入弃牌库。
var _pending_discard: Array[HandCard] = []

## 施法者美术态：攻击卡→Attack / 状态卡→Cast / 防御卡→Charge（与 CardData.card_type 对齐）。

## 受击者美术态：被攻击卡(0)命中→Hit；状态/防御卡命中→Idle（常态）。


## 打出后的组合卡：飞入弃牌库（与回收动画 recycle_card 一致），而非静默 free。
## 演出期间组合卡被隐藏(visible=false)，飞入前先恢复可见，否则看不到飞行动画。
func _flush_pending_discard() -> void:
	var tray := _last_combo_tray_center_global()
	var i := 0
	for c in _pending_discard:
		if is_instance_valid(c):
			# 非闪回卡：进对应角色弃牌堆（供洗回抽牌堆循环）；闪回副本直接销毁，不污染牌库。
			if deck != null and not c.is_flashback and is_instance_valid(c.data):
				deck.discard_card(c.owner_group, c.data)
			c.visible = true
			if is_instance_valid(c.card):
				c.card.visible = true
			# 从「快照原位」起步飞弃牌（非手牌原位）：快照销毁后卡牌在此处无缝接飞，消除「消失又闪现」。
			c.global_position = tray
			recycle_card(c, float(i) * 0.05, true)   # fade_in=true：在托盘原位淡入后飞走，比硬显形更顺
		i += 1
	_pending_discard = []

## 牌库版·打空补抽：本次打出后手牌被打空(0 张在手)时，从【当前玩家角色牌库】补抽 empty_hand_draw 张。
## 取代旧「公共池爆抽」——现在手牌来源统一为各角色牌库，避免从全局池无限注入。

func _draw_common_pool(count: int) -> void:
	if count <= 0:
		return
	_ensure_decks()
	var drawn: Array[HandCard] = []
	for cd in deck.draw_common(count):
		var hc := _spawn_card_for_role(cd, cd.belong_to)
		hc.scale = Vector2.ONE * hand_scale * 0.5
		hc.position = _flyin_origin()
		hc.flyin_lock = true
		_cards.append(hc)
		drawn.append(hc)
	layout(true, true, drawn)
	_refresh_inactive()


## 无插入演出的「使用结算」记账：用于引导/执行卡——使用时不播演出组插入（生效时才播），
## 与 _on_cutscene_finished 的区别：不 spawn 可见演出组、不动 _cutscene_playing（本就没有演出在播，输入不该被锁住整段动画时长）。


## committed=true 表示「成功打出后清选」（保留已扣 🔥）；committed=false 表示右键取消（退回已花在链环上的 🔥）。
func clear_selection(committed: bool = false) -> void:
	clear_skill_card()
	if _selected == null:
		return
	var chain := _combo_chain.duplicate()   # 取消时需要让链中卡从快照原位飞回手牌，先留存引用
	# 复位连锁数值升级状态（无论成功/取消）
	_combo_ring_count = 0
	_combo_pending_boost = false
	_selected = null
	_combo_chain = []
	_hovered = null
	allow_inactive_selection = true   # v2 所有存活角色都可出牌
	_clear_combo_visuals()   # 释放托盘快照、复位旋转态（内部记录快照中心 _combo_snap_center_global）
	_aim_line.hide_line()     # 取消/打出：立即隐藏虚线瞄准指示（下一帧 _process 也会兜底隐藏）
	var tray := _last_combo_tray_center_global()
	if not committed:
		# 取消：链中卡仍在手牌，先挪到「快照原位」再飞回 —— 不在手牌原位硬显形（消除闪现）。
		for c in chain:
			if is_instance_valid(c) and _cards.has(c):
				c.visible = true
				c.modulate = Color.WHITE
				c.global_position = tray
	_restore_all()           # 复位所有手牌（含曾被隐藏的组合卡：visible=true + modulate=白 + 回 home_pos）
	var reset_dur := cancel_reset_dur if not committed else -1.0   # 取消：更短、无 stagger 快速飞回；成功打出：走常规 anim_dur
	layout(true, false, [], false, reset_dur)   # 重新计算所有 home_pos（combo 已空 → 正常展开）；取消卡从 tray 起步快速飞回
	var br := _get_battle_root()
	if is_instance_valid(br):
		br.clear_all_dim()
	_refresh_inactive()   # 取消/打出后按高亮框角色重算未激活压暗（卡牌不再整卡灰掉）
	card_deselected.emit()


## ── 组合视觉：首张伪3D 封装快照(复用 cardtest 封装3D) + 旋转(复用 旋转卡牌) ──
## 仅首张(anchor)做伪3D 封装 → 右侧托盘；+1 的卡牌只淡出隐藏(不进托盘、不旋转)，并刷新首张旋转冲量。
func _on_card_joined_combo(hc: HandCard) -> void:
	if not (is_instance_valid(hc) and is_instance_valid(hc.data)):
		return
	if _combo_chain.size() == 1:
		_combo_package(hc)        # 首张：伪3D 封装(卡test 封装3D) → 滑入右侧托盘 + 淡入
		layout(true, false)
		_refresh_inactive()
	else:
		_fade_out_and_hide(hc)    # +1：原卡淡出隐藏（不进托盘、不旋转）
		_combo_spin_refresh()     #     首张伪3D快照刷新旋转冲量(卡test 旋转卡牌)
		layout(true, false)      # 剩余手牌快速重排（combo 态自动用 combo_layout_dur）
		_refresh_inactive()      # 重排后若 🔥 已耗尽 → 剩余未入链手牌压暗提示


## 首张伪3D 封装(复用 cardtest 封装3D)：活卡 CardRoot 渲进 front_viewport，卡背渲进 back_viewport，
## 正面/背面各一个 fake3d 显示层叠放(背面 y_rot 偏移 -180°，cull_back 自动剔除背对面)；从原卡位置滑入托盘并淡入。
func _combo_package(hc: HandCard) -> void:
	_combo_anchor = hc
	_combo_ring_count = 0          # 新链起始：仅锚点，无链环
	_combo_pending_boost = false   # 新链起始：无待替换提升
	var from_global := hc.global_position

	_combo_front_vp = _make_combo_viewport()
	_combo_back_vp = _make_combo_viewport()
	add_child(_combo_front_vp)
	add_child(_combo_back_vp)

	# 首张卡的 CardRoot 移入 front_vp（卡test 封装3D：活卡渲进视口），居中、原生 scale=1
	var card := hc.card
	if is_instance_valid(card):
		card.reparent(_combo_front_vp)
		card.position = _combo_front_vp.size * 0.5
		card.scale = Vector2.ONE
		card.visible = true

	# 卡背设计渲染进 back_vp
	_combo_card_back = _build_combo_card_back()
	if is_instance_valid(_combo_card_back):
		_combo_card_back.visible = true
		_combo_back_vp.add_child(_combo_card_back)   # 新建节点无父级，不能 reparent
		_combo_card_back.position = _combo_back_vp.size * 0.5
		_combo_card_back.scale = Vector2.ONE

	# 两个显示层：分别采样各自视口纹理；背面 z 低于正面（cull_back 决定每时刻哪面可见）
	var disp_size := Vector2(COMBO_CARD_W, COMBO_CARD_H) * combo_snap_scale
	var center_local := -disp_size * 0.5
	_combo_back_disp = TextureRect.new()
	_combo_back_disp.name = "ComboBackDisp"
	_combo_back_disp.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_combo_back_disp.size = disp_size
	_combo_back_disp.material = _make_combo_fake3d_material()
	_combo_back_disp.texture = _combo_back_vp.get_texture()
	_combo_back_disp.z_index = 1
	_combo_back_disp.mouse_filter = Control.MOUSE_FILTER_IGNORE   # 背面不拦截鼠标，交给正面层
	_combo_tray.add_child(_combo_back_disp)

	_combo_front_disp = TextureRect.new()
	_combo_front_disp.name = "ComboFrontDisp"
	_combo_front_disp.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	_combo_front_disp.size = disp_size
	_combo_front_disp.material = _make_combo_fake3d_material()
	_combo_front_disp.texture = _combo_front_vp.get_texture()
	_combo_front_disp.z_index = 2
	# 鼠标跟随倾斜：正面层拦截鼠标（复用 cardtest 的 pointer_inside 机制），使伪3D 倾斜可交互。
	_combo_front_disp.mouse_filter = Control.MOUSE_FILTER_STOP
	_combo_front_disp.mouse_entered.connect(_on_combo_display_entered)
	_combo_front_disp.mouse_exited.connect(_on_combo_display_exited)
	_combo_tray.add_child(_combo_front_disp)

	# 快照右侧词条解释面板：锚点卡进入托盘即构建（卡面【...】关键词 → KeywordGlossary 取词条，自上而下堆叠）。
	_build_combo_glossary(hc)

	# 滑入：从原卡手牌位置 → 托盘中心；并淡入（正面/背面同帧）
	var from_local := _combo_tray.to_local(from_global)
	_combo_front_disp.position = from_local
	_combo_back_disp.position = from_local
	_combo_front_disp.modulate.a = 0.0
	_combo_back_disp.modulate.a = 0.0
	var tw := create_tween().set_parallel(true)
	tw.tween_property(_combo_front_disp, "position", center_local, combo_move_dur).set_ease(Tween.EASE_OUT)
	tw.tween_property(_combo_back_disp, "position", center_local, combo_move_dur).set_ease(Tween.EASE_OUT)
	tw.tween_property(_combo_front_disp, "modulate:a", 1.0, combo_fade_dur)
	tw.tween_property(_combo_back_disp, "modulate:a", 1.0, combo_fade_dur)

	# 原 HandCard 外壳（子卡已移出）隐藏；首张不自动旋转，等 +1 触发
	hc.visible = false
	_spin_theta = 0.0
	_spin_omega = 0.0
	_spin_state = _ComboSpin.IDLE
	_combo_pointer_inside = false
	_combo_cur_x = combo_tilt_base   # 常驻微倾斜：封装瞬间即从该角度插值，保证 3D 立即可见
	_combo_cur_y = 0.0


## 构建快照右侧词条解释面板（首张锚点卡进入托盘时调用）。
## 词条来源 = 锚点卡卡面效果文字 card_effect（含【...】标签）；面板尺寸对齐快照显示层。
## 重建前先销毁上一轮可能残留/待释放的面板，避免词条串台。
func _build_combo_glossary(hc: HandCard) -> void:
	if not (is_instance_valid(hc) and is_instance_valid(hc.card)):
		return
	if is_instance_valid(_combo_glossary):
		_combo_glossary.queue_free()
		_combo_glossary = null
	_combo_glossary = COMBO_GLOSSARY_SCRIPT.new()
	_combo_glossary.name = "ComboGlossary"
	_combo_tray.add_child(_combo_glossary)
	var snap_w := COMBO_CARD_W * combo_snap_scale
	var snap_h := COMBO_CARD_H * combo_snap_scale
	# 强制放快照右侧（用户要求无视右缘溢出风险）
	_combo_glossary.build_from_data(hc.data, snap_w, snap_h, false)


## 销毁快照右侧词条解释面板（取消/打出 → _combo_unpackage 时调用）。
func _clear_combo_glossary() -> void:
	if is_instance_valid(_combo_glossary):
		_combo_glossary.queue_free()
		_combo_glossary = null


## 撤销伪3D 封装：把首张 CardRoot 移回 HandCard（否则 orphan 不被 free），释放视口/显示层/卡背，复位旋转态。
func _combo_unpackage() -> void:
	# 记录快照卡中心（全局）：供取消飞回手牌 / 打出飞弃牌都从此原位起步，消除「消失又闪现」。
	var snap := Vector2.ZERO
	if is_instance_valid(_combo_front_disp):
		snap = _combo_front_disp.global_position + _combo_front_disp.size * 0.5
	_combo_snap_center_global = snap
	if is_instance_valid(_combo_anchor) and is_instance_valid(_combo_anchor.card) \
			and is_instance_valid(_combo_front_vp) and _combo_anchor.card.get_parent() == _combo_front_vp:
		var card := _combo_anchor.card
		card.reparent(_combo_anchor)
		card.position = Vector2.ZERO
		card.scale = Vector2.ONE
		card.visible = true
	if is_instance_valid(_combo_front_disp):
		_combo_front_disp.material = null
		_combo_front_disp.texture = null
		_combo_front_disp.visible = false
		_combo_front_disp.queue_free()
	if is_instance_valid(_combo_back_disp):
		_combo_back_disp.material = null
		_combo_back_disp.texture = null
		_combo_back_disp.visible = false
		_combo_back_disp.queue_free()
	if is_instance_valid(_combo_front_vp):
		_combo_front_vp.queue_free()
	if is_instance_valid(_combo_back_vp):
		_combo_back_vp.queue_free()
	if is_instance_valid(_combo_card_back):
		_combo_card_back.queue_free()
	_combo_anchor = null
	_combo_front_vp = null
	_combo_back_vp = null
	_combo_front_disp = null
	_combo_back_disp = null
	_combo_card_back = null
	_spin_state = _ComboSpin.IDLE
	_spin_theta = 0.0
	_spin_omega = 0.0
	_settle_target = 0.0
	_combo_pointer_inside = false
	_combo_cur_x = 0.0
	_combo_cur_y = 0.0
	_clear_combo_glossary()   # 取消/打出 → 销毁快照右侧词条面板（防止残留串台）


## 快照卡中心（全局）：优先用 unpackage 时记录的伪3D 显示层中心；兜底用托盘原点。
func _last_combo_tray_center_global() -> Vector2:
	if _combo_snap_center_global != Vector2.ZERO:
		return _combo_snap_center_global
	if is_instance_valid(_combo_tray):
		return _combo_tray.global_position
	return Vector2.ZERO

## 离屏渲染目标（透明背景，持续刷新以支持翻面/文字变化）。视口取卡 2x 边长 → 卡在视口内保持原生 scale=1、只占中心 50%。
func _make_combo_viewport() -> SubViewport:
	var vp := SubViewport.new()
	vp.size = Vector2(COMBO_CARD_W * COMBO_VP_SCALE, COMBO_CARD_H * COMBO_VP_SCALE)
	vp.transparent_bg = true
	vp.disable_3d = true
	vp.handle_input_locally = false
	vp.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	return vp


## fake3d 着色器材质（纯透视形变，不含光效/阴影/抠色）。纹理由 TextureRect.texture 提供。
func _make_combo_fake3d_material() -> ShaderMaterial:
	var mat := ShaderMaterial.new()
	mat.shader = FAKE3D_SHADER
	mat.set_shader_parameter("fov", 70.0)   # 更小=透视形变越夸张（90→70：明显加强）
	mat.set_shader_parameter("cull_back", true)
	mat.set_shader_parameter("inset", 0.0)
	mat.set_shader_parameter("x_rot", 0.0)
	mat.set_shader_parameter("y_rot", 0.0)
	return mat


## 卡背：复用美术贴图（res://assets/cards/card_back.png，源 UI_Card 442x638），尺寸对齐正面卡牌。
const DEBUG_BACK_WHITE: bool = false
func _build_combo_card_back() -> Node2D:
	var root := Node2D.new()
	root.name = "CardBack"
	if DEBUG_BACK_WHITE:
		var ph := ColorRect.new()
		ph.size = Vector2(COMBO_CARD_W, COMBO_CARD_H)
		ph.position = -Vector2(COMBO_CARD_W, COMBO_CARD_H) * 0.5
		ph.color = Color.WHITE
		root.add_child(ph)
		return root
	var tex := load("res://assets/cards/card_back.png") as Texture2D
	if tex == null:
		push_warning("HandManager: 卡背贴图未导入(路径 res://assets/cards/card_back.png)，使用占位色块")
		var ph := ColorRect.new()
		ph.size = Vector2(COMBO_CARD_W, COMBO_CARD_H)
		ph.position = -Vector2(COMBO_CARD_W, COMBO_CARD_H) * 0.5
		ph.color = Color(0.12, 0.13, 0.22)
		root.add_child(ph)
		return root
	var tr := TextureRect.new()
	tr.texture = tex
	tr.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	tr.size = Vector2(COMBO_CARD_W, COMBO_CARD_H)
	tr.position = -Vector2(COMBO_CARD_W, COMBO_CARD_H) * 0.5
	root.add_child(tr)
	return root


## 原卡迅速淡出并隐藏（淡出时长 combo_fade_dur，需求指定 0.05）。用于 +1 的卡牌。
func _fade_out_and_hide(hc: HandCard) -> void:
	_kill_card_tween(hc)
	var tw := create_tween()
	tw.tween_property(hc, "modulate:a", 0.0, combo_fade_dur)
	tw.tween_callback(func(): if is_instance_valid(hc): hc.visible = false)
	_card_tweens[hc] = [tw]


## 每次 +1 刷新首张伪3D快照的旋转冲量：重置为满初速度、从当前 theta 续转（不等待上次转完 → 连击持续转动）。
## 复用 cardtest 旋转卡牌按钮：仅注入 omega，theta 连续不归零 → 旋转「刷新续转」而非重新从 0 开始。
func _combo_spin_refresh() -> void:
	if _combo_anchor == null:
		return
	_spin_omega = combo_spin_omega
	_spin_state = _ComboSpin.SPINNING
	# 旋转「输入/开始」瞬间触发白光斜扫（与边框流光错开播放）：旋转刚开始、卡牌高速转动时即可看到弧形白波从左下扫到右上。
	# 白膜在 SubViewport 内实时渲染 → 旋转过程中始终可见。每次 +1 刷新旋转都重播。
	if is_instance_valid(_combo_anchor) and is_instance_valid(_combo_anchor.card):
		_combo_anchor.card.play_card_flash()


## 旋转快照刷新：按锚点当前点数重建全部原子效果，Combo 不再改变单次效果数值。
## front_viewport 以 UPDATE_ALWAYS 实时渲染该 CardRoot，改其文字即实时刷新快照纹理；
## 于「正面不可见」的背面相位替换 → 正面转回时无缝显示新值（不卡壳、不闪烁）。
func _apply_combo_boost() -> void:
	if not (is_instance_valid(_combo_anchor) and is_instance_valid(_combo_anchor.card) and is_instance_valid(_combo_anchor.data)):
		return
	var bonus: int = _morale_bonus_for(_combo_anchor.owner_group)
	_combo_anchor.card.set_effect_text(_combo_anchor.data.effect_text(_combo_anchor.base_number, bonus))


## 每帧积分首张伪3D快照旋转（复用 cardtest 旋转物理）：SPINNING 段角速度指数衰减；跌破阈值切弹簧回正(SETTLING)；
## 阈值判定仅 SPINNING 一处——进入 SETTLING 后 match 不再回头判阈值，回弹途中角速度即便过冲超阈值也绝不「续杯」。
## SETTLING 用进入时锁定的 _settle_target（只算一次），整段回正不再重算目标，避免无限回弹。
func _combo_spin_integrate(delta: float) -> void:
	if _combo_anchor == null:
		return
	match _spin_state:
		_ComboSpin.SPINNING:
			_spin_omega *= exp(-combo_spin_drag * delta)
			_spin_theta += _spin_omega * delta
			# 连锁数值升级：旋转至背面相位（正面不可见，floor(theta/PI) 为奇数）时丝滑替换快照文字。
			if _combo_pending_boost and int(floorf(_spin_theta / PI)) % 2 == 1:
				_apply_combo_boost()
				_combo_pending_boost = false
			if _spin_omega < combo_omega_threshold:
				# 锁定回正目标：取运动前方最近的「正面」，只算这一次
				var t: float = roundf(_spin_theta / TAU) * TAU
				if t < _spin_theta:
					t += TAU
				_settle_target = t
				_spin_state = _ComboSpin.SETTLING
		_ComboSpin.SETTLING:
			var accel: float = (_settle_target - _spin_theta) * combo_settle_stiffness
			_spin_omega += accel * delta
			_spin_omega *= exp(-combo_settle_damping * delta)
			_spin_theta += _spin_omega * delta
			if absf(_settle_target - _spin_theta) < 0.002 and absf(_spin_omega) < 0.05:
				# 安全网：若弱旋转未及背面仍有待替换提升，落定时强制应用，保证卡面数值正确（正常高 omega 下不会走到此分支）。
				if _combo_pending_boost:
					_apply_combo_boost()
					_combo_pending_boost = false
				_spin_theta = _settle_target
				_spin_state = _ComboSpin.IDLE


## 把翻面角 + 倾斜写入伪3D 显示层（仅首张快照）：正面 y_rot = 倾斜 + flip_deg；背面 y_rot = 倾斜 + flip_deg - 180（Demo 卡背技巧），
## cull_back 决定哪面可见。关键：必须同时写 x_rot（鼠标跟随倾斜 + 常驻微倾斜）——否则卡牌始终平视、透视消失，看起来像丢了伪3D。
func _combo_apply_spin_to_display(delta: float) -> void:
	if not (is_instance_valid(_combo_front_disp) and _combo_front_disp.visible):
		return
	# 鼠标跟随倾斜目标（复用 cardtest）：悬停显示层时按局部坐标映射 ±combo_tilt_max；否则回落到常驻微倾斜。
	var tx: float = combo_tilt_base
	var ty: float = 0.0
	if _combo_pointer_inside:
		var lp := _combo_front_disp.get_local_mouse_position()
		var nx := clampf(remap(lp.x, 0.0, _combo_front_disp.size.x, 0.0, 1.0), 0.0, 1.0)
		var ny := clampf(remap(lp.y, 0.0, _combo_front_disp.size.y, 1.0, 0.0), 0.0, 1.0)
		tx = combo_tilt_base + lerpf(-combo_tilt_max, combo_tilt_max, nx)
		ty = lerpf(-combo_tilt_max, combo_tilt_max, ny)
	# 倾斜插值更脆（响应更快）
	var k := minf(1.0, delta * 30.0)
	_combo_cur_x = lerpf(_combo_cur_x, tx, k)
	_combo_cur_y = lerpf(_combo_cur_y, ty, k)
	var flip_deg := rad_to_deg(_spin_theta)
	var fmat := _combo_front_disp.material as ShaderMaterial
	var bmat := _combo_back_disp.material as ShaderMaterial
	if fmat != null:
		fmat.set_shader_parameter("x_rot", _combo_cur_x)
		fmat.set_shader_parameter("y_rot", _combo_cur_y + flip_deg)
	if bmat != null:
		bmat.set_shader_parameter("x_rot", _combo_cur_x)
		bmat.set_shader_parameter("y_rot", _combo_cur_y + flip_deg - 180.0)


## 伪3D 显示层鼠标进入/离开：驱动跟随倾斜（复用 cardtest 的 pointer_inside 机制）。
func _on_combo_display_entered() -> void:
	_combo_pointer_inside = true

func _on_combo_display_exited() -> void:
	_combo_pointer_inside = false


## 清空组合视觉：撤销伪3D 封装(首张卡移回 HandCard) + 释放所有渲染资源 + 复位旋转态。取消选靶 / 打出后调用。
func _clear_combo_visuals() -> void:
	_combo_unpackage()


## 所有在手牌复位到「待选」基准态（取消 / 打出后调用）。含缩放复位，避免抬起态的 hover_scale 残留。
func _restore_all() -> void:
	for c in _cards:
		if not (is_instance_valid(c) and c.state == HandCard.STATE_HAND):
			continue
		_lower_card(c)


## 把单卡 tween 到目标位置（杀掉旧 tween 避免抢 position），并复位 z 层级。
func _shift_card_to(c: HandCard, target: Vector2) -> void:
	_kill_card_tween(c)
	var tw := create_tween().set_parallel(true)
	tw.tween_property(c, "position", target, 0.2)
	_card_tweens[c] = [tw]
	c.z_index = c.base_z


## 取代旧版从 effects_text 正则解析；单标签模型下数据即唯一真相来源，无需归一化（同排/异排为合法标签）。

## 目标规则直接读取 CardEffect，不从卡面文字反向解析。

## 该角色当前士气加成；CardEffect 仅将其用于伤害效果。
## 取 BattleRoot.morale_bonus_for_role（= 士气层数 × MORALE_PER_LAYER），找不到角色/战斗根时返回 0。
func _morale_bonus_for(owner_group: String) -> int:
	var br := _get_battle_root()
	if is_instance_valid(br):
		return br.morale_bonus_for_role(owner_group)
	return 0

## 某角色士气变化时实时刷新其手牌上「攻击牌」的关键数值显示（基础+士气，或连锁提升后）。
## 由 BattleRoot._on_unit_buffs_changed 在该单位 buff 变化(含士气增减)时调用。
## 仅刷新 owner_group 匹配且在手牌的卡；连锁中的锚点按当前「是否已应用提升」同步刷新，避免与旋转替换冲突。
func refresh_morale_text_for_role(role: String) -> void:
	for c in _cards:
		if is_instance_valid(c) and c.state == HandCard.STATE_HAND and c.owner_group == role and is_instance_valid(c.card) and is_instance_valid(c.data):
			c.card.set_effect_text(_effect_text(c))

## 设计约定（2026-09-27 明确）：卡牌恒可打出，不在手牌区整卡灰掉/禁选。
## 是否「有有效目标」的反馈交由选靶黑幕（BattleRoot.set_target_dim_by_range）按攻击范围
## 对非目标单位盖黑幕剪影来体现；玩家点选后若无候选则效果 no-op（见 BattleRoot.apply_card_effect 兜底）。
## （旧 _card_playable 整卡灰掉逻辑已移除。）

## 只有存在选中目标的原子效果才显示瞄准线；自身/群体效果各自索敌。
func _needs_manual_target(hc: HandCard) -> bool:
	return is_instance_valid(hc) and is_instance_valid(hc.data) and hc.data.needs_selection()


## 自动结算占位：执行/引导卡(及全场/自身蓝卡)选中后无需玩家点选，立即按首候选打出。

## 取得 BattleRoot（经 group，避免硬编码路径），用于触发/清除选靶黑幕。
func _get_battle_root() -> BattleRoot:
	return get_tree().get_first_node_in_group("battle_root") as BattleRoot


## 供 BattleRoot 在「执行卡挂起效果演出」期间锁定/解锁出牌输入（与玩家卡演出共用 _cutscene_playing 闸门）。
func set_cutscene_lock(b: bool) -> void:
	_cutscene_playing = b


## 锁定期禁止选牌/加链/悬停检视；置位时同步刷新压暗，给出明确「手牌锁定」视觉。
func set_hand_locked(b: bool) -> void:
	_hand_locked = b
	_refresh_inactive()


func _native_width() -> float:
	for c in _cards:
		if is_instance_valid(c):
			return c.get_native_width()
	return 256.0
