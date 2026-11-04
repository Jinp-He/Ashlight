extends Node

var failures: int = 0

func check(value: bool, message: String) -> void:
	if not value:
		failures += 1
		push_error("ANIMATION FAILED: " + message)
	else:
		print("PASS ANIMATION: " + message)

func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(45.0, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))

func find_card(card_name: String) -> CardData:
	for cd in CardDatabase.new().get_all():
		if cd.card_name == card_name:
			return cd
	return null

## pos 为 HandManager 本地坐标，换算成屏幕坐标后发出点击。
func click(hm: HandManager, pos: Vector2) -> void:
	var event := InputEventMouseButton.new()
	event.button_index = MOUSE_BUTTON_LEFT
	event.pressed = true
	event.position = hm.get_global_transform_with_canvas() * pos
	hm._input(event)

func wait_idle(br: BattleRoot) -> void:
	var frames: int = 0
	while br.clock_busy and frames < 12000:
		await get_tree().process_frame
		frames += 1

func run() -> void:
	Engine.time_scale = 8
	seed(3)
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	await get_tree().create_timer(1).timeout
	var remy := br._find_unit("Remy")
	remy.set_max_hp(500)
	for uid in br.rules.enemies:
		br.rules.enemies[uid] = 100
	for hc in hm._cards.duplicate():
		hm._cards.erase(hc)
		hc.queue_free()
	br.rules.place_player_card(0, 3, "", "测试")
	var legal := hm._spawn_card_for_role(find_card("重拳"), "Rocket")
	legal.cost_color = 0
	legal.base_number = 3
	var illegal := hm._spawn_card_for_role(find_card("速射"), "Rocket")
	illegal.cost_color = 1
	illegal.base_number = 2
	hm._cards.append(illegal)
	hm._cards.append(legal)
	hm.refresh_numbers()
	hm.layout(false, false)
	check(legal.card.cost_value == 3 and legal.card._cost_label.text == "3", "card diamond shows current number")
	check(illegal.position.y > legal.position.y, "unplayable card sits lower than playable card")
	# Exercise actual click handlers using their event positions, without cached hover.
	# 两张牌有重叠：点不可出牌露在左侧、未被遮挡的部分。
	click(hm, illegal.home_pos + Vector2(-40, 0))
	check(hm.get_selected() == null and hm.get_skill_card() == illegal, "clicking an unplayable card picks it as a skill target, not a play")
	check(br._find_unit("Remy").is_dimmed() and not br._find_unit("Rocket").is_dimmed(), "only allies with a usable skill stay lit")
	var unit_click := InputEventMouseButton.new()
	unit_click.button_index = MOUSE_BUTTON_LEFT
	unit_click.pressed = true
	var rocket := br._find_unit("Rocket")
	unit_click.position = br.get_canvas_transform() * (rocket.global_position + rocket.activation_center)
	br._input(unit_click)
	check(hm.get_skill_card() == null and illegal.base_number == 3 and br.rules.skill_cooldowns.Rocket == 3, "clicking Rocket applies +1 to the card (2 → 3)")
	illegal.base_number = 2
	hm.refresh_numbers()
	br.rules.skill_cooldowns.Rocket = 0
	click(hm, legal.home_pos)
	check(hm.get_selected() == legal, "click selects a playable card without requiring previous hover frame")
	hm.clear_selection()
	await get_tree().create_timer(0.4).timeout
	hm.select_card(legal)
	var hp: float = remy.get_current_hp()
	hm.notify_used_on_unit(remy)
	check(hm._hand_locked and br.clock_busy, "input stays locked while original cutscene plays")
	await wait_idle(br)
	check(not br.clock_busy and not hm._hand_locked, "cutscene and discard finish without softlock")
	check(remy.get_current_hp() == hp - 6, "visual red 3 attack resolves as 6 damage")
	check(br.rules.ticks == 1 and br.rules.combo == 2, "animation time does not add extra countdown ticks")
	check(br._top_number.text == "3" and br._combo_label.text == "COMBO ×2", "HUD shows pile top and combo")
	check(illegal.card._cost_label.text == "2", "hand card number stays as drawn after the play")
	# Enemy attack: card lands on the pile top and its cutscene resolves immediately.
	br.rules.enemies.Remy = 1
	await br.draw_action()
	check(br.rules.top.source == "enemy" and not br.clock_busy and not hm._hand_locked, "enemy attack cutscene finishes and returns control")
	for unit in br._get_units():
		if unit.unit_faction == "enemy" and unit.get_current_hp() > 0:
			unit.damage(10000)
			unit.die()
	await get_tree().create_timer(0.4).timeout
	check(br._battle_ended and hm._hand_locked, "victory stops input")
	var tick: int = br.rules.ticks
	await br.draw_action()
	check(br.rules.ticks == tick, "draw is disabled after victory")
	print("BATTLE_V2_ANIMATION_TEST failures=%d" % failures)
	await get_tree().create_timer(2).timeout
	get_tree().quit(1 if failures else 0)
