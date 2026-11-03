class_name BreathingFrame
extends Sprite2D

## 呼吸灯扩散光圈组件：以一张美术（高亮框）为模板，周期性克隆自身，每只「扩散环」在寿命内：
##   - 缩放从 start_scale 经「快→慢→更慢」减速曲线(ease-out)放大到 max_scale（向外扩散，初快末缓）
##   - 透明度默认「快淡入 → 匀速线性淡出」单次包络（linear_fade=true：速度恒定，与缩放减速解耦）；
##     亦可切回原「慢慢淡→快淡出」三段式（linear_fade=false）。
## 形成「由小匀速推出 + 渐缓淡出消失」的呼吸灯扩散观感（对应「缩小高亮美术框再放大出去」那一步的延伸）。
## 改编自 godot_effect/glow_border.gd：原版用 Panel + StyleBoxFlat 自绘描边 + Tween 单调淡出；本版改用 Sprite2D + 美术纹理。
## 本节点自身为「模板」默认隐藏，可见的是它生成的扩散环（克隆体）。
## 颜色由 base_color 决定（modulate = base_color * glow_energy），用于区分状态（如第一/二张卡不同色）。

@export var base_color: Color = Color(0.2, 0.6, 1.0, 1.0)
@export var glow_energy: float = 1.0
@export_group("光圈设置")
@export var start_scale: float = 0.8   # 起步缩放：<1 让环先缩在卡内再推出去，消除「满尺寸弹出」的顿感
@export var max_scale: float = 1.06
@export var pulse_duration: float = 1.35   # 单环寿命：越大扩散越慢（原 1.8，按记录的「调小约25%」套到扩散速度：1.8→1.35，推出更快）
@export var spawn_interval: float = 0.4   # 生成间隔：越小扩散环越密
@export_group("淡入淡出包络")
@export var linear_fade: bool = true   # true=快淡入后匀速线性淡出（速度恒定，与缩放匀速一致）；false=原「慢慢淡→快淡出」三段式
@export var fade_in_frac: float = 0.18   # 前 18% 时间：快淡入（迅速显现）
@export var fade_out_frac: float = 0.16   # 仅线性淡出=false 时生效：快淡出起点推后（p=0.84，更接近放大极限时才加速消失）
@export var mid_alpha: float = 0.50   # 仅线性淡出=false 时生效：中段「慢慢淡」透明度下限（1.0→此值缓慢下降）

## 美术路径（由 HandCard 注入，默认指向高亮框）。
var texture_path: String = "res://assets/cards/highlight_frame.png"

var _pulse_timer: Timer = null

func _ready() -> void:
	var tex := load(texture_path) as Texture2D
	if tex == null:
		push_warning("BreathingFrame: 高亮框美术缺失（%s），呼吸灯不显示。" % texture_path)
		return
	texture = tex
	centered = true
	position = Vector2(2.0, 0.0)   # 与手牌高亮框一致的右移 2px（卡牌局部坐标）
	z_index = -10
	scale = Vector2.ONE
	_apply_tint()
	visible = false   # 模板隐藏，仅扩散环可见

func _apply_tint() -> void:
	# 原版做法：modulate = base_color * glow_energy（HDR 亮度可调）
	modulate = Color(base_color.r * glow_energy, base_color.g * glow_energy, base_color.b * glow_energy, base_color.a)

## 由 HandCard 在卡牌选中时调用：设定染色并启动呼吸（重复调用不会叠加定时器）。
func start_pulsing(tint: Color = base_color) -> void:
	base_color = tint
	_apply_tint()
	if _pulse_timer != null:
		return
	_pulse_timer = Timer.new()
	_pulse_timer.wait_time = spawn_interval
	_pulse_timer.autostart = true
	_pulse_timer.timeout.connect(_spawn_pulse_ring)
	add_child(_pulse_timer)
	_spawn_pulse_ring.call_deferred()

## 克隆自身生成一个扩散环，用 tween_method 驱动「匀速放大 + 包络淡入淡出」后销毁。
func _spawn_pulse_ring() -> void:
	if texture == null:
		return
	# 1. 克隆自身（含纹理与 modulate）
	var ring := self.duplicate() as Sprite2D
	# 2. 剥掉脚本：克隆体变纯 Sprite2D，不会递归触发 _ready / 再生定时器
	ring.script = null
	# 3. 作为兄弟节点加到父节点（HandCard）下，并移到最底层（卡牌与模板之下）
	get_parent().add_child(ring)
	get_parent().move_child(ring, 0)
	# 4. 等一帧布局完成
	await get_tree().process_frame
	if not is_instance_valid(ring):
		return
	# 5. 复核克隆体属性
	ring.centered = true
	ring.position = position
	ring.z_index = -10
	ring.scale = Vector2(start_scale, start_scale)   # 起步缩小（默认 0.8，比卡面小），从卡内长出而非满尺寸弹出
	ring.modulate = modulate
	ring.visible = true
	# 6. 用 tween_method 把进度 p(0→1 匀速) 喂给 _update_ring：
	#    缩放走 ease-out 减速曲线（快→慢→更慢，在 _update_ring 内算）；
	#    透明度走「快淡入→匀速淡出」包络（linear_fade=true，默认，与缩放曲线解耦）。
	#    进度本身用 TRANS_LINEAR，缩放/透明度曲线都在 _envelope/_update_ring 内算。
	var tween := create_tween()
	tween.tween_method(_update_ring.bind(ring), 0.0, 1.0, pulse_duration) \
		.set_trans(Tween.TRANS_LINEAR).set_ease(Tween.EASE_IN_OUT)
	tween.tween_callback(ring.queue_free)

## 进度回调：p 由 0 匀速走到 1。缩放走 ease-out 减速（快→慢→更慢）；透明度按包络计算（默认匀速淡出）。
func _update_ring(p: float, ring: Sprite2D) -> void:
	if not is_instance_valid(ring):
		return
	# 缩放走「快→慢→更慢」减速曲线（ease-out 三次）：比二次末端更拖、接近 max_scale 时趋零更缓
	var e: float = 1.0 - (1.0 - p) * (1.0 - p) * (1.0 - p)
	var s: float = lerp(start_scale, max_scale, e)
	ring.scale = Vector2(s, s)
	ring.modulate.a = _envelope(p)

## 透明度包络：前 fade_in_frac 段「快淡入」(ease-out 迅速显现)，之后：
##   - linear_fade=true（默认）：匀速线性淡出到 0（速度恒定，与缩放减速解耦）；
##   - linear_fade=false：中段慢慢淡(1→mid_alpha) → 末段快淡出(接近放大极限加速消失)。
## p 有效范围 [0,1]，包络外返回 0（起始/结束不可见）。
func _envelope(p: float) -> float:
	if p <= 0.0 or p >= 1.0:
		return 0.0
	if p < fade_in_frac:
		# 快淡入：起步即快速显现（ease-out 二次曲线）
		var k := p / fade_in_frac
		return 1.0 - (1.0 - k) * (1.0 - k)
	if linear_fade:
		# 匀速淡出：从 1.0 线性降到 0（速度恒定，与缩放一致）
		var k := (p - fade_in_frac) / (1.0 - fade_in_frac)
		return 1.0 - k
	# ── 以下为原三段式（linear_fade=false），与历史版本一致 ──
	if p > 1.0 - fade_out_frac:
		# 快淡出：末段加速消失（ease-in 二次曲线），中段值 mid_alpha → 0
		var k := (p - (1.0 - fade_out_frac)) / fade_out_frac
		return mid_alpha * (1.0 - k * k)
	# 慢慢淡：中段从 1.0 缓慢降到 mid_alpha
	var span := (1.0 - fade_out_frac) - fade_in_frac
	var k := (p - fade_in_frac) / span if span > 0.0 else 0.0
	return lerp(1.0, mid_alpha, k)

## 停止呼吸（在途的扩散环会依其寿命自然淡出销毁）。
func stop_pulsing() -> void:
	if _pulse_timer != null:
		_pulse_timer.queue_free()
		_pulse_timer = null
	visible = false
