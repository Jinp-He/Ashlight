extends Node
var failures: int = 0
var clock_time: float = 0.0
func _process(delta: float) -> void:
	clock_time += delta
func check(ok: bool, message: String) -> void:
	print("PASS TIMING: " if ok else "FAIL TIMING: ", message)
	if not ok:
		failures += 1
func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(30, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))
func run() -> void:
	Engine.time_scale = 2
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().create_timer(1).timeout
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	var caster := br._find_unit("Rocket")
	var target := br._find_unit("Remy")
	br._v2_set_busy(true)
	var group := CutsceneGroup2.new()
	br.add_child(group)
	var start: float = clock_time
	group.play({"caster": caster, "targets": [target], "stat": "bleed", "kind": 1, "value": 1, "hits": 1})
	check(is_equal_approx(group.duration_multiplier, 1.0) and is_equal_approx(group.hit_hold, 0.5) and is_equal_approx(group.friendly_wait, 0.5), "cutscene restores original duration and posture holds")
	var number := DamageNumber.new()
	check(is_equal_approx(number.blocking_duration(), 0.4), "only 0.4-second number rise and settle block")
	check(is_equal_approx(number.popup_fade_time * number.duration_multiplier, 0.2), "nonblocking fade lasts 0.2 seconds")
	number.free()
	hm._draw_common_pool(1)
	var drawn: HandCard = hm._cards.back()
	var origin: Vector2 = drawn.position
	await get_tree().create_timer(0.09).timeout
	check(drawn.flyin_lock, "fly-in lock lasts through actual flight")
	check(drawn.position.distance_to(origin) > 1, "draw animation advances during cutscene input lock")
	var middle: Vector2 = drawn.position
	hm.layout(false)
	check(drawn.position.is_equal_approx(middle), "HUD refresh does not snap or stop flying card")
	hm.select_card(drawn)
	check(hm.get_selected() == null, "card selection remains blocked")
	var count: int = hm._hand_count()
	await br.draw_action()
	check(hm._hand_count() == count, "draw-button action remains blocked")
	check(not br.use_skill("Rocket", drawn), "ally skills remain blocked")
	await get_tree().create_timer(0.35).timeout
	check(not drawn.flyin_lock, "draw animation completes and releases its flight lock")
	check(drawn.position.distance_to(middle) > 1, "flight keeps moving after refresh")
	hm._cards.erase(drawn)
	var discard_start: Vector2 = drawn.position
	hm.recycle_card(drawn)
	await get_tree().create_timer(0.1).timeout
	check(is_instance_valid(drawn) and drawn.position.distance_to(discard_start) > 1, "discard animation advances during input lock")
	await get_tree().create_timer(0.4).timeout
	check(not is_instance_valid(drawn), "discard finishes normally before cutscene ends")
	check(group._playing and br.clock_busy, "cutscene and input lock remain active during card effects")
	await group.finished
	check(clock_time - start >= 1.15 - 0.04 and clock_time - start < 1.3, "status animation restores original particle-tail duration")
	await br._v2_finish()
	check(not br.clock_busy, "input unlocks when cutscene completes")
	br.queue_free()
	await get_tree().process_frame
	print("BATTLE_TIMING_HAND_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)
