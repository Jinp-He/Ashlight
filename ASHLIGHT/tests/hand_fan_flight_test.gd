extends Node
var failures: int = 0
func check(ok: bool, message: String) -> void:
	print("PASS HAND FAN: " if ok else "FAIL HAND FAN: ", message)
	if not ok:
		failures += 1
func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(30, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))
func run() -> void:
	Engine.time_scale = 1
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().create_timer(1).timeout
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hm.set_hand_locked(true)
	for hc in hm._cards:
		hc.cost_color = int(br.rules.top.color)
	hm._hovered = null
	hm.layout(false)
	var cards: Array[HandCard] = hm._cards.duplicate()
	cards.sort_custom(func(a: HandCard, b: HandCard) -> bool: return a.home_pos.x < b.home_pos.x)
	check(cards.size() == 5, "opening hand still contains five cards")
	check(is_equal_approx(cards[0].home_pos.y - cards[2].home_pos.y, 25.0), "outer card is 25px below center")
	check(is_equal_approx(cards[0].rotation_degrees, -3.0) and is_equal_approx(cards[4].rotation_degrees, 3.0), "fan edges use minus and plus three degrees")
	check(is_equal_approx(cards[2].rotation, 0.0) and is_equal_approx(cards[2].home_pos.y, hm.hand_y), "center stays at original hand anchor")
	var left: HandCard = cards[0]
	var corner := Vector2(-left.get_native_width(), -left.get_native_height()) * hm.hand_scale * 0.5 + Vector2(2, 2)
	check(left.is_point_inside(hm.to_global(left.home_pos + corner.rotated(left.home_rotation))), "tilted corner stays clickable")
	hm._set_hover(left)
	await get_tree().create_timer(0.4).timeout
	check(absf(left.rotation) < 0.001 and is_equal_approx(left.position.y, left.home_pos.y - hm.hover_lift), "hover lifts and straightens card")
	hm._set_hover(null)
	await get_tree().create_timer(0.3).timeout
	check(is_equal_approx(left.rotation, left.home_rotation) and left.position.is_equal_approx(left.home_pos), "hover exit restores fan position and tilt")
	var start: Vector2 = left.position
	var destination := hm.to_local(hm._pile_center(&"discard_pile"))
	hm._cards.erase(left)
	hm.recycle_card(left, 0.1, true)
	await get_tree().create_timer(0.05).timeout
	check(left.position.is_equal_approx(start), "stagger delay is retained")
	await get_tree().create_timer(0.17).timeout
	var before: Vector2 = left.position
	await get_tree().create_timer(0.02).timeout
	var velocity: Vector2 = left.position - before
	check(Vector2.UP.rotated(left.rotation).dot(velocity.normalized()) > 0.98, "card top follows flight direction")
	var fraction: float = (left.position.x - start.x) / (destination.x - start.x)
	var line_y: float = lerpf(start.y, destination.y, fraction)
	check(left.position.y < line_y - 20, "discard flight arches above direct line")
	check(left.scale.x < hm.hand_scale and left.modulate.a > 0, "flight shrinks and fades in normally")
	check(hm._hand_locked, "discard flight advances without unlocking input")
	await get_tree().create_timer(0.4).timeout
	check(not is_instance_valid(left), "card is removed after completing flight")
	hm.layout(false)
	check(hm._hand_count() == 4, "remaining hand reflows normally")
	br.queue_free()
	await get_tree().process_frame
	print("HAND_FAN_FLIGHT_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)
