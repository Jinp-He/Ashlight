class_name CutsceneGroup2
extends Node2D
## 演出组2：战场原位姿态切换；finished 包含回切与所有特效尾段。
signal finished
## 仅放慢本演出，不改变全局时间或手牌动画速度。
@export_range(0.1, 3.0, 0.05) var duration_multiplier: float = 1.0
const SILHOUETTE := preload("res://battle/scripts/cutscene2/silhouette.gdshader")
const DAMAGE_NUMBER := preload("res://FX/DamageNumber/DamageNumber.gd")
@export var transition_duration: float = 0.3
@export var friendly_wait: float = 0.5
@export var flash_duration: float = 0.2
@export var hit_hold: float = 0.5
@export var shake_amplitude: float = 10.0
@export var hit_stagger: float = 0.1
@export var random_hit_rotation: bool = true
@export var defense_fade: float = 0.2
var _elapsed: float = 0.0
var _return_at: float = 0.0
var _end_at: float = 0.0
var _returned: bool = false
var _playing: bool = false
var _kind: int = 0
var _blocked: bool = false
var _hits: int = 1
var _chain: int = 1
var _caster: UnitRoot
var _actors: Array[Dictionary] = []
var _targets: Array[Dictionary] = []
var _ghosts: Array[Sprite2D] = []
var _frames: Array[Texture2D] = []
var _number_nodes: Array[DamageNumber] = []
var _breathing_paused_units: Array[UnitRoot] = []
var _numbers_end: float = 0.0
var _rng := RandomNumberGenerator.new()

func play(effect: Dictionary) -> void:
	_rng.randomize()
	_caster = effect.caster
	_kind = int(effect.get("kind", 0 if effect.stat == "damage" else (2 if effect.stat == "shield" else 1)))
	_blocked = bool(effect.get("blocked", false))
	_hits = maxi(1, int(effect.get("hits", 1)))
	if _hits > 10:
		hit_stagger = minf(hit_stagger, 1.0 / _hits)
		flash_duration = minf(flash_duration, 0.1)
	_chain = maxi(1, int(effect.get("chain", 1)))
	var hostile: bool = false
	for unit in effect.targets:
		if not is_instance_valid(unit):
			continue
		var opposing: bool = unit.unit_faction != _caster.unit_faction
		hostile = hostile or opposing
		var item := _capture(unit, opposing)
		var particles: Array[Dictionary] = []
		for i in (10 if _kind == 1 else 3):
			particles.append({"angle": _rng.randf_range(0, TAU),
				"distance": _rng.randf_range(101.5, 145.0) if _kind == 1 else _rng.randf_range(107.25, 195.0),
				"size": _rng.randf_range(6.5, 12.5) if _kind == 1 else _rng.randf_range(6, 12),
				"life": _rng.randf_range(0.85, 1.15) * (1.0 if _kind == 1 else 0.75),
				"rotation": _rng.randf_range(0, TAU), "spin": _rng.randf_range(-1, 1) * deg_to_rad(50 if _kind == 1 else 90)})
		item["particles"] = particles
		var angles: Array[float] = []
		var angle: float = _rng.randf_range(0, TAU)
		for i in _hits:
			if i > 0:
				angle = fposmod(angle + _rng.randf_range(PI / 4, TAU - PI / 4), TAU)
			angles.append(angle if random_hit_rotation else 0.0)
		item["angles"] = angles
		_targets.append(item)
		if unit == effect.get("number_target", unit):
			_show_effect_text(unit, effect)
		if opposing and unit != _caster:
			_actors.append(item)
			_switch(item, UnitRoot.UnitArtState.HIT, Color.BLACK)
			if _kind == 0 and not _blocked:
				item["flash"] = _make_ghost(item.sprite, Color.BLACK, 1)
	var caster_item := _capture(_caster, false)
	_actors.append(caster_item)
	var active_state: int = UnitRoot.UnitArtState.ATTACK
	if _kind == 1:
		active_state = UnitRoot.UnitArtState.CAST
	elif _kind == 2:
		active_state = UnitRoot.UnitArtState.CHARGE
	_switch(caster_item, active_state, Color.WHITE)
	_return_at = flash_duration + minf(0.1, hit_stagger) * (_hits - 1) + hit_hold if hostile and _kind == 0 else (hit_hold if hostile else friendly_wait)
	var fx_end: float = (_hits - 1) * hit_stagger + 0.25
	if _kind == 1:
		fx_end = 1.15
	elif _kind == 2 or _blocked:
		fx_end = 0.8625 + defense_fade
	_end_at = maxf(maxf(_return_at + transition_duration, fx_end), _numbers_end)
	if _kind == 0 and not _blocked:
		for i in 5:
			_frames.append(load("res://assets/vfx/group2/hit_%02d.png" % i) as Texture2D)
	_playing = true

func _show_effect_text(unit: UnitRoot, effect: Dictionary) -> void:
	var stat: String = effect.stat
	if stat in ["damage", "shield", "heal"]:
		var shield_left: int = int(unit.get_shield())
		var segments: int = _hits if stat == "damage" else 1
		for segment in segments:
			var number := DAMAGE_NUMBER.new()
			number.duration_multiplier = duration_multiplier
			# 数字归战斗层所有；逗留和淡出独立于演出组，不占用操作锁。
			number.z_index = z_index + 1
			get_parent().add_child(number)
			var value: int = int(effect.value)
			var invalid: int = int(effect.get("invalid_amount", 0))
			if stat == "damage":
				invalid = mini(value, shield_left)
				shield_left -= invalid
				value -= invalid
			number.spawn({"type": "normal" if stat == "damage" else stat,
				"value": value, "invalid_amount": invalid, "is_crit": bool(effect.get("is_crit", false))})
			var delay: float = segment * hit_stagger * duration_multiplier if stat == "damage" else 0.0
			var anchor := effect.get("number_anchor") as Node2D
			if is_instance_valid(anchor):
				var number_layer := number.get_parent() as Node2D
				var anchor_canvas := anchor.get_global_transform_with_canvas().origin
				number.play_popup(number_layer.get_global_transform_with_canvas().affine_inverse() * anchor_canvas, delay)
			else:
				number.play_on_unit(unit, delay)
			_number_nodes.append(number)
			_numbers_end = maxf(_numbers_end, (delay + number.blocking_duration()) / duration_multiplier)

func _numbers_ready() -> bool:
	for number in _number_nodes:
		if is_instance_valid(number) and not number.popup_settled:
			return false
	return true

func _capture(unit: UnitRoot, hostile: bool) -> Dictionary:
	if (hostile or unit == _caster) and not _breathing_paused_units.has(unit):
		unit.set_breathing_paused(true)
		_breathing_paused_units.append(unit)
	return {"unit": unit, "sprite": unit._art_sprite, "state": unit.current_art_state,
		"position": unit._art_sprite.position, "hostile": hostile}

func _make_ghost(sprite: Sprite2D, color: Color, layer: int) -> Sprite2D:
	var ghost := Sprite2D.new()
	ghost.texture = sprite.texture
	ghost.transform = sprite.transform
	ghost.centered = sprite.centered
	ghost.offset = sprite.offset
	ghost.flip_h = sprite.flip_h
	ghost.flip_v = sprite.flip_v
	ghost.z_index = sprite.z_index + layer
	var material := ShaderMaterial.new()
	material.shader = SILHOUETTE
	material.set_shader_parameter("silhouette_color", color)
	ghost.material = material
	sprite.get_parent().add_child(ghost)
	_ghosts.append(ghost)
	return ghost

func _switch(item: Dictionary, state: int, color: Color) -> void:
	var unit: UnitRoot = item.unit
	if not is_instance_valid(unit) or unit.current_art_state == state:
		return
	# 旧图立即成为纯色剪影；新图在其上方出现。仅剪影层整体淡出。
	var ghost := _make_ghost(item.sprite, color, -1)
	unit.set_art_state(state)
	create_tween().tween_property(ghost, "modulate:a", 0.0, transition_duration * duration_multiplier)

func _process(delta: float) -> void:
	if not _playing:
		return
	_elapsed += delta / duration_multiplier
	var flash_end: float = flash_duration + minf(0.1, hit_stagger) * (_hits - 1)
	for item in _targets:
		if not is_instance_valid(item.unit) or not is_instance_valid(item.sprite):
			continue
		if item.has("flash") and is_instance_valid(item.flash):
			item.flash.visible = _elapsed < flash_end
		if item.hostile and _kind == 0 and not _blocked:
			var shake_end: float = minf(flash_end + 0.15, _return_at)
			var offset: float = 0.0
			if _elapsed < shake_end:
				offset = sin(_elapsed * 55 * (1 + 0.3 * (_hits - 1))) * shake_amplitude * (1 + 0.4 * (_hits - 1)) * (1 - _elapsed / shake_end)
			item.sprite.position = item.position + Vector2(offset, 0)
			if item.has("flash") and is_instance_valid(item.flash):
				item.flash.position = item.sprite.position
	if not _returned and _elapsed >= _return_at:
		_returned = true
		for item in _actors:
			if is_instance_valid(item.unit):
				item.sprite.position = item.position
				_switch(item, int(item.state), Color.BLACK if item.hostile else Color.WHITE)
	queue_redraw()
	if _elapsed >= _end_at and _numbers_ready():
		_playing = false
		_cleanup()
		finished.emit()
		queue_free()

func _cleanup() -> void:
	for item in _actors:
		if is_instance_valid(item.sprite):
			item.sprite.position = item.position
	for ghost in _ghosts:
		if is_instance_valid(ghost):
			ghost.queue_free()
	_ghosts.clear()
	for unit in _breathing_paused_units:
		if is_instance_valid(unit):
			unit.set_breathing_paused(false)
	_breathing_paused_units.clear()

func _exit_tree() -> void:
	_cleanup()

func _draw() -> void:
	if not _playing:
		return
	for item in _targets:
		if not is_instance_valid(item.unit):
			continue
		var center: Vector2 = to_local(item.unit.get_art_center_global())
		if _kind == 0 and not _blocked:
			_draw_hits(center, item.angles)
		else:
			_draw_ring(center, item.particles)

func _draw_hits(center: Vector2, angles: Array) -> void:
	for i in angles.size():
		var age: float = _elapsed - i * hit_stagger
		if age < 0 or age >= 0.25 or _frames.is_empty():
			continue
		var frame: Texture2D = _frames[mini(4, int(age / 0.05))]
		var size: Vector2 = frame.get_size() * (1 + 0.2 * (_chain - 1))
		draw_set_transform(center, angles[i])
		draw_texture_rect(frame, Rect2(-size / 2, size), false)
	draw_set_transform(Vector2.ZERO)

func _draw_ring(center: Vector2, particles: Array) -> void:
	var defense: bool = _kind == 2 or _blocked
	var u: float = clampf(_elapsed / 0.5, 0, 1)
	var radius: float
	var width: float
	var alpha: float
	if defense:
		var back: float = 1 + 4 * pow(u - 1, 3) + 3 * pow(u - 1, 2)
		radius = lerpf(125, 50, back)
		width = lerpf(2, 5.5, u)
		alpha = 1 - clampf((_elapsed - 0.8625) / maxf(defense_fade, 0.001), 0, 1)
	else:
		radius = lerpf(10, 125, 1 - pow(1 - u, 3))
		width = lerpf(20, 0, u)
		alpha = clampf((1 - u) / 0.25, 0, 1)
	if alpha > 0 and width > 0:
		draw_arc(center, maxf(0.1, radius), 0, TAU, 128, Color(1, 1, 1, alpha), width, true)
	for p in particles:
		var progress: float = clampf(_elapsed / float(p.life), 0, 1)
		var eased: float = 1 - pow(1 - progress, 2 if defense else 2.5)
		var distance: float = float(p.distance) * (1 - eased if defense else eased)
		var pos: Vector2 = center + Vector2.from_angle(p.angle) * distance
		var size: float = float(p.size) * (1 - progress)
		var color := Color(1, 1, 1, clampf((1 - progress) / (0.3 if defense else 0.35), 0, 1))
		if size <= 0.3:
			continue
		if defense:
			draw_circle(pos, size * 0.5, color)
		else:
			var points := PackedVector2Array()
			for j in 8:
				points.append(pos + Vector2.from_angle(j * PI / 4 + float(p.rotation) + float(p.spin) * _elapsed) * size * (1.0 if j % 2 == 0 else 0.3))
			draw_colored_polygon(points, color)
