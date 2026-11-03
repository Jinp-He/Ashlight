extends Node
var failures: int = 0
func check(ok: bool, message: String) -> void:
	print("PASS BREATH: " if ok else "FAIL BREATH: ", message)
	if not ok:
		failures += 1
func _ready() -> void:
	call_deferred("run")
func run() -> void:
	Engine.time_scale = 4
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().create_timer(1).timeout
	var unit := br._find_unit("Rocket")
	var other := br._find_unit("Irene")
	var hp_position: Vector2 = unit._hp_component.global_position
	var buff_position: Vector2 = unit._buff_component.global_position
	var activation: Vector2 = unit._activation_area.global_position
	var expected_foot: Vector2 = unit._body.to_global(Vector2(0, unit._breath_foot_y))
	var old_scale: Vector2 = unit._breath_root.scale
	await get_tree().create_timer(0.7).timeout
	check(not unit._breath_root.scale.is_equal_approx(old_scale), "idle artwork breathes over time")
	check(unit._breath_root.scale.y >= 1 and unit._breath_root.scale.y <= 1.01501, "height stays within agreed 1.5 percent stretch")
	check(unit._breath_root.to_global(Vector2(0, unit._breath_foot_y)).is_equal_approx(expected_foot), "visible artwork foot remains fixed")
	check(unit._hp_component.global_position == hp_position and unit._buff_component.global_position == buff_position, "HUD never inherits breathing transform")
	check(unit._activation_area.global_position == activation, "activation box remains still")
	check(not is_equal_approx(unit._breath_phase, other._breath_phase), "units use different breathing phases")
	unit.set_art_state(UnitRoot.UnitArtState.CHARGE)
	await get_tree().create_timer(0.4).timeout
	check(unit._breath_root.scale.y > 1, "charge posture also breathes")
	var enemy := br._find_unit("Remy")
	var group := CutsceneGroup2.new()
	br.add_child(group)
	group.play({"caster": unit, "targets": [enemy], "stat": "damage", "kind": 0, "value": 1, "hits": 1})
	check(unit._breath_root.scale == Vector2.ONE and enemy._breath_root.scale == Vector2.ONE, "cutscene pauses breathing before snapshots")
	await get_tree().create_timer(group._return_at * group.duration_multiplier + 0.06).timeout
	check(unit.current_art_state == UnitRoot.UnitArtState.CHARGE and unit._breathing_paused, "return transition keeps breathing paused")
	await group.finished
	check(not unit._breathing_paused and not enemy._breathing_paused, "completed cutscene releases breathing pause")
	await get_tree().create_timer(0.5).timeout
	check(unit._breath_root.scale.y > 1, "breathing resumes after return to charge")
	unit.die()
	await get_tree().process_frame
	check(unit._breath_root.scale == Vector2.ONE, "death stops breathing")
	br.queue_free()
	await get_tree().process_frame
	print("UNIT_BREATHING_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)
