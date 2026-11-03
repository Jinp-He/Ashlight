class_name IntentTelegraph
extends Node2D

## 敌人意图抛物线预警：从施法者（敌人）美术中心，向「意图攻击范围内」各目标美术中心，
## 画一条二次贝塞尔抛物线弧（向上拱起）+ 运动箭头 + 渐隐拖尾。
## 两种播放模式（触发条件由 IntentRoot 控制）：
##   · play_once() —— 单次：施法者→目标 飞一次，到达后整条收束越过目标并淡出，播完自隐藏。
##                    用于「新生成意图播放一次」。
##   · play_loop() —— 循环：同上单次流程，淡出后无缝重开（淡入）持续飞行，用于「悬停查看时持续播放」。
##   · stop()      —— 隐藏并停止。
## 动画三阶段：① 飞行 (caster→target) ② 收束消散（箭头停在目标、整条沿末段切线继续前移 overshoot、
##            拖尾收拢、整体淡出）③ 循环时从 ① 无缝重开（首段 fade-in 避免突跳重生）。
## 坐标：本节点作为 IntentRoot 子节点，绘制用世界坐标经 to_local 转换；每帧从 caster/targets 的

@export var arc_height_factor: float = 0.30   # 弧高 = 水平距离 × 此系数（向上拱）
@export var line_width: float = 4.0           # 弧线/拖尾线宽
@export var trail_span: float = 0.38          # 拖尾占整段比例 (0..1)
@export var arrow_size: float = 18.0          # 箭头（三角）大小
@export var flight_duration_single: float = 0.55   # 单次飞行时长（秒）
@export var flight_duration_loop: float = 1.1      # 循环飞行时长（秒）
@export var exit_duration: float = 0.42       # 收束消散时长（秒）—— 到达目标后「持续位移 + 淡出」的过程
@export var overshoot_distance: float = 70.0   # 收束阶段整条越过目标的前移像素距离（固定、与弧长无关）
@export var fade_in_span: float = 0.15         # 飞行首段淡入比例（仅用于循环重开时平滑出现，避免突跳）
@export var color: Color = Color(1.0, 0.35, 0.25, 1.0)   # 预警色（attack 默认红）

enum Mode { IDLE, SINGLE, LOOP }
enum Phase { IDLE = 0, FLIGHT = 1, EXIT = 2 }

var _mode: int = Mode.IDLE
var _phase: int = Phase.IDLE
var _t: float = 0.0          # 飞行进度（弧参数 0..1）
var _ec: float = 0.0         # 收束消散进度 (0..1)
var _flight_dur: float = 1.0
var _caster: UnitRoot
var _targets: Array[UnitRoot] = []
var _valid: bool = false

## 更新数据（不改动当前播放状态/可见性）：施法者、目标集、颜色。
func setup(caster: UnitRoot, targets: Array[UnitRoot], col: Color = Color(1.0, 0.35, 0.25, 1.0)) -> void:
	_caster = caster
	_targets = targets
	color = col
	_valid = is_instance_valid(_caster)

## 单次：施法者→目标 飞一次，到达后收束越过目标并淡出，播完自隐藏。无有效施法者/目标则无效。
func play_once() -> void:
	if not _valid or _targets.is_empty():
		return
	_mode = Mode.SINGLE
	_start_flight(flight_duration_single)

## 持续循环飞行（悬停查看）：单次流程结束后无缝重开，淡入→飞行→收束淡出→重开。
func play_loop() -> void:
	if not _valid or _targets.is_empty():
		return
	_mode = Mode.LOOP
	_start_flight(flight_duration_loop)

## 进入飞行阶段（重置进度，首段淡入起点）。
func _start_flight(dur: float) -> void:
	_phase = Phase.FLIGHT
	_t = 0.0
	_ec = 0.0
	_flight_dur = dur
	visible = true

## 停止并隐藏。
func stop() -> void:
	_mode = Mode.IDLE
	_phase = Phase.IDLE
	visible = false

func _process(delta: float) -> void:
	if _phase == Phase.IDLE:
		return
	if not _valid or _targets.is_empty():
		stop()
		return
	if _phase == Phase.FLIGHT:
		_t += delta / _flight_dur
		if _t >= 1.0:
			_t = 1.0
			_phase = Phase.EXIT
			_ec = 0.0
	else:   # Phase.EXIT：收束消散
		_ec += delta / exit_duration
		if _ec >= 1.0:
			if _mode == Mode.SINGLE:
				stop()
				return
			# LOOP：无缝重开（淡入起点，alpha 由 0 起，不突跳）
			_start_flight(_flight_dur)
	queue_redraw()

func _draw() -> void:
	if _phase == Phase.IDLE or not _valid or not is_instance_valid(_caster):
		return
	var p0w: Vector2 = _caster.get_art_center_global()
	for tgt in _targets:
		if not is_instance_valid(tgt):
			continue
		var p2w: Vector2 = tgt.get_art_center_global()
		_draw_arc(p0w, p2w)

## 单条抛物线的拖尾 + 箭头。根据当前阶段决定 head/tail 区间与整体 alpha。
func _draw_arc(p0w: Vector2, p2w: Vector2) -> void:
	var p0: Vector2 = to_local(p0w)
	var p2: Vector2 = to_local(p2w)
	var dist: float = p0.distance_to(p2)
	if dist < 2.0:   # 自施法（起点≈终点）无弧，跳过
		return
	var mid: Vector2 = (p0 + p2) * 0.5
	var arc_h: float = -dist * arc_height_factor   # 向上拱（屏幕 y 负方向）
	var p1: Vector2 = mid + Vector2(0.0, arc_h)

	var g_alpha: float            # 整体 alpha（淡入/淡出）
	var head_t: float             # 箭头所在弧参数
	var tail_t0: float            # 拖尾尾端弧参数
	var offset := Vector2.ZERO    # 收束阶段整条前移偏移（沿末段切线越过目标）

	if _phase == Phase.FLIGHT:
		head_t = _t
		tail_t0 = maxf(0.0, _t - trail_span)
		g_alpha = clampf(_t / fade_in_span, 0.0, 1.0)   # 首段淡入（循环重开平滑）
	else:   # Phase.EXIT：箭头停在目标，拖尾收拢，整条沿切线越过目标并淡出
		var p := 1.0 - _ec                       # 收拢系数：1→0（拖尾向箭头收拢）
		head_t = 1.0
		tail_t0 = 1.0 - trail_span * p
		var tangent: Vector2 = (p2 - p1).normalized()   # 抵达目标时的切线方向
		offset = tangent * (overshoot_distance * _ec)    # 随消散进度前移越过目标
		g_alpha = 1.0 - _ec                            # 整体淡出

	var q0: Vector2 = p0 + offset
	var q1: Vector2 = p1 + offset
	var q2: Vector2 = p2 + offset

	# 拖尾：从 tail_t0 到 head_t 分段画，越靠近 head 越亮（彗星尾）。
	var steps: int = 16
	for i in range(steps):
		var ta: float = lerpf(tail_t0, head_t, float(i) / float(steps))
		var segment_end: float = lerpf(tail_t0, head_t, float(i + 1) / float(steps))
		var a0: Vector2 = _bez(q0, q1, q2, ta)
		var b0: Vector2 = _bez(q0, q1, q2, segment_end)
		var a: float = lerpf(0.12, 1.0, float(i) / float(steps)) * g_alpha
		draw_line(a0, b0, Color(color.r, color.g, color.b, color.a * a), line_width, true)
	# 箭头：head_t 处朝向切线方向。
	var head: Vector2 = _bez(q0, q1, q2, head_t)
	var ahead: Vector2 = _bez(q0, q1, q2, minf(head_t + 0.012, 1.0))
	var dir: Vector2 = (ahead - head).normalized()
	if dir == Vector2.ZERO:
		dir = (q2 - q0).normalized()
	_draw_arrow(head, dir, g_alpha)

## 朝 dir 的三角箭头。
func _draw_arrow(pos: Vector2, dir: Vector2, a: float) -> void:
	var c := Color(color.r, color.g, color.b, color.a * a)
	var tip: Vector2 = pos + dir * arrow_size
	var wing_l: Vector2 = pos + dir * (-arrow_size * 0.3) + dir.rotated(PI / 2.0) * arrow_size * 0.55
	var wing_r: Vector2 = pos + dir * (-arrow_size * 0.3) + dir.rotated(-PI / 2.0) * arrow_size * 0.55
	var poly: PackedVector2Array = [tip, wing_l, wing_r]
	draw_colored_polygon(poly, c)

## 二次贝塞尔。
func _bez(p0: Vector2, p1: Vector2, p2: Vector2, t: float) -> Vector2:
	var u: float = 1.0 - t
	return u * u * p0 + 2.0 * u * t * p1 + t * t * p2
