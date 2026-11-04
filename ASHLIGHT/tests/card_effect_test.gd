extends Node

var failures: int = 0
var checks: int = 0
var br: BattleRoot

func check(condition: bool, message: String) -> void:
	checks += 1
	if condition: print("PASS ATOM: " + message)
	else:
		failures += 1
		push_error("ATOM FAILED: " + message)

func atom(kind: String, target: String, amount: int, scaled: bool = false, hits: int = 1) -> CardEffect:
	var result := CardEffect.new()
	result.kind = kind
	result.target = target
	result.amount = amount
	result.scales_with_number = scaled
	result.hits = hits
	return result

func run_card(data: CardData, selected: String, times: int = 1) -> void:
	await br._v2_play_effect({"uid": "Rocket", "selected": selected, "cd": data}, 3, times)

func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(45.0, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))

func run() -> void:
	seed(28)
	var all := CardDatabase.new().get_all()
	check(all.size() == 24, "all 24 migrated cards load")
	check(all.all(func(cd: CardData) -> bool: return cd.validation_errors().is_empty() and cd.effects.size() == 1), "all original cards contain one valid atom")
	var sample := load("res://tests/fixtures/composite_card.tres") as CardData
	check(sample.validation_errors().is_empty() and sample.effects.size() == 4, "composite tres loads independent nested resources")
	check(sample.keyword_names() == ["伤害", "护盾", "中毒"], "keywords come from effect and status IDs")
	check(sample.effect_text(3).contains("6点【伤害】") and sample.effect_text(3).contains("3点【护盾】"), "each effect has independent scaling")
	br = (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	br.skip_battle_animations = true
	await get_tree().create_timer(0.8).timeout
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	for hc in hm._cards.duplicate():
		hm._cards.erase(hc)
		hc.queue_free()
	var rocket := br._find_unit("Rocket")
	var remy := br._find_unit("Remy")
	var irene := br._find_unit("Irene")
	for unit in br._get_units():
		unit.set_max_hp(500)
		unit.reduce_shield(10000)
	check(br.v2_targets(sample, rocket).has(remy) and not br.v2_targets(sample, rocket).has(rocket), "manual target is constrained by selected effects only")
	await run_card(sample, "Remy", 2)
	check(remy.get_current_hp() == 488 and rocket.get_shield() == 6, "Combo repeats selected damage and self shield independently")
	check(remy.get_buff_stacks("poison") == 2 and hm._hand_count() == 2, "Combo repeats status and draw effects")
	check(irene.get_shield() == 0, "self shield does not leak onto other allies")
	# Order is observable because healing is capped at max HP.
	var ordered := CardData.new()
	ordered.effects = [atom("heal", "自身", 5), atom("damage", "自身", 5)]
	rocket.reduce_shield(10000)
	rocket.damage(2)
	await run_card(ordered, "Rocket", 2)
	check(rocket.get_current_hp() == 495, "Combo runs heal→damage→heal→damage, preserving ordered list")
	var multi := CardData.new()
	multi.effects = [atom("damage", "选中敌人", 2, false, 3)]
	var hp_events: Array[float] = []
	var record := func(current: float, _max: float) -> void: hp_events.append(current)
	remy._hp_component.hp_changed.connect(record)
	await run_card(multi, "Remy")
	remy._hp_component.hp_changed.disconnect(record)
	check(hp_events.size() == 3 and remy.get_current_hp() == 482, "three hits cause three separate HP changes")
	var area := CardData.new()
	var shield_atom := atom("shield", "全体友方", 2)
	shield_atom.exclude_self = true
	area.effects = [shield_atom]
	await run_card(area, "Remy")
	check(rocket.get_shield() == 0 and irene.get_shield() == 2, "allied area effect can exclude caster")
	var timer := CardData.new()
	timer.effects = [atom("haste", "选中敌人", 2), atom("slow", "选中敌人", 5)]
	br.rules.enemies.Remy = 10
	await run_card(timer, "Remy")
	check(br.rules.enemies.Remy == 13, "countdown effects execute instead of silently doing nothing")
	# A defeated selected enemy must not suppress subsequent independent effects.
	var lethal := sample.duplicate(true) as CardData
	lethal.effects[0].amount = 1000
	var before_shield: float = rocket.get_shield()
	await run_card(lethal, "Remy", 2)
	check(remy.get_current_hp() == 0 and rocket.get_shield() == before_shield + 6, "dead target skips its effects; self effects continue across Combo")
	var bad := CardData.new()
	bad.effects = [atom("damage", "选中敌人", 1), atom("shield", "选中友方", 1)]
	check(not bad.validation_errors().is_empty(), "incompatible manual targets produce validation errors")
	bad.effects = [atom("invalid", "自身", 1)]
	check(not bad.validation_errors().is_empty(), "unknown effect cannot default to damage")
	bad.effects = [null]
	check(not bad.validation_errors().is_empty(), "null effects are rejected")
	bad.effects = [atom("apply_status", "自身", 1)]
	bad.effects[0].status_id = "invalid"
	check(not bad.validation_errors().is_empty(), "unknown status is rejected")
	# Check the exact preview component used by the Inspector, including nested edits.
	var preview := preload("res://addons/card_inspector/preview.gd").new()
	preview.data = sample.duplicate(true)
	# 模拟真实 Inspector 继承的深色主题，确保编辑器底板不会盖住卡面。
	var editor_theme := Theme.new()
	var dark_background := StyleBoxFlat.new()
	dark_background.bg_color = Color(0.12, 0.12, 0.12, 1.0)
	editor_theme.set_stylebox("normal", "RichTextLabel", dark_background)
	editor_theme.set_stylebox("focus", "RichTextLabel", dark_background)
	preview.theme = editor_theme
	add_child(preview)
	for frame in 6: await get_tree().process_frame
	check(preview.card._effect_label.get_theme_stylebox("normal") is StyleBoxEmpty, "card text background stays transparent under dark Inspector theme")
	check(preview.card._effect_label.get_theme_stylebox("focus") is StyleBoxEmpty, "card text focus background cannot cover the card artwork")
	check(preview.card._effect_label.get_theme_color("default_color") == Color.BLACK, "black effect text retains contrast against the card's own light panel")
	check(preview.card._effect_label.position.y > preview.card.type_offset.y + 14.0, "multi-effect text stays below card title and type")
	check(preview.card._effect_label.position.y + preview.card._effect_label.size.y < preview.card.desc_offset.y - 8.0, "multi-effect text fits above supplementary description")
	check(preview.card.card_name == sample.card_name and preview.card.card_effect == sample.effect_text(3), "Inspector preview binds the same CardRoot and generated text")
	preview.data.effects[0].amount = 4
	preview._refresh()
	check(preview.card.card_effect.contains("12点【伤害】"), "nested effect edits refresh preview")
	preview.number.value = 4
	check(preview.card.cost_value == 4 and preview.card.card_effect.contains("16点【伤害】"), "preview number updates diamond and scaled effects")
	preview.data.base_number = 2
	preview._refresh()
	check(preview.card.cost_value == 2, "base number edits update preview without random rolls")
	preview.data.effects[0].amount = 0
	preview._refresh()
	check(sample.effects[0].amount == 2, "preview never mutates original resource data")
	preview.queue_free()
	br.queue_free()
	await get_tree().process_frame
	print("CARD_EFFECT_TEST checks=%d failures=%d" % [checks, failures])
	get_tree().quit(1 if failures else 0)
