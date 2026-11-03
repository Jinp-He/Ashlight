class_name CutsceneBurst
extends Node2D

## 演出滑入起手时的「粒子迸发」效果：
##   从中心向四周发散，颜色为白→黑的随机灰阶；初速最快、随后逐渐减速（缓动），
##   在减速过程中透明度从 1 渐隐到 0 后自动销毁。
## 由 CutsceneController 在「滑入起手」实例化并 play()；进入缓动区时被施加时间缩放(慢放)，离开恢复。动画结束自动 queue_free。
## 所有数值均为 @export，可直接在 cutscene_burst.tscn 中调参、配合 cutscene_preview 的「迸发&圆环」按钮孤立预览。

@export_group("粒子迸发")
@export var particle_count: int = 24                 # 粒子数量
@export var particle_size: float = 6.0               # 粒子大小(px，直径)
@export var particle_spread_speed: float = 360.0     # 粒子扩散速度(px/s，初始向外速度)——调慢：600→360
@export var particle_ease_speed: float = 1.6         # 粒子缓动速度(阻力系数/s)——调缓：3.0→1.6（减速更慢→扩散更远）
@export var particle_ease_duration: float = 1.3      # 粒子缓动时长(s)——拉长：0.5→1.3（减速窗口更长）
@export var particle_fade_delay: float = 0.45        # 粒子消失等待时间(s)——0.25→0.45（更晚开始淡出）
@export var particle_fade_duration: float = 0.75     # 粒子消失时间(s)——0.4→0.75（整体存活更久→扩散范围更大）

const _BASE_TEX_SIZE: int = 32

var _tex: Texture2D = null
var _parts: Array = []      # 每项为 Dictionary: {spr: Sprite2D, pos: Vector2, vel: Vector2, age: float}
var _active: bool = false
var _time_scale: float = 1.0   # 时间缩放：缓动区内由编排器设为 <1 实现慢放，离开恢复 1.0

func _ready() -> void:
	_tex = _make_circle_tex()
	# 层级：单位美术(z1)/闪白(z2) 之上，buff提示(z6)/伤害数字(z7) 之下。
	z_index = 5

## 在本地原点(0,0)开始迸发（调用方已把本节点 position 设到锚点）。
func play() -> void:
	if particle_count <= 0 or _tex == null:
		queue_free()
		return
	_active = true
	var rng := RandomNumberGenerator.new()
	for i in particle_count:
		var spr := Sprite2D.new()
		spr.texture = _tex
		spr.centered = true
		var sc: float = particle_size / float(_BASE_TEX_SIZE)
		spr.scale = Vector2(sc, sc)
		var g: float = rng.randf()                       # 白(1.0)→黑(0.0) 随机灰阶
		spr.modulate = Color(g, g, g, 1.0)
		add_child(spr)
		var ang: float = rng.randf() * TAU
		var spd: float = particle_spread_speed * rng.randf_range(0.6, 1.0)
		var vel := Vector2(cos(ang), sin(ang)) * spd
		_parts.append({"spr": spr, "pos": Vector2.ZERO, "vel": vel, "age": 0.0})

## 由编排器在缓动区进入/离开时调用，对存活实例施加/恢复时间缩放（<1=慢放）。
func set_time_scale(f: float) -> void:
	_time_scale = f

func _process(delta: float) -> void:
	if not _active:
		return
	var dt: float = delta * _time_scale
	var alive: bool = false
	for p in _parts:
		var spr := p["spr"] as Sprite2D
		if not is_instance_valid(spr):
			continue
		var age: float = float(p["age"]) + dt
		p["age"] = age
		var vel := p["vel"] as Vector2
		# 缓动：ease_duration 内施加阻力，速度逐渐趋零（开头最快、随后渐缓）。
		if age < particle_ease_duration:
			var drag: float = clampf(particle_ease_speed * dt, 0.0, 1.0)
			vel = vel.lerp(Vector2.ZERO, drag)
			p["vel"] = vel
		var pos := p["pos"] as Vector2 + vel * dt
		p["pos"] = pos
		spr.position = pos
		# 透明度：等待 fade_delay 后开始淡出，fade_duration 内归零。
		var a: float = 1.0
		if age > particle_fade_delay:
			a = 1.0 - (age - particle_fade_delay) / maxf(particle_fade_duration, 0.0001)
		a = clampf(a, 0.0, 1.0)
		var m := spr.modulate
		m.a = a
		spr.modulate = m
		if a > 0.0:
			alive = true
	if not alive:
		queue_free()

## 生成一张实心白圆纹理（运行时生成，避免引入额外美术文件）；粒子按需缩放复用此纹理。
func _make_circle_tex() -> ImageTexture:
	var s: int = _BASE_TEX_SIZE
	var img := Image.create(s, s, false, Image.FORMAT_RGBA8)
	img.fill(Color(0.0, 0.0, 0.0, 0.0))
	var c := Vector2(s * 0.5, s * 0.5)
	for y in s:
		for x in s:
			var d: float = (Vector2(x + 0.5, y + 0.5) - c).length()
			if d <= s * 0.5 - 0.5:
				img.set_pixel(x, y, Color(1.0, 1.0, 1.0, 1.0))
	return ImageTexture.create_from_image(img)
