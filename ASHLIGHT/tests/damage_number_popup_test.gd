extends Node
var failures: int = 0
func check(ok: bool, message: String) -> void:
	print("PASS POPUP: " if ok else "FAIL POPUP: ", message)
	if not ok:
		failures += 1
func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(30, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))
func texts(number: DamageNumber) -> Array[String]:
	var result: Array[String] = []
	for child in number.get_children():
		if child is Label:
			result.append(child.text)
	return result
func run() -> void:
	Engine.time_scale = 2
	seed(20261103)
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().create_timer(1).timeout
	var caster := br._find_unit("Rocket")
	var target := br._find_unit("Remy")
	target.gain_shield(8)
	br._v2_set_busy(true)
	var group := CutsceneGroup2.new()
	var timing: float = group.duration_multiplier
	br.add_child(group)
	group.play({"caster": caster, "targets": [target], "stat": "damage", "kind": 0, "value": 5, "hits": 3})
	var numbers := group._number_nodes.duplicate()
	check(numbers.size() == 3, "one number group per damage segment")
	check(texts(numbers[0]) == ["无效 -5"], "fully blocked hit shows only invalid amount")
	check(texts(numbers[1]) == ["-2", "无效 -3"], "partial hit bundles actual damage and invalid amount")
	check(texts(numbers[2]) == ["-5"], "shield preview is consumed across segments")
	var used: Array[Vector2] = []
	var area := Rect2(target.activation_center - target.activation_size * 0.5, Vector2(160, 170))
	for number in numbers:
		var point: Vector2 = number.get_meta("target_anchor")
		check(area.has_point(point), "anchor lies inside top 170px of activation box")
		for earlier in used:
			check(point.distance_to(earlier) >= 30, "segment anchors remain at least 30px apart")
		used.append(point)
		check(number.position.is_equal_approx(number.popup_anchor + Vector2(0, 15)), "popup begins 15px below anchor")
	check(numbers[0].visible and not numbers[1].visible, "later segments wait for their stagger")
	var peak_probe := DamageNumber.new()
	br.add_child(peak_probe)
	peak_probe.play_popup(Vector2.ZERO)
	check(peak_probe.popup_anchor.is_equal_approx(Vector2(0, 50)), "popup anchor shifts down 50px")
	peak_probe._popup_tween.pause()
	peak_probe._popup_tween.custom_step(peak_probe.popup_rise_time * peak_probe.duration_multiplier)
	check(peak_probe.scale.is_equal_approx(Vector2.ONE * 1.44), "rise reaches 20 percent overshoot above the enlarged baseline")
	peak_probe._popup_tween.kill()
	peak_probe.remove_from_group("blocking_damage_numbers")
	peak_probe.queue_free()
	await get_tree().create_timer(0.45 * numbers[0].duration_multiplier).timeout
	check(numbers[0].scale.is_equal_approx(Vector2.ONE * 1.2), "settle restores the enlarged baseline")
	check(numbers[0].position.is_equal_approx(numbers[0].popup_anchor), "rise ends at selected anchor")
	check(numbers[0].popup_settled and not numbers[0].is_in_group("blocking_damage_numbers"), "number hold begins after settle without blocking progression")
	check(is_equal_approx(numbers[0].popup_hold_time, 1.0), "number hold lasts one second")
	await group.finished
	check(is_instance_valid(numbers[2]) and numbers[2].popup_settled and not numbers[2].popup_fading, "group finishes while last segment still holds independently")
	await br._v2_finish()
	check(not br.clock_busy and is_instance_valid(numbers[2]) and not numbers[2].popup_fading, "input unlocks during number hold before fade starts")
	check(not numbers[2].is_in_group("blocking_damage_numbers"), "number hold and fade do not belong to blocking wait")
	await numbers[2].hold_finished
	check(numbers[2].popup_fading, "number continues into independent fade after its hold")
	await get_tree().create_timer(0.25 * timing).timeout
	check(not is_instance_valid(numbers[2]), "fade frees number group after 0.2s")
	var blocked_heal := DamageNumber.new()
	br.add_child(blocked_heal)
	blocked_heal.spawn({"type": "heal", "value": 0, "invalid_amount": 7})
	check(texts(blocked_heal) == ["无效 -7"], "fully blocked heal has no label or zero main number")
	blocked_heal.queue_free()
	var used_many: Array[Vector2] = []
	for i in 20:
		var point := DamageNumber.pick_anchor(target, used_many)
		check(area.has_point(point) and DamageNumber._anchor_clear(point, used_many), "dense sampling preserves bounds and spacing")
		used_many.append(point)
	br.queue_free()
	await get_tree().process_frame
	print("DAMAGE_NUMBER_POPUP_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)

