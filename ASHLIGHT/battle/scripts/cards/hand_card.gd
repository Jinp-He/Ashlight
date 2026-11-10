class_name HandCard
extends Node2D

## 单张手牌容器：包裹一个 CardRoot 视觉实例 + 手牌元数据。
## 负责自身悬停命中测试与飞入/回收动画所需的尺寸缓存；颜色/可用性一律委托 CardRoot，本容器不画颜色。
## 排序/位置由 HandManager 统一驱动（向上不依赖 parent）。

## 手牌项状态：0 idle(牌库外) / 1 hand(在场) / 2 played(已打出回收)。
const STATE_IDLE: int = 0
const STATE_HAND: int = 1
const STATE_PLAYED: int = 2

var card: CardRoot
var idx: int = 0
var owner_group: String = ""
var cost: int = 0
var cost_color: int = 0                          # 接牌颜色(0红/1蓝/2黄/3绿)，镜像 CardRoot.cost_color
var base_number: int = 1                         # 抽到手时浮动确定的基础点数，离开手牌前不变
var state: int = STATE_IDLE
var order: int = 0
## 关联的卡牌静态数据（由 HandManager 从 CardDatabase 填充），打出时依次执行原子效果。
var data: CardData = null
var is_flashback: bool = false
## 每个手牌实例独立；普通抽牌和新闪回均从 0 开始，不写回 CardData。
var charge_points: int = 0

func add_charge(amount: int) -> void:
	if is_instance_valid(data) and data.has_charge:
		charge_points = maxi(0, charge_points + amount)

## 布局还原用：base_z=常态层级（layout 写入），home_pos=常态位置（layout 写入）。
## 悬停时临时抬升 z/位移，离开后恢复到这两个值。
var base_z: int = 0
var home_pos: Vector2 = Vector2.ZERO
var home_rotation: float = 0.0
## 飞入锁定：爆发抽牌/开局发牌等「从牌库飞入」期间为 true，禁止 layout(false) 非动画分支
## 由 layout 动画分支在飞入 tween 完成时解锁。
var flyin_lock: bool = false

## 卡牌原始像素尺寸（setup 后由 _cache_native 读取 Base 底图填充；默认 ZERO 表示尚未缓存）。
## 注意：绝不能用「非 0 默认值」当缓存标记——见 _cache_native 注释，否则高亮框会缩得比卡还小、藏到卡背后。
var _native_size: Vector2 = Vector2.ZERO
var _native_cached: bool = false

## ── 稳定悬停命中参数（由 HandManager.layout 注入，基于逻辑 home_pos，不含悬停上移/放大）──
## 目的：悬停动画(上移 hover_lift + 放大 hover_scale)会改变卡牌自身 transform，若命中测试读实时
## transform，底边会上移越过鼠标 → 反复进出 → 弹性抖动循环。故命中框固定为「逻辑 home 位置 + 基准缩放」，
## 使悬停只改视觉、不改判定区域，彻底断开正反馈。
var _hit_base_scale: float = 0.0
var _hit_lift: float = 0.0
var _hit_ready: bool = false

## 注入稳定命中参数。base_scale=手牌基准缩放，lift=悬停上移量(px)。layout 每次重排时调用。
func set_hit_params(base_scale: float, lift: float) -> void:
	_hit_base_scale = base_scale
	_hit_lift = lift
	_hit_ready = true


## 把 CardRoot 实例收编为子节点，并写入元数据。此时 card 尚未入场景树（_ready 未跑），
## 故原生尺寸延迟到 is_point_inside / get_native_width 首次调用时缓存（那时已 build 完）。
func setup(p_card: CardRoot, p_idx: int, p_group: String) -> void:
	card = p_card
	idx = p_idx
	owner_group = p_group
	cost = p_card.cost_value
	cost_color = p_card.cost_color
	add_child(p_card)


## 缓存卡牌原始尺寸（取 CardRoot 的 Base 底图尺寸）。
## 关键：必须用独立标记 _native_cached 判断是否已读取，不能用「_native_size 是否非 0」当判断——
## 否则一旦默认给了非 0 兜底值，本函数会永远提前返回、真实尺寸(如 400×576)读不进来，
## 导致高亮框 target 失真成比卡还小、被卡面完全盖住而「看不见高亮框」。
func _cache_native() -> void:
	if _native_cached:
		return
	_native_cached = true
	if is_instance_valid(card):
		var base := card.get_node_or_null("VisualGroup/Base") as TextureRect
		if is_instance_valid(base) and is_instance_valid(base.texture):
			_native_size = base.texture.get_size()
	if _native_size == Vector2.ZERO:
		_native_size = Vector2(256.0, 384.0)   # 底图缺失时兜底，避免命中测试全失效


func get_native_width() -> float:
	_cache_native()
	return _native_size.x


func get_native_height() -> float:
	_cache_native()
	return _native_size.y


## 命中测试：给定全局坐标是否落在当前卡牌矩形内。
## 关键：判定基于「稳定的逻辑 home 命中框」(set_hit_params 注入)，中心=home_pos.x / (home_pos.y - lift/2)，
## 半高=基准半高 + lift/2 —— 同时覆盖「待机(center=home)」与「悬停停定(center=home-lift)」两态，且固定不变。
## 故悬停上移/放大动画不会改判定区域 → 鼠标停底边也不会反复进出 → 弹性动画只播一次。
func is_point_inside(global_pos: Vector2) -> bool:
	_cache_native()
	if _hit_ready and _native_size != Vector2.ZERO:
		var hw: float = _native_size.x * _hit_base_scale * 0.5
		var hh: float = _native_size.y * _hit_base_scale * 0.5
		var cx: float = home_pos.x
		var cy: float = home_pos.y - _hit_lift * 0.5
		var top: float = cy - (hh + _hit_lift * 0.5)
		var bottom: float = cy + (hh + _hit_lift * 0.5)
		var lp := (get_parent() as Node2D).to_local(global_pos)
		# 常态扇形的角落也能选中；悬停仍用固定逻辑区域，避免动画导致反复进出。
		var fan_point: Vector2 = (lp - home_pos).rotated(-home_rotation)
		if absf(fan_point.x) <= hw and absf(fan_point.y) <= hh:
			return true
		return lp.x >= cx - hw and lp.x <= cx + hw and lp.y >= top and lp.y <= bottom
	# 兜底：未注入参数时回退到实时几何（避免开局前命中全失效），但此路径不参与悬停稳定性。
	var center := global_position
	var sz := _native_size * scale
	var half := sz * 0.5
	return global_pos.x >= center.x - half.x and global_pos.x <= center.x + half.x \
		and global_pos.y >= center.y - half.y and global_pos.y <= center.y + half.y


## ── 选中呼吸灯：以用户高亮框美术为模板，周期性克隆、放大淡出形成向外扩散光圈 ──
## 改编自 godot_effect/glow_border.gd：原版用 Panel+StyleBoxFlat 自绘描边，本版改用 Sprite2D + highlight_frame.png 美术。
## 选中卡牌时（set_select_frame）启动呼吸（start_pulsing），取消/打出时（clear_select_frame）停止；
## 本节点自身为「模板」隐藏，可见的是它生成的扩散环（克隆体），按用户要求「只显示扩散环、无静态底框」。
## 颜色由 base_color 决定（modulate = base_color * glow_energy），调用方按第一/二张传入不同色以区分状态。
## 美术铁规则：高亮框按原始尺寸 1:1 居中放置，绝不二次缩放去「匹配」卡牌；由 HandCard 整体缩放统一处理。

## 呼吸灯所用美术（高亮框）：用户美术，缺失仅告警跳过。
const BREATHING_FRAME_TEX: String = "res://assets/cards/highlight_frame.png"
## 预加载呼吸灯脚本（用 preload 实例化，避免依赖全局类缓存）。
const BreathingFrameScript: Script = preload("res://battle/scripts/cards/breathing_frame.gd")

var _breathing: BreathingFrame = null

## 惰性构建呼吸灯节点（每实例一次），置于卡牌之下（卡牌组最底层）、按美术原始尺寸 1:1 居中。
func _ensure_breathing() -> void:
	if _breathing != null:
		return
	_cache_native()
	_breathing = BreathingFrameScript.new()
	_breathing.name = "BreathingFrame"
	_breathing.texture_path = BREATHING_FRAME_TEX   # 让组件 _ready 内 load 指定美术
	add_child(_breathing)

## 选中时启动呼吸灯：tint 为染色（第一张/第二张用不同色区分状态，对应原 hand_manager 的 color_a）。
## dashed/color_b 参数保留以兼容 HandManager 调用端，内部已不使用。
func set_select_frame(_dashed: bool, color_a: Color, _color_b: Color) -> void:
	_ensure_breathing()
	if is_instance_valid(_breathing):
		_breathing.start_pulsing(color_a)

## 取消/打出/切换配对卡时停止呼吸灯（在途的扩散环会自然淡出销毁）。
func clear_select_frame() -> void:
	if is_flashback and state == STATE_HAND and not flyin_lock:
		set_select_frame(false, Color.WHITE, Color.WHITE)
		return
	if is_instance_valid(_breathing):
		_breathing.stop_pulsing()
