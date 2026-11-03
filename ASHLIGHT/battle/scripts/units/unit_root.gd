class_name UnitRoot
extends Node2D

## 单位部件根节点（Step 1 · 6a 核心可见部件）。
## 五态美术状态机 + 垂直居中 + 前后排渐变遮罩 + 悬停描边。美术按 res://assets/units/{faction}/{id}/{id}_{State}.png 加载（按阵营分类存放）。

# --- 可调参数（编辑器直接改，F6 即时生效）---
@export var unit_id: String = "Zhouzhou"
@export var mastery: int = 1                  # 精通：被动放大系数（周周 §2，默认1；装备/成长可提升）
@export var is_back_row: bool = false :
	set(value):
		is_back_row = value
		_refresh_materials()
@export var back_row_darkness: float = 0.85 :
	set(value):
		back_row_darkness = value
		if is_instance_valid(_back_row_material) and _back_row_material.shader != null:
			_back_row_material.set_shader_parameter("mask_darkness", value)
@export var back_row_invert: bool = false :
	set(value):
		back_row_invert = value
		if is_instance_valid(_back_row_material) and _back_row_material.shader != null:
			_back_row_material.set_shader_parameter("invert_gradient", value)
## 后排遮罩渐变起始高度(UV.y)：越小覆盖越靠头部、面积越大。0.5=仅下半身，0.4=略过腰线往上，0.2=覆盖到接近头顶，可在编辑器滑到满意高度(F6 即时生效)。
@export var back_row_mask_top: float = 0.2 :
	set(value):
		back_row_mask_top = value
		if is_instance_valid(_back_row_material) and _back_row_material.shader != null:
			_back_row_material.set_shader_parameter("mask_top", value)
@export var visual_offset_y: float = 0.0 :
	set(value):
		visual_offset_y = value
		_apply_layout()
@export var hover_outline_color: Color = Color(1.0, 1.0, 1.0, 1.0) :
	set(value):
		hover_outline_color = value
		if is_instance_valid(_outline_material) and _outline_material.shader != null:
			_outline_material.set_shader_parameter("outline_color", value)
@export var hover_outline_thickness: float = 3.0 :
	set(value):
		hover_outline_thickness = value
		if is_instance_valid(_outline_material) and _outline_material.shader != null:
			_outline_material.set_shader_parameter("outline_thickness", value)

# --- 激活体积（固定框体，不再依赖美术尺寸）---
# 碰撞体尺寸与位置完全由此两参数驱动，编辑器改值 F6 即时生效（无可视化，仅功能）。
# 尺寸 activation_size 默认 160×330；中心 activation_center 默认 (0,-125)。
# x=0 水平居中于单位组锚点；y=-125 使框体顶边固定在局部 y=-290（美术上方），整体相对原本贴美术底边(160)上移 300px 后再向下延长 30px（底边局部 y=40）。
@export var activation_size: Vector2 = Vector2(160.0, 330.0) :
	set(value):
		activation_size = value
		_position_activation()
@export var activation_center: Vector2 = Vector2(0.0, -125.0) :
	set(value):
		activation_center = value
		_position_activation()

# --- 血条数据（6b，转发给 UnitHp 组件；F6 可直接调）---
@export var max_hp: float = 10.0
@export var current_hp: float = 10.0
@export var shield: float = 0.0
## 血条/Buff框体整体下移；不影响美术、判定框或Buff获得提示条。
@export var unit_hud_shift_y: float = 15.0 :
	set(value):
		unit_hud_shift_y = value
		_apply_layout()
## 倒计时独立于血条位移；当前比上一版下移5px。
@export var countdown_offset_y: float = 33.0 :
	set(value):
		countdown_offset_y = value
		_apply_layout()
## 血条独立微调，避免带动Buff框体与提示条。
@export var hp_group_offset_y: float = 3.0 :
	set(value):
		hp_group_offset_y = value
		_apply_layout()
## 血条顶边相对单位组锚点(UnitRoot.position)的向下偏移。
## 锚点即「血条靠上居中」：默认 0.0 时血条顶边中心正好落在锚点上；
## 设为正数可在锚点与血条之间留出间隙。该偏移不依赖美术高度，故敌我单位组同 Y 时血条严格对齐。
@export var hp_offset_y: float = -5.0 :
	set(value):
		hp_offset_y = value
		_apply_layout()
@export var buff_offset_y: float = 26.0 :
	set(value):
		buff_offset_y = value
		_apply_layout()
## 意图组(IntentRoot)相对单位组锚点的垂直偏移（默认 -200，即美术上方；美术到位后排布由 IntentRoot 内部 export 微调）。
@export var intent_offset_y: float = -190.0 :
	set(value):
		intent_offset_y = value
		if is_instance_valid(_intent_component):
			_intent_component.set_anchor_position(Vector2(0.0, value))
## 单位阵营：player=玩家, enemy=敌人。意图组仅敌人(enemy)显示（转发给 IntentRoot.set_faction）。
@export var unit_faction: String = "player" :
	set(value):
		unit_faction = value
		if is_instance_valid(_intent_component):
			_intent_component.set_faction(value)

## 敌人下一次行动所需的等待格数。开局与行动后共用同一速度区间。
var enemy_speed_tier: String = "medium"
var ai_memory: Dictionary = {}

func enemy_wait_ticks() -> int:
	match enemy_speed_tier:
		"fast": return randi_range(4, 6)
		"slow": return randi_range(8, 10)
		"random": return randi_range(1, 10)
		_: return randi_range(6, 8)

# --- 经验与升级数据 ---
@export var current_level: int = 1
@export var xp_to_next: float = 2.0  # 当前等级升级所需经验（初始 2 点；生产环境应从数据表读取）

const UNITS_DIR: String = "res://assets/units/"
const OUTLINE_SHADER_PATH: String = "res://battle/scripts/units/outline.gdshader"
const BACK_MASK_SHADER_PATH: String = "res://battle/scripts/units/back_row_mask.gdshader"
## 保留作语义参考（攻击/前排单体），实际不再使用。
const DEFAULT_ENEMY_INTENT_TYPE: String = "att"
const DEFAULT_ENEMY_INTENT_TARGET: String = "front_monomer"
## 升级序列帧（LVup）资源目录与参数：作为单位组叠加特效，垂直居中于单位中心。
const LVUP_DIR: String = "res://assets/Unit UI/_lvup/"
const LVUP_FRAME_COUNT: int = 8
const LVUP_FPS: float = 8.0
## 血条/经验条纹理高度（与美术源 143×47 一致，铁规则下尺寸固定）。用于 Buff 框体排在血条下方时定位。
const BAR_HEIGHT: float = 47.0
## 美术底边与血条顶边之间的呼吸间隙（单位：px）。
const ART_BAR_GAP: float = 10.0
## 升级特效相对单位中心的垂直偏移（默认下移 50px，避免遮住角色本体）；编辑器可调。
@export var upgrade_fx_offset_y: float = 50.0 :
	set(value):
		upgrade_fx_offset_y = value
		if is_instance_valid(_upgrade_anim):
			_upgrade_anim.position = Vector2(0.0, value)
## 选靶黑幕淡入/淡出时长(s)。F6 即时生效，喜欢更慢/更快直接改这里。
@export var dim_fade_time: float = 0.18 :
	set(value):
		dim_fade_time = maxf(value, 0.0)

enum UnitArtState { IDLE, ATTACK, CAST, HIT, CHARGE }

@export_group("美术呼吸")
@export var breathing_enabled: bool = true
@export var breathing_period: float = 3.0
@export var breathing_height: float = 0.015
@export var breathing_width: float = 0.005
var _breath_root: Node2D
var _breath_phase: float = 0.0
var _breath_weight: float = 0.0
var _breath_foot_y: float = 0.0
var _breathing_paused: bool = false

@export var current_art_state: UnitArtState = UnitArtState.IDLE :
	set(value):
		current_art_state = value
		if is_instance_valid(_art_sprite):
			_load_state(value)

var _body: Node2D
var _art_sprite: Sprite2D
var _outline_material: ShaderMaterial
var _back_row_material: ShaderMaterial
var _hovering: bool = false
## 选靶黑幕状态：true=美术已变纯黑剪影（由 set_dimmed 驱动）。
var _dimmed: bool = false
## 死亡标记：die() 已触发一次，防止重复播放死亡演出（黑幕淡出期间重复掉血不再二次触发）。
var _dead: bool = false
## 选靶黑幕剪影叠加层：独立 Sprite2D，z 高于美术 Sprite，绘制于「前后排渐变遮罩」之上，避免被渐变材质覆盖（back_row 也能看到黑幕）。
var _dim_overlay: Sprite2D
var _dim_gradient_material: ShaderMaterial
## 黑幕淡入淡出 tween 引用，每次开关前先 kill，避免与本次动画抢 modulate.a。
var _dim_tween: Tween
var _externally_highlighted: bool = false
var _activation_area: Area2D
var _hp_component: UnitHp
var _buff_component: UnitBuff
var _countdown_component: UnitCountdown
@export var intent_arrow_anchor_offset: Vector2 = Vector2(0.0, -125.0) :
	set(value):
		intent_arrow_anchor_offset = value
		if is_instance_valid(_intent_arrow_anchor):
			_intent_arrow_anchor.position = value
var _intent_arrow_anchor: Marker2D
var _intent_component: IntentRoot
var _xp_component: UnitXp
var _upgrade_anim: AnimatedSprite2D
var _event_bus: Node  # 运行期通过 /root/EventBus 取得，避免依赖编辑器全局符号表缓存

signal part_hovered(part_id: String)
## 取消关注(signal unhover)也携带 unit_id，便于联动方精确取消对应高亮（避免维护 last-hover 状态）。
signal part_unhovered(unit_id: String)
signal intent_changed(unit_id: String, type_id: String, value: String, target_faction: String, scope: String)
signal died(uid: String)
## buff 集合变化信号：转发自 BuffRoot.buffs_changed，携带 unit_id，供 BattleRoot 实时刷新手牌士气数值等。
signal buffs_changed(unit_id: String)

func _ready() -> void:
	_build_node_tree()
	_breath_phase = float(posmod(unit_id.hash(), 1000)) / 1000.0 * TAU
	if unit_id != "":
		set_unit_art(unit_id)
	# 监听全局升级参数：只负责「参数→播放」，与生产者（按钮/其他情境）完全解耦。
	# 用 /root/EventBus 取实例（而非裸全局名），避免编辑器缓存未刷新时静态校验报 “not declared”。
	_event_bus = get_node_or_null("/root/EventBus")
	if is_instance_valid(_event_bus):
		_event_bus.connect("upgrade_triggered", _on_upgrade_triggered)

func _exit_tree() -> void:
	if is_instance_valid(_event_bus) and _event_bus.is_connected("upgrade_triggered", _on_upgrade_triggered):
		_event_bus.disconnect("upgrade_triggered", _on_upgrade_triggered)

func _build_node_tree() -> void:
	_intent_arrow_anchor = Marker2D.new()
	_intent_arrow_anchor.name = "IntentArrowAnchor"
	_intent_arrow_anchor.position = intent_arrow_anchor_offset
	add_child(_intent_arrow_anchor)
	_body = Node2D.new(); _body.name = "BodyRoot"; _body.z_index = 0; add_child(_body)
	# 仅美术和剪影进入此容器；血条、Buff、提示与判定框均不参与拉伸。
	_breath_root = Node2D.new()
	_breath_root.name = "BreathingArt"
	_body.add_child(_breath_root)

	# 悬停描边：着色器沿角色剪影外缘绘白边（§4.4 美术级，非方块/非放大剪影）
	_outline_material = ShaderMaterial.new()
	_outline_material.shader = load(OUTLINE_SHADER_PATH) as Shader
	if _outline_material.shader != null:
		_outline_material.set_shader_parameter("outline_color", hover_outline_color)
		_outline_material.set_shader_parameter("outline_thickness", hover_outline_thickness)
	else:
		push_warning("UnitRoot: 描边 Shader 加载失败 %s，悬停描边将不显示" % OUTLINE_SHADER_PATH)

	# 后排渐变遮罩：仅作用于美术不透明像素（贴合剪影，非方形覆盖），经 next_pass 与描边共存
	_back_row_material = ShaderMaterial.new()
	_back_row_material.shader = load(BACK_MASK_SHADER_PATH) as Shader
	if _back_row_material.shader != null:
		_back_row_material.set_shader_parameter("mask_darkness", back_row_darkness)
		_back_row_material.set_shader_parameter("invert_gradient", back_row_invert)
		_back_row_material.set_shader_parameter("mask_top", back_row_mask_top)
	else:
		push_warning("UnitRoot: 后排遮罩 Shader 加载失败 %s" % BACK_MASK_SHADER_PATH)

	_art_sprite = Sprite2D.new(); _art_sprite.name = "ArtSprite"; _art_sprite.centered = true; _breath_root.add_child(_art_sprite)
	# 选靶黑幕剪影叠加层：与美术同纹理、同位置，z 更高，绘制于渐变遮罩之上（修复 back_row 渐变吃黑幕问题）。
	_dim_overlay = Sprite2D.new(); _dim_overlay.name = "DimOverlay"; _dim_overlay.centered = true
	_dim_gradient_material = ShaderMaterial.new()
	_dim_gradient_material.shader = preload("res://battle/scripts/units/selection_dim.gdshader")
	_dim_overlay.material = _dim_gradient_material
	_dim_overlay.z_index = 10; _dim_overlay.modulate = Color(0.0, 0.0, 0.0, 0.0); _dim_overlay.visible = false
	_breath_root.add_child(_dim_overlay)

	# 升级序列帧特效（LVup）：仅非敌方(player)单位启用。enemy 阵营关闭升级组 → 不创建 UpgradeFX。
	if unit_faction != "enemy":
		_upgrade_anim = AnimatedSprite2D.new(); _upgrade_anim.name = "UpgradeFX"
		_upgrade_anim.centered = true; _upgrade_anim.position = Vector2(0.0, upgrade_fx_offset_y); _upgrade_anim.z_index = 5
		_body.add_child(_upgrade_anim)
		_upgrade_anim.sprite_frames = _build_lvup_frames()
		_upgrade_anim.visible = false
		_upgrade_anim.animation_finished.connect(_on_upgrade_finished)
	else:
		_upgrade_anim = null

	# 血条组件（6b）：HpRoot 即 UnitHp 实例，自带三层血条+护盾，位于美术下方
	var hp := UnitHp.new(); hp.name = "HpRoot"; hp.z_index = 20; add_child(hp)
	hp.setup_stats(max_hp, current_hp, shield)
	_hp_component = hp
	_countdown_component = UnitCountdown.new()
	_countdown_component.name = "CountdownRoot"
	_countdown_component.z_index = 20
	add_child(_countdown_component)

	# 经验条组件（UnitXp）：仅非敌方(player)单位启用。enemy 阵营关闭升级组 → 不创建经验条。
	if unit_faction != "enemy":
		var xp := UnitXp.new(); xp.name = "XpRoot"; xp.z_index = 19; add_child(xp)
		xp.setup_stats(xp_to_next, 0.0, current_level)
		_xp_component = xp
	else:
		_xp_component = null
	# Buff 框体（6c）：UnitBuff 自带图标排布+层数角标+益害着色+滑入提示（含内部 Toast）
	var buff_root := UnitBuff.new(); buff_root.name = "BuffRoot"; buff_root.z_index = 20; add_child(buff_root)
	_buff_component = buff_root
	_buff_component.buffs_changed.connect(_on_buffs_changed)
	var intent_root := IntentRoot.new(); intent_root.name = "IntentRoot"; intent_root.z_index = 20; add_child(intent_root)
	intent_root.set_anchor_position(Vector2(0.0, intent_offset_y))
	intent_root.set_faction(unit_faction)   # 阵营转发：决定意图组是否显示
	_intent_component = intent_root
	# 敌方阵营：常态下意图即打开（显示默认攻击意图），无需手动触发「显示意图」按钮。

	# 激活体积：碰撞体（尺寸/位置不依赖美术），水平居中于单位组、垂直靠齐美术下方。
	# ActivationArea 才是真正接收悬停事件的碰撞体（无红色可视化框，仅功能）。
	_activation_area = Area2D.new(); _activation_area.name = "ActivationArea"; _activation_area.input_pickable = true
	add_child(_activation_area)
	var act_shape := CollisionShape2D.new(); act_shape.name = "ActivationShape"
	act_shape.shape = RectangleShape2D.new(); _activation_area.add_child(act_shape)
	_activation_area.mouse_entered.connect(_on_art_mouse_entered)
	_activation_area.mouse_exited.connect(_on_art_mouse_exited)
	_position_activation(); _apply_layout(); _refresh_materials()

## 美术目录 = UNITS_DIR + 阵营 + "/" + unit_id + "/"。美术按阵营分类存放（player/enemy）。
func _art_dir() -> String:
	return UNITS_DIR + unit_faction + "/" + unit_id + "/"

func set_unit_art(id: String) -> void:
	unit_id = id
	var dir := _art_dir()
	if not DirAccess.dir_exists_absolute(dir):
		push_warning("UnitRoot: 未找到单位美术目录 %s，使用粉色占位" % dir)
		_show_pink_placeholder(); return
	_load_state(current_art_state)
	_apply_layout()  # 美术尺寸确定后重定位血条到美术下方

func set_art_state(state: UnitArtState) -> void:
	current_art_state = state
	if state not in [UnitArtState.IDLE, UnitArtState.CHARGE]:
		_reset_breathing_transform()

func set_breathing_paused(paused: bool) -> void:
	_breathing_paused = paused
	if paused:
		_reset_breathing_transform()

func _reset_breathing_transform() -> void:
	_breath_weight = 0.0
	if is_instance_valid(_breath_root):
		_breath_root.scale = Vector2.ONE
		_breath_root.position = Vector2.ZERO

func _process(delta: float) -> void:
	if not breathing_enabled or _breathing_paused or _dead or current_art_state not in [UnitArtState.IDLE, UnitArtState.CHARGE]:
		_reset_breathing_transform()
		return
	_breath_phase = fposmod(_breath_phase + delta * TAU / maxf(breathing_period, 0.1), TAU)
	_breath_weight = move_toward(_breath_weight, 1.0, delta / 0.2)
	var wave: float = (1.0 - cos(_breath_phase)) * 0.5 * _breath_weight
	_breath_root.scale = Vector2(1.0 - breathing_width * wave, 1.0 + breathing_height * wave)
	# 补偿拉伸后的底边位移，保持脚底中心固定。
	_breath_root.position = Vector2(0.0, _breath_foot_y * (1.0 - _breath_root.scale.y))

## 新增 buff（委托给 BuffRoot 组件）。tone: 0未定/1有益/2有害；display_name 取自数据表名称；description 取自数据表说明文字
func add_buff(buff_id: String, stack: int = 1, tone: int = 0, display_name: String = "", description: String = "") -> void:
	if is_instance_valid(_buff_component):
		_buff_component.add_buff(buff_id, stack, tone, display_name, description)

## 移除本单位某 buff（委托 BuffRoot）。供「攻击行为结算后清空士气层数」等一次性消费场景使用。
func remove_buff(buff_id: String) -> void:
	if is_instance_valid(_buff_component):
		_buff_component.remove_buff(buff_id)

## 设置最大生命并回满（数据驱动初始化用，如 MonsterDatabase.max_hp）；_ready 后调用也会同步刷新 HpRoot。
func set_max_hp(value: float) -> void:
	max_hp = value
	current_hp = value
	if is_instance_valid(_hp_component):
		_hp_component.setup_stats(max_hp, current_hp, get_shield())

## 受到 amount 点伤害（委托给 HpRoot，实时刷新血条）。
## 返回本次【实际扣除的血量】，护盾吸收部分不计入（供中毒/出血飘字显示真实掉血）。
func damage(amount: float) -> float:
	if is_instance_valid(_hp_component):
		return _hp_component.apply_damage(amount)
	return 0.0

## 获得 amount 点护盾（委托给 HpRoot，实时刷新护盾叠层）。
func gain_shield(amount: float) -> void:
	if is_instance_valid(_hp_component):
		var before: float = _hp_component.get_shield()
		_hp_component.apply_shield(amount)
		# 仅实际新增护盾时提示名称；护盾数值仍由伤害数字组件显示。
		if _hp_component.get_shield() > before and is_instance_valid(_buff_component):
			_buff_component.show_gain_toast("护盾", "shield")

## 当前护盾值（委托给 HpRoot，供【中毒】护盾 1:1 抵消层数结算）。
func get_shield() -> float:
	if is_instance_valid(_hp_component):
		return _hp_component.get_shield()
	return 0.0

## 消耗护盾（委托给 HpRoot）。护盾不够扣时按 0 收底，不报错。
func reduce_shield(amount: float) -> void:
	if is_instance_valid(_hp_component):
		_hp_component.reduce_shield(amount)

## 减少某 buff 的层数（最小 0；归零则移除该条目）。
## 供【出血】每次推进 -1 层 /【中毒】护盾抵消层数 使用。
func remove_buff_stacks(buff_id: String, amount: int) -> void:
	if is_instance_valid(_buff_component):
		_buff_component.remove_buff_stacks(buff_id, amount)

## 回复 amount 点血量（委托给 HpRoot，实时刷新血条）。供「回复血量」卡牌款式(原型 heal)使用。
func heal(amount: float) -> void:
	if is_instance_valid(_hp_component):
		_hp_component.apply_heal(amount)

## 当前血量（委托给 HpRoot，供 BattleRoot 在掉血后判断是否清零触发死亡）。
func get_current_hp() -> float:
	if is_instance_valid(_hp_component):
		return _hp_component.get_current_hp()
	return 0.0

## 单位美术中心的世界坐标（意图抛物线预警 / 卡牌虚线等取起点终点用）。
## 美术容器 _body 的 position 即美术中心相对单位锚点(UnitRoot.position)的偏移。
func get_intent_arrow_anchor_global() -> Vector2:
	if is_instance_valid(_intent_arrow_anchor):
		return _intent_arrow_anchor.global_position
	return to_global(intent_arrow_anchor_offset)

func get_art_center_global() -> Vector2:
	if is_instance_valid(_body):
		return global_position + _body.position
	return global_position

## 查询本单位某 buff 的当前层数（委托 BuffRoot，供士气等关键数值加成读取）。无该 buff 返回 0。
## 精通属性（周周 §2）：返回被动放大系数。默认 1。
func get_mastery() -> int:
	return mastery

func get_buff_stacks(buff_id: String) -> int:
	if is_instance_valid(_buff_component):
		return _buff_component.get_buff_stacks(buff_id)
	return 0

## 转发 BuffRoot 的 buff 变化信号（携带本 unit_id），供 BattleRoot 实时刷新手牌士气数值等。
func _on_buffs_changed() -> void:
	buffs_changed.emit(unit_id)

## 死亡演出：美术盖黑幕并保持，整组（含黑幕 / 血条 / 意图）在 death_fade_time 内逐渐淡出，
## 默认 0.2s 淡出（用户要求）；_dead 守卫防止重复触发。
func die(death_fade_time: float = 0.2) -> void:
	if _dead:
		return
	_dead = true
	_reset_breathing_transform()
	if is_instance_valid(_dim_gradient_material):
		_dim_gradient_material.set_shader_parameter("solid_black", true)
	# 立即使美术变纯黑剪影（盖黑幕、保持），不参与后续角色 / 选靶。
	if is_instance_valid(_dim_overlay):
		if is_instance_valid(_dim_tween):
			_dim_tween.kill()
		_dim_overlay.visible = true
		_dim_overlay.modulate = Color(0.0, 0.0, 0.0, 1.0)
	# 整组 0.2s 淡出（父节点 modulate.a 影响全部子节点，黑幕随之淡出至全透明）。
	var tw := create_tween()
	tw.tween_property(self, "modulate:a", 0.0, maxf(death_fade_time, 0.0))
	tw.tween_callback(func() -> void:
		died.emit(unit_id)
		queue_free())

## 持续伤害(DoT)结算飘字（出血/中毒 共用）：在单位美术上方生成紫色结算伤害飘字（标签=source_label + 数字），
## 与演出组2共用数字动画；仅使用结算伤害紫色款(C_TICK #C79BFF)。
## 由 BattleRoot._tick_bleed / _tick_poison 调用；amount = 本次实际造成的伤害值（被护盾抵消部分不计入）。
func spawn_tick_damage(source_label: String, amount: int, invalid_amount: int = 0) -> void:
	if amount <= 0 and invalid_amount <= 0:
		return
	var dmg := DamageNumber.new()
	dmg.name = "TickDamage"
	dmg.z_index = 47
	get_parent().add_child(dmg)  # 数字独立于单位死亡淡出，播放到自身结束。
	dmg.spawn({"type": "tick", "source": source_label, "value": amount, "invalid_amount": invalid_amount})
	# 与其他数字共用判定框随机锚点及弹起/停留/淡出时序。
	dmg.play_on_unit(self)

## 出血结算伤害数字：转发到通用 DoT 飘字（标签"出血"）。
func spawn_bleed_damage(amount: int) -> void:
	spawn_tick_damage("出血", amount)

func set_intent(type_id: String, value: String = "", target_faction: String = "", scope: String = "单体", value_base: String = "", display_text: String = "", card_color: int = -1, card_number: int = 0) -> void:
	if is_instance_valid(_intent_component):
		_intent_component.set_intent(type_id, value, target_faction, scope, value_base, display_text, card_color, card_number)
	intent_changed.emit(unit_id, type_id, value, target_faction, scope)

func get_intent() -> Dictionary:
	if is_instance_valid(_intent_component):
		return _intent_component.get_intent()
	return {}

## 逻辑与 _build_node_tree 敌方分支一致；玩家方调用为空操作（玩家无意图）。
func _load_state(state: UnitArtState) -> void:
	if _art_sprite == null:
		return
	var base := _art_dir()
	var path := base + unit_id + "_" + _state_suffix(state) + ".png"
	var tex: Texture2D = load(path) as Texture2D if ResourceLoader.exists(path) else null
	if tex == null and state != UnitArtState.IDLE:
		tex = load(base + unit_id + "_Idle.png") as Texture2D  # 缺态回退 IDLE
	if tex == null:
		_show_pink_placeholder(); return
	_art_sprite.texture = tex
	var image := tex.get_image()
	_breath_foot_y = tex.get_height() * 0.5
	if image != null and not image.is_empty():
		if image.is_compressed():
			image.decompress()
		var used: Rect2i = image.get_used_rect()
		if used.has_area():
			_breath_foot_y = float(used.end.y) - tex.get_height() * 0.5
			# 以可见美术高度计算渐变，避免不同贴图透明留白影响黑幕分布。
			if is_instance_valid(_dim_gradient_material):
				_update_dim_gradient_bounds(image)
	# 同步黑幕叠加层纹理与变换（保持剪影与当前美术态一致）
	if is_instance_valid(_dim_overlay):
		_dim_overlay.texture = tex
		_dim_overlay.position = _art_sprite.position
		_dim_overlay.scale = _art_sprite.scale
		_dim_overlay.flip_h = _art_sprite.flip_h

func _update_dim_gradient_bounds(image: Image) -> void:
	# 排除原图近透明噪点，让“顶部/60%高度”对应实际人物，而非透明画布。
	image.convert(Image.FORMAT_RGBA8)
	var pixels: PackedByteArray = image.get_data()
	var width: int = image.get_width()
	var height: int = image.get_height()
	var top: int = -1
	var bottom: int = height
	for y in height:
		for x in width:
			if pixels[(y * width + x) * 4 + 3] > 128:
				top = y
				break
		if top >= 0:
			break
	for y in range(height - 1, -1, -1):
		var found: bool = false
		for x in width:
			if pixels[(y * width + x) * 4 + 3] > 128:
				bottom = y + 1
				found = true
				break
		if found:
			break
	_dim_gradient_material.set_shader_parameter("art_top", float(maxi(0, top)) / height)
	_dim_gradient_material.set_shader_parameter("art_bottom", float(bottom) / height)

func _state_suffix(state: UnitArtState) -> String:
	match state:
		UnitArtState.IDLE: return "Idle"
		UnitArtState.ATTACK: return "Attack"
		UnitArtState.CAST: return "Cast"
		UnitArtState.HIT: return "Hit"
		UnitArtState.CHARGE: return "Charge"
	return "Idle"

func _show_pink_placeholder() -> void:
	if _art_sprite == null:
		return
	_art_sprite.texture = null
	if is_instance_valid(_dim_overlay):
		_dim_overlay.texture = null
		_dim_overlay.visible = false
	var ph := ColorRect.new()
	ph.color = Color(1.0, 0.4, 0.7, 1.0); ph.size = Vector2(120.0, 160.0)
	ph.position = Vector2(-60.0, -80.0); ph.mouse_filter = Control.MOUSE_FILTER_IGNORE
	_body.add_child(ph)

## 定位激活碰撞体：水平居中于单位组锚点，垂直由 activation_center 控制（默认贴美术底边）。
## 碰撞体尺寸/位置均由 export 参数驱动，便于在编辑器即时调整（F6 生效）。
func _position_activation() -> void:
	if is_instance_valid(_activation_area):
		_activation_area.position = activation_center
		var sh := _activation_area.get_child(0) as CollisionShape2D
		if sh != null and sh.shape is RectangleShape2D:
			(sh.shape as RectangleShape2D).size = activation_size

func _current_art_size() -> Vector2:
	if _art_sprite != null and _art_sprite.texture != null:
		return _art_sprite.texture.get_size()
	return Vector2(120.0, 160.0)

func _on_art_mouse_entered() -> void:
	_hovering = true; _refresh_materials(); part_hovered.emit(unit_id)

func _on_art_mouse_exited() -> void:
	_hovering = false; _refresh_materials(); part_unhovered.emit(unit_id)

## 权威悬停命中测试：激活碰撞体矩形（中心 = 世界 position + activation_center，尺寸 = activation_size）。
## 由 BattleRoot._process 调用，取代易漏发的 mouse_exited（快速移动残留高亮问题）。
func is_point_inside(global_pos: Vector2) -> bool:
	var center := global_position + activation_center
	var half := activation_size * 0.5
	return Rect2(center - half, activation_size).has_point(global_pos)

func set_external_highlight(on: bool) -> void:
	_externally_highlighted = on
	_refresh_materials()

## 选靶命中反馈：短暂点亮本单位组描边（默认 0.5s 后自动熄灭）。
## 测试阶段用于证明「按标签解析出的目标单位」被正确选中（伤害/增益未填，暂不实装）。
func flash(duration: float = 0.5) -> void:
	set_external_highlight(true)
	await get_tree().create_timer(duration).timeout
	set_external_highlight(false)

## 选靶黑幕：顶部10%高度保留20%深灰遮罩，再渐变到75%高度完全覆盖#222222；沿用淡入/淡出时长。
## 实现为独立叠加 Sprite（z 高于美术 Sprite 与其渐变遮罩 next_pass），绘制于「前后排渐变遮罩」之上，后排单位也能正常显示黑幕。
## 仅覆盖美术，不影响血条/意图条（后者位于更高 z 的同级节点）。
func set_dimmed(on: bool) -> void:
	if _dead:
		return
	_dimmed = on
	if not is_instance_valid(_dim_overlay):
		return
	# 杀掉可能在跑的淡入/淡出，防止与本次动画争抢 modulate.a 导致闪烁。
	if is_instance_valid(_dim_tween):
		_dim_tween.kill()
	_dim_tween = create_tween()
	if on:
		_dim_overlay.visible = true
		_dim_overlay.modulate = Color(0.0, 0.0, 0.0, 0.0)
		_dim_tween.tween_property(_dim_overlay, "modulate:a", 1.0, dim_fade_time)
	else:
		# 从当前 alpha 淡出到 0，结束时再隐藏节点（避免重新切入时的亮度跳变）。
		_dim_tween.tween_property(_dim_overlay, "modulate:a", 0.0, dim_fade_time)
		_dim_tween.tween_callback(func() -> void:
			if is_instance_valid(_dim_overlay) and not _dimmed:
				_dim_overlay.visible = false)

## 查询本单位是否处于黑幕剪影态（盖黑幕时不可作为卡牌目标）。
## 注意：淡出动画播放中 _dimmed 已置 false，本查询返回「当前是否应被视作黑幕」，
## 故以 _dim_overlay 是否仍可见为准（淡出末帧前仍视为不可选，避免动画期间误点）。
func is_dimmed() -> bool:
	if is_instance_valid(_dim_overlay) and _dim_overlay.visible:
		return true
	return _dimmed

# 调度 _art_sprite.material：悬停描边(_outline)与后排遮罩(_back_row)经 next_pass 串联可共存
func _refresh_materials() -> void:
	if not is_instance_valid(_art_sprite):
		return
	var mask_on := is_back_row and is_instance_valid(_back_row_material)
	var out_on := (_hovering or _externally_highlighted) and is_instance_valid(_outline_material)
	if is_instance_valid(_outline_material):
		_outline_material.next_pass = null
	if is_instance_valid(_back_row_material):
		_back_row_material.next_pass = null
	if out_on and mask_on:
		_outline_material.next_pass = _back_row_material
		_art_sprite.material = _outline_material
	elif out_on:
		_art_sprite.material = _outline_material
	elif mask_on:
		_art_sprite.material = _back_row_material
	else:
		_art_sprite.material = null

func _apply_layout() -> void:
	var art_h := _current_art_size().y
	# 锚点(UnitRoot.position) 即「血条靠上居中」：血条顶边中心落在锚点上（hp_offset_y 为锚点→血条顶的微调间隙）。
	# 关键：血条位置不再依赖美术高度 art_h，因此不同美术尺寸的单位组放到同一场景 Y 时，血条严格对齐（敌我同步）。
	var bar_top_y := visual_offset_y + hp_offset_y
	var hud_top_y: float = bar_top_y + unit_hud_shift_y
	if is_instance_valid(_hp_component):
		_hp_component.position = Vector2(0.0, hud_top_y + hp_group_offset_y)
	if is_instance_valid(_xp_component):
		_xp_component.position = Vector2(0.0, hud_top_y + hp_group_offset_y)
	if is_instance_valid(_countdown_component):
		_countdown_component.position = Vector2(0.0, bar_top_y + countdown_offset_y)
	if is_instance_valid(_body):
		# 美术整体置于血条上方：美术中心 = 锚点 - (art_h/2 + 间隙)，使美术底边悬于血条顶之上、互不重叠。
		_body.position = Vector2(0.0, bar_top_y - ART_BAR_GAP - art_h * 0.5 + 20.0)  # 美术下移20px，更靠近血条组
	if is_instance_valid(_buff_component):
		# Buff 框体排在血条正下方（BAR_HEIGHT 为整条高度，旧 30.0 半条近似已替换）。
		_buff_component.position = Vector2(0.0, hud_top_y + BAR_HEIGHT + buff_offset_y - 15.0)
		_buff_component.set_toast_layout_compensation(unit_hud_shift_y)

## 收到升级参数：仅当目标 id 与本单位匹配时才播放一次升级动画（无淡入淡出）。
## 与生产者解耦 —— 谁 emit 的无关紧要，本函数只认 unit_id 是否命中自己。
func _on_upgrade_triggered(id: String) -> void:
	if id != unit_id:
		return
	_play_upgrade_animation()

## 播放升级序列帧一次（直接 play，不循环、不淡入淡出；结束时由 _on_upgrade_finished 隐藏）。
func _play_upgrade_animation() -> void:
	if not is_instance_valid(_upgrade_anim) or _upgrade_anim.sprite_frames == null:
		return
	_upgrade_anim.visible = true
	_upgrade_anim.play("lvup")

## 升级动画播放一次结束后隐藏（保持「仅播放一次」语义，不在场上残留）。
func _on_upgrade_finished() -> void:
	if is_instance_valid(_upgrade_anim):
		_upgrade_anim.visible = false

## 给予经验值：累加到当前经验条，达到升级阈值时自动 emit upgrade_triggered 播放升级动画。
## 升级后等级+1、溢出经验滚入下一级。与「直接 emit upgrade」完全解耦——本方法才是真正的升级触发入口。
func add_xp(amount: float) -> void:
	if not is_instance_valid(_xp_component):
		return
	_xp_component.add_xp(amount)
	if _xp_component.check_level_up():
		var new_level := _xp_component.do_level_up()
		current_level = new_level
		xp_to_next = _get_xp_for_level(new_level)  # 从数据表取新等级所需经验
		_xp_component.set_max_xp(xp_to_next)
		print("UnitRoot: %s 升级到 Lv.%d！输出升级参数播放动画" % [unit_id, new_level])
		# 通过 EventBus 输出升级参数 → UnitRoot 自身监听 → 播放动画
		if is_instance_valid(_event_bus):
			_event_bus.emit_signal("upgrade_triggered", unit_id)

## 切换血条/经验条显示（互斥：显一个隐另一个）。
func toggle_bar_display(show_xp: bool) -> void:
	if is_instance_valid(_hp_component):
		_hp_component.visible = not show_xp
	if is_instance_valid(_xp_component):
		_xp_component.visible = show_xp

## 获取当前经验显示信息（供测试/调试/UI 使用）。
func get_xp_info() -> Dictionary:
	if not is_instance_valid(_xp_component):
		return {"level": current_level, "current": 0.0, "max": xp_to_next}
	return {"level": current_level, "current": _xp_component.get_current_xp(), "max": _xp_component.get_max_xp()}
func _get_xp_for_level(level: int) -> float:
	return float(level * 2)

## 构建 LVup 序列帧（SpriteFrames）：从 LVUP_DIR 顺序加载 lvup_001..LVUP_FRAME_COUNT。
## loop=false、speed=LVUP_FPS，配合 play("lvup") 实现「播放一次」。
func _build_lvup_frames() -> SpriteFrames:
	var sf := SpriteFrames.new()
	sf.add_animation("lvup")
	sf.set_animation_loop("lvup", false)
	sf.set_animation_speed("lvup", LVUP_FPS)
	for i in range(1, LVUP_FRAME_COUNT + 1):
		var path := LVUP_DIR + "lvup_%03d.png" % i
		var tex := load(path) as Texture2D
		if tex != null:
			sf.add_frame("lvup", tex)
		else:
			push_warning("UnitRoot: 升级序列帧缺失 %s（检查 res://assets/Unit UI/_lvup/ 是否导入）" % path)
	return sf
