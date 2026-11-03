class_name FireHud
extends Control

## 全队共享 🔥 预算可视化（规则2·火计数 §6）。3 个 pip = 🔥_MAX，亮=可用、暗=已耗。
## 美术：assets/Unit UI/Fire_Chain.png —— 6 帧横向精灵图（64×64/帧，白色剪影），
##       由 modulate 染色（沿用工程「纯白形状靠 modulate 着色」约定）。
## 亮格恒纯白（不再做流光变色），空槽恒纯黑。

const FIRE_MAX: int = 3
const PIP_SIZE: float = 64.0          # 显示尺寸 = 原始帧 64×64（保持原始尺寸，不缩放）
const PIP_SPACING: float = 40.0       # 相邻 pip 中心距（再调窄：52→40）
## 🔥 组锚定到「连锁快照卡」下方：X 与快照卡中心对齐，Y 在快照卡中心下方 FIRE_SNAPSHOT_GAP_Y。
## 快照卡中心落点实时取自 hand_manager 的托盘公式（取不到时回落到下方兜底常量，需与 hand_manager.gd 默认同步）。
const FIRE_SNAPSHOT_GAP_Y: float = 150.0         # 🔥 组中心相对「快照卡中心」向下偏移(px)；正=更靠下（原50，本次再下100→150）
const FIRE_ALIGN_OFFSET: Vector2 = Vector2.ZERO  # 额外微调偏移（x负=左移 / y负=上移）
## hand_manager 兜底值（与其 @export 默认值保持一致）：combo_tray_margin=200 / combo_tray_lift=70 / hand_y=810 / combo_snap_offset=(-40,-20)
const FIRE_HAND_Y_FB: float = 810.0
const FIRE_TRAY_MARGIN_FB: float = 200.0
const FIRE_TRAY_LIFT_FB: float = 70.0
const FIRE_SNAP_OFF_FB: Vector2 = Vector2(-40.0, -20.0)
const PIP_EMPTY: Color = Color(0.0, 0.0, 0.0, 1.0)   # 空槽纯黑（已耗 🔥 不显）
const PIP_LIT: Color = Color(1.0, 1.0, 1.0, 1.0)     # 已点亮 🔥 恒纯白

const FRAME_SIZE: int = 64
const FRAME_COUNT: int = 6
const FPS: float = 8.0               # 每 0.125 秒一帧（8fps）
const FIRE_SHEET: Texture2D = preload("res://assets/Unit UI/Fire_Chain.png")

var _pips: Array[AnimatedSprite2D] = []
var _lit: Array[bool] = []

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	custom_minimum_size = Vector2(PIP_SPACING * (FIRE_MAX - 1) + PIP_SIZE, PIP_SIZE)
	var frames := _build_frames()
	for i in FIRE_MAX:
		var s := AnimatedSprite2D.new()
		s.sprite_frames = frames
		s.centered = true
		s.modulate = PIP_EMPTY
		s.position = Vector2(i * PIP_SPACING + PIP_SIZE * 0.5, PIP_SIZE * 0.5)
		add_child(s)
		s.play("burn")
		_pips.append(s)
		_lit.append(false)
	# 锚定到「连锁快照卡」下方（含窗口尺寸变化重锚）
	_reposition_to_snapshot()
	if get_viewport() != null:
		get_viewport().size_changed.connect(_reposition_to_snapshot)
	# 连 BattleRoot.fire_changed（团队共享池）：初始刷新 + 后续实时刷新。
	var br := get_tree().get_first_node_in_group("battle_root") as BattleRoot
	if is_instance_valid(br):
		br.fire_changed.connect(_on_fire_changed)
		_on_fire_changed(br.get_team_fire(), FIRE_MAX)

## 整组锚定到「连锁快照卡」下方：X 与快照卡中心对齐，Y = 快照卡中心 + FIRE_SNAPSHOT_GAP_Y。
## 快照中心由 hand_manager 托盘公式实时读取（取不到时回落到兜底常量），并监听视口尺寸变化重锚。
func _reposition_to_snapshot() -> void:
	var vp: Viewport = get_viewport()
	if vp == null:
		return
	var sz: Vector2 = vp.size
	var w: float = custom_minimum_size.x
	var h: float = custom_minimum_size.y
	# 快照卡中心默认落点（与 hand_manager 托盘公式一致：托盘原点即快照卡中心）
	var snap_cx: float = sz.x - FIRE_TRAY_MARGIN_FB + FIRE_SNAP_OFF_FB.x
	var snap_cy: float = FIRE_HAND_Y_FB - FIRE_TRAY_LIFT_FB + FIRE_SNAP_OFF_FB.y
	var hm := get_tree().get_first_node_in_group("hand_manager")
	if is_instance_valid(hm) and "combo_tray_margin" in hm:
		snap_cx = sz.x - float(hm.combo_tray_margin) + hm.combo_snap_offset.x
		snap_cy = float(hm.hand_y) - float(hm.combo_tray_lift) + hm.combo_snap_offset.y
	# 组中心对齐快照卡中心(横向)，并置于其下方 FIRE_SNAPSHOT_GAP_Y
	position = Vector2(snap_cx - w * 0.5, snap_cy + FIRE_SNAPSHOT_GAP_Y - h * 0.5) + FIRE_ALIGN_OFFSET

## 由横向精灵图构建 6 帧循环动画 "burn"（每帧一个 AtlasTexture 区域）。
func _build_frames() -> SpriteFrames:
	var sf := SpriteFrames.new()
	if sf.has_animation("default"):
		sf.remove_animation("default")
	sf.add_animation("burn")
	sf.set_animation_speed("burn", FPS)
	sf.set_animation_loop("burn", true)
	for i in FRAME_COUNT:
		var at := AtlasTexture.new()
		at.atlas = FIRE_SHEET
		at.region = Rect2(i * FRAME_SIZE, 0, FRAME_SIZE, FRAME_SIZE)
		sf.add_frame("burn", at)
	return sf

## remaining 个 pip 点亮（恒纯白），其余熄灭（纯黑 + 定格）。
func _on_fire_changed(remaining: int, _max_val: int) -> void:
	for i in _pips.size():
		var on := i < remaining
		_lit[i] = on
		if on:
			_pips[i].modulate = PIP_LIT
			if not _pips[i].is_playing():
				_pips[i].play("burn")
		else:
			_pips[i].modulate = PIP_EMPTY
			_pips[i].stop()
