class_name WindParticles
extends Node2D

## 风尘粒子拖尾效果（纯代码、无贴图、无外部依赖）。
## 粒子沿 wind_angle 风向匀速移动，垂直于风向做正弦摆动，身后拖出渐隐残影；
## 飞出屏幕边缘 fade_margin 像素后延迟缓慢淡出并回收。全部用 _draw() 即时绘制。
## 挂载层级：放在战斗场景 Background/Midground 之上、Foreground 之下（不高于前景）即可。

@export var wind_angle: float = 7.2
@export var wind_speed: float = 400.0
@export var particle_interval: float = 1.5
@export var particle_count: int = 3
@export var particle_speed: float = 300.0
@export var particle_spread: float = 20.0
@export var particle_sway: float = 23.0
@export var particle_sway_freq: float = 1.23
@export var particle_size: float = 4.0
@export var particle_trail: int = 500
@export var particle_opacity: float = 0.1
@export var fade_margin: float = 800.0
@export var fade_duration_min: float = 3.0
@export var fade_duration_max: float = 5.0
@export var particle_color: Color = Color(1.0, 1.0, 1.0)
## 风尘粒子生成范围（世界坐标 Rect2）：默认 1600×500、贴顶(靠上方)，
## 下方被前景遮挡的区域不生成粒子；粒子仍可向下微漂，但「源头」恒在上半区。
## 与战斗设计分辨率 1600×900 对齐：x∈[0,1600] 全宽、y∈[0,500] 顶部。
@export var spawn_rect: Rect2 = Rect2(0.0, 0.0, 1600.0, 500.0)

## 单个风尘粒子（强类型，替代原版 Dictionary 以通过 GDScript 2.0 strict）。
class Particle:
	var pos: Vector2
	var vel: Vector2
	var sway_phase: float
	var trail: Array[Vector2] = []
	var rand_op: float
	var fade_start_time: float = -1.0
	var fade_duration: float = 0.0

	## initial=true 时全屏随机散布（首帧即有分布）；false 时从左侧屏外飘入（回收重生）。
	func reset(region: Rect2, angle: float, spread: float, speed: float, initial: bool) -> void:
		var a := angle + (randf() - 0.5) * spread
		if initial:
			pos = Vector2(region.position.x + randf() * region.size.x, region.position.y + randf() * region.size.y)
		else:
			pos = Vector2(region.position.x - 30.0 - randf() * 50.0, region.position.y + randf() * region.size.y)
		vel = Vector2(cos(a), sin(a)) * speed * (0.6 + randf() * 0.8)
		sway_phase = randf() * TAU
		rand_op = 0.2 + randf() * 0.4
		trail = []
		fade_start_time = -1.0
		fade_duration = 0.0

var _particles: Array[Particle] = []
var _particle_timer: float = 0.0
var _next_spawn_interval: float = 1.5
var _time: float = 0.0

func _ready() -> void:
	_reset_particles()

func _reset_particles() -> void:
	_particles.clear()
	var angle := deg_to_rad(wind_angle)
	var spread := deg_to_rad(particle_spread)
	for i in range(particle_count):
		var p := Particle.new()
		p.reset(spawn_rect, angle, spread, particle_speed, true)
		_particles.append(p)

func _spawn_particle() -> void:
	var angle := deg_to_rad(wind_angle)
	var spread := deg_to_rad(particle_spread)
	var p := Particle.new()
	p.reset(spawn_rect, angle, spread, particle_speed, false)
	_particles.append(p)

func _process(delta: float) -> void:
	_time += delta
	var angle := deg_to_rad(wind_angle)
	var spread := deg_to_rad(particle_spread)
	var perp := Vector2(-sin(angle), cos(angle))

	# 发射新粒子：间隔 1~2 秒，每次 1~2 个，总数上限 count×2
	_particle_timer += delta
	if _particle_timer >= _next_spawn_interval and _particles.size() < particle_count * 2:
		_particle_timer = 0.0
		_next_spawn_interval = 1.0 + randf() * 1.0
		_spawn_particle()
		if randf() > 0.5 and _particles.size() < particle_count * 2:
			_spawn_particle()

	var vp := get_viewport_rect().size
	for i in range(_particles.size() - 1, -1, -1):
		var p := _particles[i]
		p.trail.append(p.pos)
		if p.trail.size() > particle_trail:
			p.trail.pop_front()
		# 垂直风向的正弦摆动
		var sway := sin(_time * particle_sway_freq + p.sway_phase) * particle_sway * delta
		p.pos += p.vel * delta + perp * sway
		# 出屏淡出检测：超出 fade_margin 后才开始计时淡出
		var m := fade_margin
		var is_beyond: bool = p.pos.x > vp.x + m or p.pos.x < -m or p.pos.y < -m or p.pos.y > vp.y + m
		if is_beyond:
			if p.fade_start_time < 0.0:
				p.fade_start_time = _time
				p.fade_duration = fade_duration_min + randf() * (fade_duration_max - fade_duration_min)
			if _time - p.fade_start_time >= p.fade_duration:
				p.reset(spawn_rect, angle, spread, particle_speed, false)
		else:
			if p.fade_start_time >= 0.0:
				p.fade_start_time = -1.0
				p.fade_duration = 0.0

	# 限制总数
	while _particles.size() > particle_count * 2:
		_particles.pop_front()

	queue_redraw()

func _draw() -> void:
	for p in _particles:
		var trail := p.trail
		var max_alpha := minf(p.rand_op, particle_opacity)
		var fade := _get_fade(p)
		# 拖尾线（渐细渐淡）
		for t in range(trail.size() - 1):
			var frac := float(t) / float(trail.size())
			var alpha := frac * max_alpha * fade
			var sz := particle_size * (0.2 + frac * 0.8)
			var col := Color(particle_color.r, particle_color.g, particle_color.b, alpha)
			draw_line(trail[t], trail[t + 1], col, sz, true)
		# 粒子头
		if trail.size() > 0:
			var col := Color(particle_color.r * 1.1, particle_color.g * 1.1, particle_color.b * 1.1, max_alpha * 0.8 * fade)
			draw_circle(p.pos, particle_size * 0.7, col)

func _get_fade(p: Particle) -> float:
	if p.fade_start_time < 0.0:
		return 1.0
	var elapsed := _time - p.fade_start_time
	return clampf(1.0 - elapsed / p.fade_duration, 0.0, 1.0)
