extends Node
var failures: int = 0

func check(ok: bool, message: String) -> void:
	print("PASS GROUP2: " if ok else "FAIL GROUP2: ", message)
	if not ok:
		failures += 1

func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(40, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))

func run() -> void:
	Engine.time_scale = 8
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().create_timer(1).timeout
	var ally := br._find_unit("Rocket")
	var target := br._find_unit("Remy")
	var other := br._find_unit("Irene")
	target.set_art_state(UnitRoot.UnitArtState.CHARGE)
	var group := CutsceneGroup2.new()
	br.add_child(group)
	group.play({"caster": ally, "targets": [target], "stat": "bleed", "kind": 1, "value": 2, "hits": 1})
	check(target.current_art_state == UnitRoot.UnitArtState.HIT, "hostile status enters HIT")
	check(ally.current_art_state == UnitRoot.UnitArtState.CAST, "status caster enters CAST")
	await group.finished
	check(target.current_art_state == UnitRoot.UnitArtState.CHARGE, "target returns to previous CHARGE")
	check(ally.current_art_state == UnitRoot.UnitArtState.IDLE, "caster returns to previous IDLE")
	group = CutsceneGroup2.new()
	br.add_child(group)
	group.play({"caster": ally, "targets": [other], "stat": "shield", "kind": 2, "value": 3, "hits": 1})
	check(other.current_art_state == UnitRoot.UnitArtState.IDLE, "friendly defense does not enter HIT")
	check(group._end_at >= 0.8625 + 0.2, "defense completion includes particle tail and 0.2s fade")
	await get_tree().create_timer(0.9).timeout
	check(is_instance_valid(group) and group._playing, "defense remains active during final fade")
	await group.finished
	for shield in [2, 1]:
		ally.reduce_shield(ally.get_shield())
		ally.gain_shield(shield)
		var team_hp := ally.get_current_hp()
		br._enemy_attack(target, {"value": 2, "target": "missing"})
		group = br.get_node("演出组2") as CutsceneGroup2
		check(group._targets.size() == 3 and group._targets.all(
			func(item: Dictionary) -> bool: return item.unit.current_art_state == UnitRoot.UnitArtState.HIT),
			"all allies enter HIT in the same frame")
		check(group._blocked == (shield == 2), "full absorption selects block feedback")
		check(group._number_nodes.size() == 1 and group._number_nodes[0].popup_anchor.is_equal_approx(
			br.to_local(br.get_node("HUD/TeamHUD/TeamDamageAnchor").global_position) + Vector2(0, 50)), "one number at team anchor")
		await group.finished
		check(ally.get_shield() == 0 and ally.get_current_hp() == team_hp - (2 - shield),
			"team shield and HP change once")
		await get_tree().process_frame
	ally.heal(1)
	for uid in br.rules.enemies:
		br.rules.enemies[uid] = 1
	for uid in ["Remy", "Wangg", "Grey"]:
		br._find_unit(uid).set_max_hp(500)
	br._v2_set_busy(true)
	var sequence: Array[String] = []
	var observed: Array[int] = []
	var flags := {"overlap": false, "old_group": false}
	var sampler := func() -> void:
		var active: int = 0
		for child in br.get_children():
			if child is CutsceneGroup2 and child._playing:
				active += 1
				if not observed.has(child.get_instance_id()):
					observed.append(child.get_instance_id())
					sequence.append(child._caster.unit_id)
			if child is CutsceneGroup:
				flags.old_group = true
		if active > 1:
			flags.overlap = true
	get_tree().process_frame.connect(sampler)
	var done: Array[bool] = [false]
	finish_actions(br, done)
	await get_tree().process_frame
	check(br.clock_busy, "queue holds action lock")
	check(not br.use_skill("Rocket", null), "skills rejected while busy")
	while not done[0]:
		await get_tree().process_frame
	get_tree().process_frame.disconnect(sampler)
	check(sequence == ["Remy", "Wangg", "Grey"], "ready enemies play from left to right")
	check(not flags.overlap, "enemy groups never overlap")
	check(not flags.old_group, "legacy insert group is disconnected")
	await br._v2_finish()
	check(not br.clock_busy, "queue releases input after completion")
	# Resource-based composite cards must preserve the new presentation while resolving each atom.
	var composite := load("res://tests/fixtures/composite_card.tres") as CardData
	var before_hp: float = target.get_current_hp()
	var before_shield: float = ally.get_shield()
	var before_poison: int = target.get_buff_stacks("poison")
	var presented: Array[int] = []
	var chains: Array[int] = []
	var seen_groups: Array[int] = []
	var capture := func() -> void:
		for child in br.get_children():
			if child is CutsceneGroup2 and child._playing and not seen_groups.has(child.get_instance_id()):
				seen_groups.append(child.get_instance_id())
				presented.append(child._kind)
				chains.append(child._chain)
	get_tree().process_frame.connect(capture)
	await br._v2_play_effect({"uid": "Rocket", "selected": "Remy", "cd": composite}, 3, 2)
	get_tree().process_frame.disconnect(capture)
	check(presented.size() == 4 and presented.all(func(kind: int) -> bool: return kind == composite.card_type), "each resource atom uses card presentation type exactly once")
	check(chains == [2, 2, 2, 2], "all atom animations receive Combo scale")
	check(target.get_current_hp() == before_hp - 12 and ally.get_shield() == before_shield + 6, "animated composite resolves selected damage and self defense")
	check(target.get_buff_stacks("poison") == before_poison + 2, "animated composite preserves status across Combo repetitions")
	br.queue_free()
	await get_tree().process_frame
	print("BATTLE_GROUP2_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)

func finish_actions(br: BattleRoot, done: Array[bool]) -> void:
	await br._after_action()
	done[0] = true
