extends Node
var failures: int = 0
func check(ok: bool, message: String) -> void:
	print("PASS ACTION SYNC: " if ok else "FAIL ACTION SYNC: ", message)
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
	for uid in br.rules.enemies:
		br.rules.enemies[uid] = 100
	br._find_unit("Remy").set_max_hp(500)
	br.rules.place_player_card(0, 3, "", "同步测试")
	br._v2_refresh_hud()
	await get_tree().create_timer(0.4).timeout
	var data: CardData
	for card in CardDatabase.new().get_all():
		if card.card_name == "重拳":
			data = card
	var hand_card := hm._spawn_card_for_role(data, "Rocket")
	hand_card.cost_color = 1
	hand_card.base_number = 3
	var payload := br.v2_payload(hand_card, br._find_unit("Remy"))
	br.play_card(payload)
	check(br.clock_busy, "player cutscene is active")
	check(br._top_flame._transition_active and br._top_flame.target_tint.is_equal_approx(CardRoot.COST_COLORS[1]), "player color dissolve starts with cutscene")
	check(br._top_number.text == "3", "pile number updates with player action")
	check(br.rules.top.color == 1, "pile rule state updates at accepted player action")
	while br.clock_busy:
		await get_tree().process_frame
	hand_card.queue_free()
	var intent: Dictionary = br._v2_intents.Remy.duplicate()
	intent.color = 2
	intent.number = 4
	intent.value = 1
	br._v2_intents.Remy = intent
	br._v2_set_busy(true)
	var done: Array[bool] = [false]
	play_enemy(br, done)
	check(br._top_flame._transition_active and br._top_flame.target_tint.is_equal_approx(Color("#d09633")), "enemy color dissolve starts with intent animation and uses updated yellow")
	check(br._top_number.text == "4", "pile number updates before enemy animation finishes")
	check(br._top_number.get_theme_constant("outline_size") == 0, "central pile number has no outline")
	check(CardRoot.COST_COLORS[2].to_html(false) == "d09633", "card palette yellow is exactly d09633")
	while not done[0]:
		await get_tree().process_frame
	br.queue_free()
	await get_tree().process_frame
	print("PILE_ACTION_SYNC_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)
func play_enemy(br: BattleRoot, done: Array[bool]) -> void:
	await br._enemy_play("Remy")
	await br._v2_finish()
	done[0] = true
