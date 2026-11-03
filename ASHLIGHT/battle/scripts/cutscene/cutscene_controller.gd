class_name CutsceneController
extends Node2D

## 战斗「打入/施法」演出编排器（Phase 1：玩家出牌）。
## 读取施法者(主目标)与受击者(次目标)的单位美术，生成 A/B 单位图沿参考实现 generate_cutscene.py 的轨迹滑入：
##   我方：左外滑入→向右→右外出；敌方：右外滑入→向左→左外出（单位 A 恒在左、B 恒在右，左右入场时 x 轴处于对应居中位置，仅方向翻转）。
##   经过中央缓动区[600,1000]减速（对位定格感）。注意：CutsceneInsert 14帧特效 + 屏幕震动不再由本控制器触发——
##   改由「触发演出」的调用方在 play() 同时同步播放（插入帧立即播、震屏延迟 shake_delay 起手），本控制器仅负责单位滑入编排。
## 美术仅取单位态贴图（res://assets/units/{faction}/{id}/{id}_{State}.png），不含血条/buff/意图等部件。
## 单位美术不翻转（无论我方/敌方方向，仅位置从对应屏外滑入）；仅插入帧特效对敌方水平翻转（扫击方向反转）。
## 美术态按卡牌类型(施法者)或敌方意图类型(Phase 2)选取；怪物缺 Charge 态回退 Idle。
## 单位图保持原尺寸显示、底部对齐（脚线贴 unit_baseline）。
## 依赖：CutsceneInsert（group "cutscene_insert"，层级须低于本节点）由调用方同步播放；BattleRoot/Stage 父节点用于屏幕震动偏移。

signal finished
## 缓动区（慢速对位区）进入/离开：命中瞬间对外广播，供调用方同步触发自己的效果（如真实伤害数字结算）。
signal slow_zone_entered
signal slow_zone_exited

# --- 运动参数（与 generate_cutscene.py / BattleCutscene_Preview 对齐）---
@export var entry_delay: float = 0.1       # 触发后等待多久单位图才出现并开始滑入（入场前停顿）
@export var slide_speed: float = 4000.0
@export var slow_width: float = 55.0        # 中央缓速区总宽（同时作为速度渐变尺度）：宽 55px、居中[772.5,827.5]（half=slow_width*0.5≈27.5，与预览红线框「缓动区」对齐）；锚点离中心 dist 经 smoothstep(dist/slow_width) 连续映射速度（越近越慢），取代原过渡带
@export var slow_factor: float = 0.02       # 缓动区最慢速度系数：slow_speed = slide_speed * slow_factor（0.02 → 80px/s，锚点正对中心时达到此最慢速度）；相对 0.028 再调低，整体更慢
@export var max_duration: float = 4.0       # 演出总时长兜底上限（从 play 起算）；slide_speed=4000/slow_factor=0.02/slow_width=55 下：平台停留~0.69s + 过渡/进出场，整段约 1.3~1.5s，4.0 留足余量；到达 _end_x 即提前结束
@export var unit_baseline: float = 430.0   # 单位图底部基线 y（底部对齐：脚线落此）；先下移120(360→480) 再上移50(→430)，净下移70
@export var unit_scale: float = 1.0        # 单位图缩放（保持原尺寸 = 1.0）
# 站位间距（faction-aware）：阵营内 intra_spacing、阵营间 cross_gap（跨阵营处用 cross_gap）。
# 整组以 design_width*0.5 为轴居中；slot_count/design_width 另用于入场起止 x 计算。
@export var slot_count: int = 6
@export var design_width: float = 1600.0
@export var intra_spacing: float = 170.0     # 阵营内单位间距(px)：与 Battle 场景一致（缩短自旧均匀 266.7）
@export var cross_gap: float = 266.7          # 阵营间间距(px)：沿用旧均匀插槽值（Battle 场景为 300，如需与战斗对齐改此值）

# --- 屏幕震动参数（与插入帧同步：play 后延迟 shake_delay 起手）---
@export var shake_amp: float = 3.0
@export var shake_delay: float = 0.3
@export var shake_duration: float = 0.25

# --- 缓动区单位效果（进入慢速对位区时出现并播放、离开缓动区后消失）---
# 说明：本编排器单位仅有「裸美术」、不含血条/buff框/意图等部件；下列效果是缓动区内叠加在单位美术上的演出层。
@export_group("Slow-Zone FX")
## 单位闪白（在单位美术轮廓内叠加纯白，不溢出美术图）。
## 用户 2026-10-03 反馈观感不佳，临时关闭；如后续要恢复，置回 true 即可。
@export var flash_enabled: bool = false
@export var flash_alpha: float = 0.9            # 闪白不透明度（瞬时亮起，无透明度过渡）
@export var flash_pulse_time: float = 0.25      # 闪白持续时长(s)：进入缓动区瞬时亮起并保持，到点瞬时熄灭（一次性硬闪，不持续白膜）
## 闪白仅作用于「被选中的目标单位」（单位数组第 2 个及以后）；false=全部单位都闪。
@export var flash_targets_only: bool = true
## buff/debuff 获取提示（滑入浮条，复用 UnitBuff 的 toast 美术）：进入缓动区滑入、离开时淡出。
@export var prompt_enabled: bool = true
## 提示组中心距「单位美术中心底边」的像素数（向上为正）。
## 默认值 72 取自战场单位组 buff 提示组：BuffRoot.y(53) + toast_offset_y(-120) → 提示组中心 UnitRoot 本地 y=-67；
## 美术底边在 y=+5 → 距底边 72px 上方。使编排器提示与战场单位组提示同位（锚点=美术中心底边）。
@export var prompt_above_bottom: float = 72.0
@export var prompt_slide_dist: float = 20.0     # 滑入起始上偏移(px)
@export var prompt_in_time: float = 0.20
@export var prompt_out_time: float = 0.14
## 伤害数字（定位：锚点 = 单位美术中心底边；基准上移 damage_base_up，再叠加 上下 随机抖动）。
## 水平区间按阵营分：我方(player)居中偏左 damage_faction_offset、敌方(enemy)居中偏右，各自在 ±damage_region_spread/2 内随机取位（攻击数字中心锚点在区域内随机）。
@export var damage_number_enabled: bool = true
@export var damage_base_up: float = 150.0    # 相对美术底边的基准上移(px)（单段起始，较初版下移100）
@export var damage_up_jitter: float = 50.0   # 额外上移抖动范围 0..50(px)
@export var damage_faction_offset: float = 50.0  # 阵营区域中心相对单位中心的横向偏移(px)：我方取其负值(偏左)、敌方取其正值(偏右)
@export var damage_region_spread: float = 50.0  # 阵营区域内随机取位的总宽度(px)：中心偏移 ±spread/2（即 50px 区间）
@export var damage_seg_gap: float = 10.0     # 多段之间保底间隙(px)：按「实际数字高度」留白（content_height + 此间隙），不再用写死整组尺寸
@export var damage_seg_gap_jitter: float = 0.0  # 间隙浮动(px)：用户要求固定 10px 间距，默认不浮动

# --- 缓动区锚点命中效果（粒子迸发 + 扩散圆环）：进入慢速对位区时于锚点位置生成 ---
# 具体数值参数（粒子数量/大小/速度/缓动/消失、圆环粗细/扩散速度 等）定义在对应场景根节点的 @export，
# 可直接在 cutscene_burst.tscn / cutscene_ring.tscn 中调参；本处仅放总开关。
@export_group("Burst FX (缓动区·粒子迸发)")
@export var burst_enabled: bool = true
@export_group("Ring FX (缓动区·扩散圆环)")
@export var ring_enabled: bool = true
@export_group("Slow-Zone FX Anchor (锚点)")
# 锚点基准 = 编排器的「固定中心」(设计坐标，不随单位/镜头变化)，非实际画面中心(viewport 中心)。
# 粒子迸发/扩散圆环均以此点为出生与扩散中心；fx_anchor_offset 在其上做微调(Y 负=上移、X 右正左负)。
@export var fx_anchor_center: Vector2 = Vector2(800.0, 450.0)
@export var fx_anchor_offset: Vector2 = Vector2(0.0, -150.0)   # 锚点相对编排器固定中心的上移量(px)：Y 负=上移(-150=上移150)
@export var fx_slow_time_scale: float = 0.4   # 缓动区内对冲击波/圆环施加的时间缩放(<1=慢放)；离开缓动区恢复 1.0

# --- 滑入形变（横向拉伸着色器，仅形变A·整体横向缩放）---
# 形变系数由「整组锚点 _center_x 离缓动区中心(W*0.5)的距离」统一计算、整组同步施加：
#   锚点离缓动区越近 → 系数越低 → 越接近原比例(1.0)；锚点离得越远 → 系数越高 → 拉伸至 slide_stretch_max。
# 不按各单位自身是否「粘到缓动区」(旧逻辑基于 b.position.x) 判定，避免整组形变不一致。
# 仅作用于单位美术（不影响子节点 buff 提示/伤害数字）。
@export var slide_stretch_enabled: bool = true
@export var slide_stretch_max: float = 2.0     # 锚点离缓动区最远时横向最大拉伸倍数
@export var slide_stretch_ramp: float = 250.0 # 锚点离缓动区边缘多远形变系数达到最大(px)：smoothstep(边缘→边缘+ramp) 映射 0..1

const W: float = 1600.0
const FLASH_SHADER_PATH: String = "res://battle/scripts/units/flash_white.gdshader"
const BUFF_TOAST_PATH: String = "res://assets/Unit UI/unit_hud/buff_toast.png"
const FONT_PATH: String = "res://assets/fonts/DouyinSansBold.otf"
## buff/debuff 获取提示模块（图标 + 滑入提示条，Centered 成组）。preload 脚本(.new()) 实例化，运行期 _ready 自构件。
const CutsceneBuffPrompt := preload("res://battle/scripts/cutscene/cutscene_buff_prompt.gd")
## 伤害数字组件（按 FX/DamageNumber/README_DamageNumber.md 实现）。preload 脚本实例化。
const DamageNumberCls := preload("res://FX/DamageNumber/DamageNumber.gd")
## 滑入横向拉伸着色器（canvas_item，仅形变A·整体横向缩放）：上层按整组锚点离缓动区距离写入 u_stretch。
const SlideStretchShader := preload("res://battle/scripts/cutscene/slide_stretch.gdshader")
## 护盾(shield)不计入 buff/debuff 提示器：护盾有独立的表现层（单位血条护盾叠层 gain_shield），
## 不走 add_buff / 提示器。此名单内的名称/图标绝不生成 buff 提示组（防御性，避免误喂）。
const BUFF_PROMPT_BLOCKLIST: Array[String] = ["护盾", "shield"]
## 缓动区锚点命中效果：粒子迸发 / 扩散圆环场景（参数见各自根节点 @export，可在场景中直接调）。
const CutsceneBurstScene := preload("res://battle/scripts/cutscene/cutscene_burst.tscn")
const CutsceneRingScene := preload("res://battle/scripts/cutscene/cutscene_ring.tscn")

# 当前演出中由「滑入起手」生成的冲击波/圆环实例追踪（供缓动区慢放控制，自动清理已销毁引用）。
var _fx_bursts: Array = []
var _fx_rings: Array = []
var _fx_spawned: bool = false   # 本次演出是否已生成冲击波/圆环（首帧开始滑动时生成一次）

var _unit_sprites: Array[Sprite2D] = []
var _slot_x: Array[float] = []             # 各单位静息对齐的 x
var _unit_half_heights: Array[float] = []  # 各单位图半高（含缩放），用于底部对齐
var _elapsed: float = 0.0
var _active: bool = false
var _shake_at: float = -1.0
var _direction: String = "player"
var _battle_root: Node2D
var _center_x: float = 0.0       # 单位对中心锚点当前 x（位置驱动运动的状态）
var _center_dir: int = 1         # +1 我方从左、-1 敌方从右
var _start_x: float = 0.0
var _end_x: float = 0.0

# --- 缓动区单位效果状态 ---
var _in_slow_zone: bool = false                 # 当前中心锚点是否处于缓动区（慢速对位区）
var _flash_mat: ShaderMaterial = null           # 闪白共享材质（所有单位闪白共用一次强度 tween）
var _flash_strength: float = 0.0                # 当前闪白强度（供离场从当前值淡出）
var _flash_sprites: Array[Sprite2D] = []        # 各单位闪白层（单位图子节点，随单位图销毁）
var _flash_off_timer: SceneTreeTimer = null     # 闪白硬灭定时器（进入缓动区瞬时亮起，到点瞬时熄灭）
var _fx_nodes: Array[Node] = []                 # 伤害数字等效果节点（离场统一淡出销毁）
var _dmg_burst_id: int = 0                       # 当前「跳字组」批次号：间距只比对同批次、同单位的多段
var _stretch_materials: Array[ShaderMaterial] = []  # 与 _unit_sprites 对齐：每单位美术的横向拉伸材质（未启用/无则 null）
var _unit_fx_data: Array[Dictionary] = []       # 与 _unit_sprites 对齐：{damage, buff_name, buff_tone, dmg_payload}
## 静态 buff 提示（滑入即带、持久显示，不做获取/移除动画）。作为单位图子节点随其销毁，不加入 _fx_nodes。
var _static_prompts: Array[Node] = []
var _font: Font = null                          # 抖音美好体（提示/伤害数字共用）

func _ready() -> void:
	add_to_group("cutscene_controller")
	_battle_root = get_parent() as Node2D
	_font = load(FONT_PATH) as Font
	var sh := load(FLASH_SHADER_PATH) as Shader
	if sh != null:
		_flash_mat = ShaderMaterial.new()
		_flash_mat.shader = sh
		_flash_mat.set_shader_parameter("u_strength", 0.0)
	else:
		push_warning("CutsceneController: 闪白着色器加载失败 %s（闪白将不显示）" % FLASH_SHADER_PATH)

## 启动一次演出。caster/target 为字典 {"id":String,"faction":String,"state":int(UnitArtState)}；
## target 为空字典表示自施法（仅 caster 单块居中）。direction ∈ {"player","enemy"}。
## 注意：本方法只负责单位滑入编排；插入帧特效与屏幕震动由调用方在调用本方法的同时同步触发
## （解耦：插入帧立即播、震屏延迟 shake_delay 起手），本控制器不再触发 CutsceneInsert。
## 启动一次演出（兼容旧单/双单位调用）。caster/target 为字典 {"id","faction","state"}；
## target 为空表示自施法（仅 caster 单块）。多单位请改用 play_units()。
func play(caster: Dictionary, target: Dictionary, direction: String, cost: int) -> void:
	var entries: Array[Dictionary] = []
	if not caster.is_empty() and caster.get("id", "") != "":
		entries.append({"id": caster.get("id", ""), "faction": caster.get("faction", "player"), "state": int(caster.get("state", 0))})
	if not target.is_empty() and target.get("id", "") != "":
		entries.append({"id": target.get("id", ""), "faction": target.get("faction", "enemy"), "state": int(target.get("state", 0))})
	_start_play(entries, direction, cost)

## 多单位入口：units 为任意数量单位字典数组（每个含 id/faction/state）。
## 用于预览场景随机组合（我方1+敌方3、敌方2+我方3 等）以测试多单位对齐/居中逻辑。
## direction ∈ {"player","enemy"} 决定整组从哪侧滑入（与各单位阵营无关，阵营仅决定美术）。
func play_units(units: Array[Dictionary], direction: String, cost: int) -> void:
	begin_damage_burst()          # 每次演出起手开启新批次：本演出的多段数字归为同一跳字组
	_start_play(units, direction, cost)

## 真正启动演出的内部实现（units 为已展开的单位字典数组）。
func _start_play(units: Array[Dictionary], direction: String, cost: int) -> void:
	_clear_units()
	_direction = direction
	_elapsed = 0.0
	_shake_at = -1.0          # 演出抖动已移除：永不触发震屏（_update_shake 在 _shake_at<0 时直接 return，不偏移父节点 position）
	# 位置驱动运动初值：中心锚点从对应屏外起滑。
	_center_dir = 1 if direction == "player" else -1
	var sp: float = design_width / float(slot_count)
	_start_x = -sp if _center_dir == 1 else W + sp
	_end_x = W + sp if _center_dir == 1 else -sp
	_center_x = _start_x
	# 阵营排序：我方(player)恒居左、敌方(enemy)恒居右（用户 2026-09-21 要求）。
	# 同阵营内保留传入先后顺序决定相对左右；跨阵营则我方整体在左、敌方整体在右。
	var sorted_u: Array[Dictionary] = []
	var enemy_u: Array[Dictionary] = []
	for u in units:
		if u.get("faction", "player") == "enemy":
			enemy_u.append(u)
		else:
			sorted_u.append(u)
	sorted_u.append_array(enemy_u)
	units = sorted_u
	if units.is_empty():
		return
	# 对齐位置：阵营内间距 intra_spacing、阵营间间距 cross_gap（跨阵营处用 cross_gap）。
	# 阵营排序后我方恒在左、敌方恒在右；整组相对坐标以「设计宽中心」为轴居中展开。
	_slot_x.clear()
	_unit_half_heights.clear()
	var rel_x: Array[float] = []
	var cum: float = 0.0
	for i in units.size():
		if i > 0:
			var is_cross: bool = units[i - 1].get("faction", "player") != units[i].get("faction", "player")
			cum += cross_gap if is_cross else intra_spacing
		rel_x.append(cum)
	var mid_off: float = design_width * 0.5 - cum * 0.5   # 居中：rel_x 中点对齐设计宽中心
	for i in units.size():
		_slot_x.append(rel_x[i] + mid_off)
		var spr := Sprite2D.new()
		spr.centered = true
		# 单位美术不翻转：无论我方/敌方方向，角色朝向保持原美术，仅整体位置从对应屏外滑入。
		spr.texture = _load_unit_texture(units[i].get("id", ""), units[i].get("faction", "player"), int(units[i].get("state", 0)))
		spr.scale = Vector2.ONE * unit_scale
		var h: float = spr.texture.get_height() if spr.texture != null else 0.0
		_unit_half_heights.append(h * unit_scale * 0.5)
		spr.visible = false
		spr.z_index = 1
		spr.set_meta("faction", units[i].get("faction", "player"))   # 伤害数字水平区间按阵营取位
		add_child(spr)
		_unit_sprites.append(spr)
		_stretch_materials.append(_attach_stretch(spr))   # 挂载滑入形变着色器材质（未启用则为 null）
		# 逐单位可选效果数据（伤害数字 / buff 提示）——单位字典可选字段，缺省即不触发该类效果。
		var d: Dictionary = {
			"damage": int(units[i].get("damage", 0)),
			"buff_name": String(units[i].get("buff_name", "")),
			"buff_tone": int(units[i].get("buff_tone", 0)),
			"buff_icon": String(units[i].get("buff_icon", "")),
			"dmg_payload": units[i].get("dmg_payload", {}),
		}
		_unit_fx_data.append(d)
		# 静态 buff 提示：单位滑入即带（持久状态，不做获取动画/移除），位置同战场单位组（距底 prompt_above_bottom）。
		if prompt_enabled and String(d.get("buff_name", "")) != "":
			_spawn_buff_prompt_static(spr, String(d.get("buff_name", "")), int(d.get("buff_tone", 0)), String(d.get("buff_icon", "")))
		# 伤害数字：滑入即带（持久状态、无动画），随单位图销毁；不进 _fx_nodes（不被 _stop_slow_zone_fx 淡出）。
		# 仅当 damage_number_enabled 且单位带 dmg_payload 时触发；多段间距比对按同批次同单位（dmg_burst meta）。
		if damage_number_enabled and not d.get("dmg_payload", {}).is_empty():
			_spawn_damage_number(spr, d.get("dmg_payload", {}), true)
	_active = true

func _clear_units() -> void:
	for b in _unit_sprites:
		if is_instance_valid(b):
			b.queue_free()
	_unit_sprites.clear()
	_slot_x.clear()
	_unit_half_heights.clear()
	# 效果层随单位图一起销毁（闪白/伤害数字均为单位图的子节点）；此处仅复位追踪状态与闪白强度。
	# 静态 buff 提示同为单位图子节点随其销毁，这里只清追踪数组。
	_unit_fx_data.clear()
	_fx_nodes.clear()
	_static_prompts.clear()
	_flash_sprites.clear()
	_stretch_materials.clear()   # 材质随单位图(queue_free)销毁，这里只清追踪数组
	_in_slow_zone = false
	_flash_off_timer = null
	_set_flash_strength(0.0)
	_fx_bursts.clear()   # 冲击波/圆环自管理销毁；此处仅清追踪数组（节点随控制器或自身 queue_free）
	_fx_rings.clear()
	_fx_spawned = false

func _load_unit_texture(id: String, faction: String, state: int) -> Texture2D:
	if id == "":
		return null
	var base := "res://assets/units/%s/%s/%s_" % [faction, id, id]
	var state_path := base + _state_suffix(state) + ".png"
	var tex: Texture2D = load(state_path) as Texture2D if ResourceLoader.exists(state_path) else null
	if tex == null and state != 0:
		tex = load(base + "Idle.png") as Texture2D   # 缺态回退 Idle（怪物无 Charge 时即落此）
	if tex == null:
		push_warning("CutsceneController: 单位美术缺失 %s/%s state=%d" % [faction, id, state])
	return tex

func _state_suffix(state: int) -> String:
	match state:
		0: return "Idle"
		1: return "Attack"
		2: return "Cast"
		3: return "Hit"
		4: return "Charge"
	return "Idle"

## 当前应使用的滑动速度，仅由「中心锚点离缓动区中心的距离」决定（位置驱动，无时间上限）：
##   缓动区中心 ±slow_width*0.5 内（与特效触发区 now_in 对齐）→ 恒 slow_speed = slide_speed * slow_factor（低速平台，对位停留 / 顿感）；
##   平台外 → 经 smoothstep 从 half 连续过渡至 slide_speed（全速进场/离场），过渡宽度 = slow_width（无独立过渡带参数）。
func _current_speed(x: float) -> float:
	var half: float = slow_width * 0.5                        # 缓动区半宽 = 低速平台半径（与特效触发区 now_in 对齐）
	var dist: float = abs(x - W * 0.5)
	if dist <= half:
		return slide_speed * slow_factor                      # 平台内：恒最慢，制造对位停留 / 顿感
	var t: float = smoothstep(0.0, 1.0, clampf((dist - half) / slow_width, 0.0, 1.0))
	return lerpf(slide_speed * slow_factor, slide_speed, t)   # 平台外：smoothstep 连续加速至全速

func _process(delta: float) -> void:
	if not _active:
		return
	_elapsed += delta
	_update_shake()   # 震屏独立于单位图可见性：entry_delay 期间也要能震（与插入帧同步打击）
	if _elapsed < entry_delay:
		for b in _unit_sprites:
			if is_instance_valid(b):
				b.visible = false
		if _elapsed >= max_duration:
			_finish()
		return
	# 位置驱动：每帧按中心锚点当前是否在缓动区内决定速度并推进，不依赖任何时间分段。
	# 首帧真正开始滑动时生成冲击波/圆环（取代旧「进入缓动区才生成」逻辑）。
	if not _fx_spawned:
		_spawn_fx_at_start()
		_fx_spawned = true
	_center_x += _center_dir * _current_speed(_center_x) * delta
	var reached_end: bool = (_center_dir == 1 and _center_x >= _end_x) or (_center_dir == -1 and _center_x <= _end_x)
	if reached_end:
		_center_x = _end_x
	var off: float = _center_x - (design_width * 0.5)
	# 滑入形变系数：以「整组锚点 _center_x」离缓动区中心(W*0.5)的距离统一计算（整组同步形变）。
	# 锚点离缓动区越近系数越低→原比例(1.0)；离得越远系数越高→最大拉伸(slide_stretch_max)。
	# 不按各单位自身是否「粘到缓动区」(旧逻辑基于 b.position.x) 判定，避免整组形变不一致。
	var _stretch_val: float = 1.0
	if slide_stretch_enabled:
		var anchor_dist: float = abs(_center_x - W * 0.5)
		var t: float = smoothstep(0.0, 1.0, clampf((anchor_dist - slow_width * 0.5) / slide_stretch_ramp, 0.0, 1.0))
		_stretch_val = lerpf(1.0, slide_stretch_max, t)
	for i in _unit_sprites.size():
		var b := _unit_sprites[i] as Sprite2D
		if not is_instance_valid(b):
			continue
		b.visible = true
		# 底部对齐：脚线（sprite 底边）贴 unit_baseline，整图向上生长。
		b.position = Vector2(_slot_x[i] + off, unit_baseline - _unit_half_heights[i])
		# 滑入形变：整组统一施加上面算出的 _stretch_val（基于锚点距离），仅作用于单位美术。
		if slide_stretch_enabled and i < _stretch_materials.size():
			var sm: ShaderMaterial = _stretch_materials[i]
			if is_instance_valid(sm):
				sm.set_shader_parameter("u_stretch", _stretch_val)
	# 缓动区效果：进入慢速对位区展开（闪白/提示/伤害数字）、离开时收起。
	_update_slow_zone_fx()
	# 冲击波/圆环跟随编排器滑入：每帧把已生成实例拉到实时锚点（X 随 _center_x 平移）。
	_update_fx_follow()
	# 插入帧已改由调用方在触发时同步播放，本控制器不再触发 CutsceneInsert。
	if reached_end or _elapsed >= max_duration:
		_finish()

func _update_shake() -> void:
	if _shake_at < 0.0 or _battle_root == null:
		return
	if _elapsed < _shake_at:
		return
	var st: float = _elapsed - _shake_at
	if st >= shake_duration:
		_battle_root.position = Vector2.ZERO
		_shake_at = -1.0
		return
	var f: float = 1.0 - st / shake_duration
	var ang: float = randf() * TAU
	_battle_root.position = Vector2(cos(ang), sin(ang)) * shake_amp * f

func _finish() -> void:
	_active = false
	_clear_units()
	if is_instance_valid(_battle_root):
		_battle_root.position = Vector2.ZERO
	_shake_at = -1.0
	finished.emit()


## ---- 缓动区单位效果（闪白 / buff 提示 / 伤害数字）----
## 进入/离开检测：中心锚点进入 [750,850] 慢速对位区时展开效果、离开时收起；状态变化对外发信号。
func _update_slow_zone_fx() -> void:
	var now_in: bool = absf(_center_x - W * 0.5) <= slow_width * 0.5
	if now_in and not _in_slow_zone:
		_in_slow_zone = true
		_play_slow_zone_fx()
		slow_zone_entered.emit()
	elif _in_slow_zone and not now_in:
		_in_slow_zone = false
		_stop_slow_zone_fx()
		slow_zone_exited.emit()


## 判定第 i 个单位是否属于「被选中的目标」：数组第 2 个及以后（=受击者）；仅 1 个单位时即该单位（自施法）。
func _is_fx_target(i: int) -> bool:
	if not flash_targets_only:
		return true
	return _unit_sprites.size() <= 1 or i > 0


## 缓动区锚点坐标：X 跟随编排器整组滑入中心 _center_x（即"编排器移动"），Y 固定为
## 设计中心 Y(fx_anchor_center.y) + 偏移(fx_anchor_offset.y)，不随滑入改变。
## 因此冲击波/圆环会随演出滑入一起平移（从入口滑到中心再滑出），而非钉死在固定点。
## fx_anchor_offset.x 作相对移动中心的水平微调（默认 0），fx_anchor_center.x 仅为静止时的中心参考值。
func _anchor_world_pos() -> Vector2:
	return Vector2(_center_x + fx_anchor_offset.x, fx_anchor_center.y + fx_anchor_offset.y)


## 展开缓动区单位效果：仅剩闪白（各自开关；仅作用于被选中的目标单位）。
## 注意：buff/debuff 提示 与 伤害数字 均已改为「单位滑入即挂载」的持久状态（见 _spawn_buff_prompt_static / _start_play 的持久伤害数字），
## 不在此处获取/移除，整段滑入→滑出持续可见、无动画。
func _play_slow_zone_fx() -> void:
	for i in _unit_sprites.size():
		var spr := _unit_sprites[i] as Sprite2D
		if not is_instance_valid(spr) or spr.texture == null:
			continue
		if not _is_fx_target(i):
			continue
		if flash_enabled:
			_spawn_flash(spr)
	# 闪白：进入缓动区瞬时亮起（无透明度过渡），保持 flash_pulse_time 后瞬时熄灭（一次性硬闪，不持续白膜）。
	if flash_enabled and not _flash_sprites.is_empty():
		_set_flash_strength(flash_alpha)
		_flash_off_timer = get_tree().create_timer(flash_pulse_time)
		_flash_off_timer.timeout.connect(_on_flash_off)
	# 粒子迸发 + 扩散圆环：已在「滑入起手」(_spawn_fx_at_start) 生成于锚点位置；
	# 此处改为对存活实例施加缓动区慢放（离开缓动区时恢复 1.0，见 _stop_slow_zone_fx）。
	_apply_fx_time_scale(fx_slow_time_scale)


## 滑入起手即生成冲击波/圆环（取代旧的「进入缓动区才生成」逻辑）：
## 于固定锚点位置实例化并 play()，存档引用供缓动区慢放控制。
func _spawn_fx_at_start() -> void:
	var anchor := _anchor_world_pos()
	if burst_enabled:
		var b := CutsceneBurstScene.instantiate() as CutsceneBurst
		if b != null:
			add_child(b)
			b.position = anchor
			b.play()
			_fx_bursts.append(b)
	if ring_enabled:
		var r := CutsceneRingScene.instantiate() as CutsceneRing
		if r != null:
			add_child(r)
			r.position = anchor
			r.play()
			_fx_rings.append(r)


## 每帧把已生成的冲击波/圆环拉到实时锚点，使其随编排器滑入一起平移（X 跟随 _center_x）。
## 缓动区慢放(_apply_fx_time_scale)只改内部时间缩放，不影响此处位置跟随。
func _update_fx_follow() -> void:
	if not _fx_spawned:
		return
	var p := _anchor_world_pos()
	for b in _fx_bursts:
		if is_instance_valid(b):
			b.position = p
	for r in _fx_rings:
		if is_instance_valid(r):
			r.position = p


## 对存活的冲击波/圆环施加时间缩放（缓动区内 <1 慢放，离开恢复 1.0）；自动清理已销毁实例引用。
func _apply_fx_time_scale(f: float) -> void:
	for arr in [_fx_bursts, _fx_rings]:
		var i: int = arr.size() - 1
		while i >= 0:
			var n = arr[i]
			if is_instance_valid(n) and n.has_method("set_time_scale"):
				n.set_time_scale(f)
				i -= 1
			else:
				arr.remove_at(i)
				i -= 1


## 写入闪白共享材质的强度（供 Tween 驱动；同时记录当前值，便于离场从当前值淡出）。
func _set_flash_strength(v: float) -> void:
	_flash_strength = v
	if is_instance_valid(_flash_mat):
		_flash_mat.set_shader_parameter("u_strength", v)


## 闪白硬灭回调：定时器到点瞬时熄灭（无过渡）。
func _on_flash_off() -> void:
	_set_flash_strength(0.0)
	_flash_off_timer = null


## 为单位 Sprite 挂载「滑入横向拉伸」着色器材质（仅作用美术本身，不影响子节点 buff 提示/伤害数字）。
## 返回 ShaderMaterial 供上层逐帧更新 u_stretch（基于整组锚点距离统一计算）；未启用(slide_stretch_enabled=false)则返回 null。
func _attach_stretch(spr: Sprite2D) -> ShaderMaterial:
	if not slide_stretch_enabled:
		return null
	var mat := ShaderMaterial.new()
	mat.shader = SlideStretchShader
	mat.set_shader_parameter("u_stretch", 1.0)   # 初始原比例；逐帧由 _process 按锚点距离写入形变系数
	spr.material = mat
	return mat


## 生成单位闪白层：与单位图同纹理（alpha 作蒙版）、纯白 blend_add 叠加 → 严格限制在美术轮廓内、不溢出。
func _spawn_flash(spr: Sprite2D) -> void:
	if not is_instance_valid(_flash_mat):
		return
	var f := Sprite2D.new()
	f.name = "HitFlash"
	f.texture = spr.texture
	f.centered = spr.centered
	f.material = _flash_mat
	f.z_index = 2                 # 绘制在单位美术之上
	spr.add_child(f)              # 作为单位图子节点 → 随单位滑入/重排一起移动、随单位图销毁
	_flash_sprites.append(f)


## 生成 buff/debuff 提示模块（持久挂载版）：单位滑入即带在身上，不做获取/移除动画，随单位图销毁。
## Icon + Toast 两部件水平居中成组（CutsceneBuffPrompt 内部布局）。
## 锚点 = 单位美术中心底边；提示组中心置于「底边缘上方 prompt_above_bottom px」（与战场单位组同位，距底 72px）。
## 不入 _fx_nodes：因此不会被 _stop_slow_zone_fx 淡出移除，作为单位的持久状态显示到演出结束。
func _spawn_buff_prompt_static(spr: Sprite2D, display_name: String, tone: int, icon_name: String = "") -> void:
	# 护盾不计入 buff 提示器：名称/图标在 BUFF_PROMPT_BLOCKLIST 内则跳过（护盾走独立护盾叠层）。
	if BUFF_PROMPT_BLOCKLIST.has(display_name) or BUFF_PROMPT_BLOCKLIST.has(icon_name):
		return
	var prompt := CutsceneBuffPrompt.new()
	prompt.name = "BuffPrompt"
	prompt.z_index = 6
	spr.add_child(prompt)            # 加入树 → _ready 自构件（Icon + Toast）
	prompt.setup(display_name, tone, icon_name)
	var art_h: float = spr.texture.get_height() if spr.texture != null else 120.0
	prompt.position = Vector2(0.0, art_h * 0.5 - prompt_above_bottom)
	prompt.modulate = Color(1.0, 1.0, 1.0, 1.0)   # 直接显示，无滑入淡入动画
	_static_prompts.append(prompt)


## 伤害数字：按 payload 委托 DamageNumber 组件构建（两行结构、配色/描边/暴击放大 见 README §3~§9）。
## 锚点 = 单位美术中心底边；基准上移 damage_base_up，再叠加 0..damage_up_jitter 上移。
## 水平区间按阵营分：我方(player)居中偏左 -damage_faction_offset、敌方(enemy)居中偏右 +damage_faction_offset，
## 各自在 ±damage_region_spread/2 内随机取位（伤害数字中心锚点在区域内随机生成）。
## 每段打上当前「跳字组」批次号(dmg_burst meta)。
## 保底间距仅比对「同批次、同单位(spr)」的已有数字：跨次点击(旧节点未即时释放)与跨单位都不计入，
## 只有同一次跳字在同个单位上的多段才会错开。首段不强制上推，落在用户指定的基准区间内。
## payload 缺省按 normal 处理：{type, value, is_crit, source, absorbed_amount, blocked}（见 README §9）。
func _spawn_damage_number(spr: Sprite2D, payload: Dictionary, persistent: bool = false) -> void:
	var dmg := DamageNumberCls.new()
	dmg.name = "DamageNumber"
	dmg.z_index = 7
	dmg.set_meta("dmg_burst", _dmg_burst_id)   # 标记所属跳字组批次
	spr.add_child(dmg)
	var p: Dictionary = payload if not payload.is_empty() else {"type": "normal", "value": 0}
	dmg.spawn(p)
	var art_h: float = spr.texture.get_height() if spr.texture != null else 120.0
	# 阵营水平区间：我方(player)偏左、敌方(enemy)偏右，各自在偏移 ± spread/2 内随机取位。
	var fac: String = spr.get_meta("faction", "player")
	var region_center: float = damage_faction_offset if fac == "enemy" else -damage_faction_offset
	var half: float = damage_region_spread * 0.5
	var dx: float = region_center + randf_range(-half, half)
	var up: float = damage_base_up + randf_range(0.0, damage_up_jitter)
	var y: float = art_h * 0.5 - up
	# 保底间距：仅当同单位(spr)、同批次(dmg_burst)已有伤害数字时生效，避免多段重叠。
	# 按「实际数字高度」算间隙（不再用写死整组尺寸）：新数字底缘 - gap = 旧数字顶缘，gap≈damage_seg_gap 浮动。
	# 只向上推（更小 y），若随机位置已足够高则不回拉。
	var has_existing: bool = false
	var top_y: float = y
	var top_h: float = 0.0
	for c in spr.get_children():
		if c != dmg and c is DamageNumber and c.get_meta("dmg_burst", -1) == _dmg_burst_id:
			has_existing = true
			if c.position.y < top_y:
				top_y = c.position.y
				top_h = c.content_height
	if has_existing:
		var new_h: float = dmg.content_height
		var gap: float = damage_seg_gap + randf_range(0.0, damage_seg_gap_jitter)
		var required_y: float = top_y - top_h * 0.5 - gap - new_h * 0.5
		if y > required_y:
			y = required_y
	dmg.position = Vector2(dx, y)
	# 持久版（滑入即带、全程可见）不进 _fx_nodes，随单位图销毁；非持久版（旧缓动区临时生成）进 _fx_nodes 离区淡出。
	if not persistent:
		_fx_nodes.append(dmg)


## 开启一个新的「跳字组」批次（同一次多段伤害归为同批，间距只对本批同单位生效）。
## 调用时机：每次演出 play_units 起手；测试场景每次点击「造成伤害」起手。
func begin_damage_burst() -> void:
	_dmg_burst_id += 1


## 收起缓动区单位效果：闪白硬灭（一次性脉冲到点已自灭；离区偏早则立即熄灭，无过渡），
## 提示/伤害数字淡出后销毁。提示模块（CutsceneBuffPrompt）有 play_out 则调其自身淡出销毁；其余走通用淡出。
func _stop_slow_zone_fx() -> void:
	_set_flash_strength(0.0)
	_apply_fx_time_scale(1.0)   # 离开缓动区：冲击波/圆环恢复常速播放
	for n in _fx_nodes:
		if is_instance_valid(n):
			if n.has_method("play_out"):
				n.play_out(prompt_out_time)
			else:
				var nt := create_tween().set_parallel(true)
				nt.tween_property(n, "modulate:a", 0.0, prompt_out_time)
				nt.tween_callback(n.queue_free).set_delay(prompt_out_time)
	_fx_nodes.clear()
