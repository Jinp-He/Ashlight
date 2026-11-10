extends Node

var failures := 0

func check(ok: bool, message: String) -> void:
	print("PASS: " if ok else "FAIL: ", message)
	if not ok: failures += 1

func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(12.0).timeout.connect(func() -> void: get_tree().quit(2))

func run() -> void:
	var battle := preload("res://battle/Battle.tscn").instantiate() as BattleRoot
	add_child(battle)
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	await get_tree().create_timer(2).timeout
	var support := CardData.new()
	var shield := CardEffect.new()
	shield.kind = "shield"
	shield.target = "全体友方"
	support.effects = [shield]
	var owner := battle._find_unit("Zhouzhou")
	var card := hand._spawn_card_for_role(support, owner.unit_id)
	card.cost_color = int(battle.rules.top.color)
	hand._cards.append(card)
	hand.select_card(card)
	for unit in battle._get_units():
		check(unit.is_dimmed() == (unit.unit_faction == "enemy"), unit.unit_id + " has correct target highlight")
	check(battle._effect_targets(shield, owner).size() == 1, "shared shield settles once")
	hand.clear_selection()
	var blue := support.duplicate() as CardData
	blue.uno_color = 1
	hand.deck.draw_pile.append(blue)
	var enemy := battle._find_unit("Remy")
	var before := hand._cards.size()
	var hp := battle._team.get_current_hp()
	battle._v2_intents.Remy = {"uid": "Remy", "name": "测试攻击", "stat": "damage", "value": 7, "color": 0, "number": 3, "target": owner.unit_id}
	battle.rules.counters.append({"uid": owner.unit_id, "name": "测试反制", "value": 0, "blue_flashback": 1})
	battle._enemy_play(enemy.unit_id)
	var fx := battle.get_node_or_null("演出组2") as CutsceneGroup2
	check(is_instance_valid(fx) and fx._playing and fx._caster == owner, "non-damage counter plays a cast animation")
	var layer := battle.get_node("TriggerLayer")
	var label := layer.get_child(-1).get_node("Content/Text") as Label
	check(label.text.contains("测试反制") and label.text.contains("测试攻击") and label.text.contains("1 张蓝色闪回牌"), "prompt identifies counter, interrupted action and reward")
	await get_tree().create_timer(2).timeout
	check(battle._team.get_current_hp() == hp, "counter cancels enemy damage")
	check(hand._cards.size() == before + 1, "counter grants one flashback")
	var reward := hand._cards.back() as HandCard
	check(reward.is_flashback and not reward.flyin_lock and reward.card.get_parent() == reward, "flashback returns to hand after flip")
	var correct_layers := true
	for i in hand._cards.size():
		var current := hand._cards[i]
		correct_layers = correct_layers and current.base_z == 10 + hand._cards.size() - 1 - i and current.z_index == (1000 if current == hand._hovered else current.base_z)
	check(correct_layers, "new and old cards share the same hand stacking order")
	print("COUNTER_PRESENTATION_TEST failures=", failures)
	get_tree().quit(1 if failures else 0)
