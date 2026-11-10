extends Node

var failures: int = 0
var checks: int = 0
var br: BattleRoot
var hm: HandManager

func _ready() -> void:
	call_deferred("run")
	get_tree().create_timer(45.0, true, false, true).timeout.connect(func() -> void: get_tree().quit(2))

func check(condition: bool, message: String) -> void:
	checks += 1
	if not condition:
		failures += 1
		push_error("TEST FAILED: " + message)
	else:
		print("PASS: " + message)

func card_named(card_name: String) -> CardData:
	for card in CardDatabase.new().get_all():
		if card.card_name == card_name:
			return card
	return null

## 在手牌里放一张指定颜色 / 基础点数的牌。
func make_card(card_name: String, color: int, base: int) -> HandCard:
	var cd := card_named(card_name)
	var hc := hm._spawn_card_for_role(cd, cd.belong_to)
	hc.cost_color = color
	hc.base_number = base
	hm._cards.append(hc)
	return hc

func play(hc: HandCard, target: String) -> void:
	var payload := br.v2_payload(hc, br._find_unit(target))
	hm._cards.erase(hc)
	hc.queue_free()
	await br.play_card(payload)

func clear_hand() -> void:
	for hc in hm._cards.duplicate():
		hm._cards.erase(hc)
		hm.deck.discard_card(hc.owner_group, hc.data)
		hc.queue_free()

func set_top(color: int, number: int) -> void:
	br.rules.place_player_card(color, number, "", "测试")

func test_monster_resources() -> void:
	var expected: Dictionary = {"Remy": [30.0, 3, 2], "Wangg": [30.0, 4, 3], "Grey": [40.0, 5, 3], "Mafe": [99.0, 3, 3]}
	for uid in expected:
		var data := MonsterDatabase.get_data(uid)
		var values: Array = expected[uid]
		check(data != null and data.validation_errors().is_empty() and data.max_hp == values[0] and data.countdown == values[1] and data.intents.size() == values[2], uid + " monster tres preserves HP, countdown and intent count")
	var grey_config := MonsterDatabase.get_data("Grey")
	var mafe_config := MonsterDatabase.get_data("Mafe")
	check(grey_config.intents[1] == mafe_config.intents[1], "different monsters can share the same intent resource")
	check(grey_config.intent_at(0).intent_name == "重锤" and grey_config.intent_at(1).intent_name == "粉碎" and grey_config.intent_at(2).stat == "shield" and grey_config.intent_at(3) == grey_config.intent_at(0), "intent array cycles in resource order")
	var smash := grey_config.intent_at(1)
	check(smash.value == 16 and smash.color == 0 and smash.number == 7 and smash.delay == 1, "smash tres retains its attack and recovery delay")
	var first := smash.make_payload("Grey", "Irene")
	var second := smash.make_payload("Mafe", "Rocket")
	first.value = 999
	first.target = "Zhouzhou"
	check(second.value == 16 and second.target == "Rocket" and second.uid == "Mafe" and smash.value == 16, "runtime intent snapshots never mutate shared resources or each other")
	var invalid_monster := grey_config.duplicate() as MonsterData
	invalid_monster.intents = []
	check(not invalid_monster.validation_errors().is_empty(), "empty monster intent cycle is rejected")
	invalid_monster.intents = [null]
	check(not invalid_monster.validation_errors().is_empty(), "null intent slots are rejected")
	invalid_monster = grey_config.duplicate() as MonsterData
	invalid_monster.countdown = 0
	check(not invalid_monster.validation_errors().is_empty(), "non-positive monster countdown is rejected")
	invalid_monster.countdown = 5
	invalid_monster.max_hp = 0
	check(not invalid_monster.validation_errors().is_empty(), "non-positive monster HP is rejected")
	var invalid_intent := smash.duplicate() as EnemyIntentData
	invalid_intent.stat = "invalid"
	check(not invalid_intent.validation_errors().is_empty(), "unknown intent kind cannot silently become a shield")
	invalid_intent.stat = "damage"
	invalid_intent.color = 4
	check(not invalid_intent.validation_errors().is_empty(), "invalid intent color is rejected")
	invalid_intent.color = 0
	invalid_intent.delay = -1
	check(not invalid_intent.validation_errors().is_empty(), "negative intent recovery delay is rejected")

func run() -> void:
	seed(22)
	test_monster_resources()
	# 初始卡组是独立资源；牌库里的卡必须显式列入才能成为初始牌。
	var starting_decks: Array[StartingDeckData] = []
	for role in ["zhouzhou", "irene", "rocket"]:
		var config := load("res://data/starting_decks/%s.tres" % role) as StartingDeckData
		check(config != null and config.validation_errors().is_empty() and config.cards.size() == 5, role + " starter tres contains 5 valid cards")
		starting_decks.append(config)
	var solo := DeckManager.new()
	check(solo.init_decks(["Irene", "Irene"]) and solo.draw_pile.size() == 5 and not solo.has_role("Rocket"), "only present roles contribute their starting deck; duplicate roles do not double cards")
	var custom := starting_decks[0].duplicate() as StartingDeckData
	custom.cards = [starting_decks[0].cards[0], starting_decks[0].cards[0]]
	var custom_deck := DeckManager.new()
	check(custom_deck.init_from_starting_decks([custom]) and custom_deck.draw_pile.size() == 2, "custom starter uses explicit list instead of all role cards")
	var copies := custom_deck.draw_common(2)
	check(copies.size() == 2 and copies[0] == copies[1], "repeated CardData references produce multiple drawable copies")
	custom_deck.discard_card("Zhouzhou", copies[0])
	check(custom.cards.size() == 2 and starting_decks[0].cards.size() == 5, "drawing and discarding never mutate starter resource arrays")
	check(custom_deck.init_decks(["Rocket"]) and custom_deck.draw_pile.size() == 5 and custom_deck.discard_pile.is_empty() and not custom_deck.has_role("Zhouzhou"), "reinitializing replaces roles and clears old discard pile")
	var invalid := StartingDeckData.new()
	check(not invalid.validation_errors().is_empty(), "empty starter config is rejected")
	invalid.cards = [null]
	check(not invalid.validation_errors().is_empty(), "empty card slots are rejected")
	invalid.cards = [starting_decks[1].cards[0]]
	check(not invalid.validation_errors().is_empty(), "cards belonging to a different role are rejected")
	var scene := load("res://battle/Battle.tscn") as PackedScene
	br = scene.instantiate() as BattleRoot
	get_tree().root.add_child(br)
	hm = get_tree().get_first_node_in_group("hand_manager") as HandManager
	br.skip_battle_animations = true
	await get_tree().create_timer(0.8).timeout
	var rules := br.rules
	check(hm._hand_count() == 5, "opening is exactly 5 shared cards")
	check(hm.deck.draw_pile.size() == 9 and hm.deck.discard_pile.size() == 1, "15-card deck: 5 drawn, 1 flipped as starting top")
	check(not rules.top.is_empty() and rules.top.source == "player", "starting top is a flipped player card")
	check(rules.combo == 1, "combo starts at ×1")
	var initial := rules.enemies.duplicate()
	await get_tree().create_timer(0.2).timeout
	check(rules.ticks == 0 and rules.enemies == initial, "idle does not advance countdowns")
	check(initial.Remy == 3 and initial.Wangg == 4 and initial.Grey == 5, "enemy countdowns come from MonsterDatabase")
	check(br._find_unit("Remy").get_current_hp() == 30 and br._find_unit("Grey").get_current_hp() == 40, "battle HP initializes from monster resources")
	var grey_unit := br._find_unit("Grey")
	var grey_data := MonsterDatabase.get_data("Grey")
	grey_unit.ai_memory["v2_actions"] = 1
	br._v2_next_intent(grey_unit)
	var smash_snapshot: Dictionary = br._v2_intents.Grey
	check(smash_snapshot.name == "粉碎" and smash_snapshot.value == 16 and smash_snapshot.delay == 1 and br.resolve_targets(grey_unit, "敌方").has(br._find_unit(smash_snapshot.target)), "resource intent is previewed with a locked living opponent")
	br._end_enemy_action(smash_snapshot)
	check(rules.enemies.Grey == 6 and br._v2_intents.Grey.stat == "shield" and br._v2_intents.Grey.target == "Grey", "finishing smash adds recovery delay and previews next self-shield intent")
	br._end_enemy_action(br._v2_intents.Grey)
	check(rules.enemies.Grey == 5 and br._v2_intents.Grey.name == "重锤", "finishing shield restores base countdown and wraps resource intent cycle")
	check(grey_data.countdown == 5 and grey_data.intents[1].delay == 1, "battle countdowns and cycle progress do not modify monster or intent resources")
	grey_unit.ai_memory["v2_actions"] = 0
	br._v2_next_intent(grey_unit)


	# --- 纯规则：接牌、Combo ---
	var r := ChainRules.new()
	r.place_player_card(0, 3, "", "x")
	check(r.can_play(0, 1) and r.can_play(1, 3) and not r.can_play(1, 2), "legal = same color or same current number")
	r.combo_up()
	check(not r.can_play(2, 2) and r.can_play(2, 3), "combo does not change card numbers")
	r.combo_up(); r.combo_up(); r.combo_up()
	check(r.combo == ChainRules.MAX_COMBO, "combo caps at ×4")
	r.combo_down()
	check(r.combo == 3, "damage penalty lowers combo by one")
	r.combo = 1
	r.combo_down()
	check(r.combo == 1, "damage penalty never reduces combo below ×1")
	var cd := card_named("重拳")
	var rolls: Dictionary = {}
	for i in 200:
		rolls[cd.roll_number()] = true
	check(rolls.keys().all(func(n: int) -> bool: return n >= 2 and n <= 4) and rolls.size() == 3, "drawn number floats within base ±1")
	check(card_named("爆破").roll_number() <= CardData.NUMBER_MAX, "floating number clamps to 1-5")
	check(cd.effects[0].value(4) == 8 and not card_named("整理").effects[0].scales_with_number, "effect value = current number × per-point factor")

	# --- 出牌：推进倒计时、升级连锁、更新牌堆顶 ---
	var remy := br._find_unit("Remy")
	var grey := br._find_unit("Grey")
	remy.set_max_hp(500)
	grey.set_max_hp(500)
	br._find_unit("Wangg").set_max_hp(500)
	clear_hand()
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	set_top(0, 3)
	var hp: float = remy.get_current_hp()
	var attack := make_card("重拳", 0, 3)
	var spare := make_card("速射", 1, 3)
	make_card("速射", 3, 1)
	hm.refresh_numbers()
	await play(attack, "Remy")
	check(remy.get_current_hp() == hp - 6, "combo ×1: red 3 attack deals 3 × 2 damage once")
	check(rules.combo == 2 and rules.ticks == 1 and rules.enemies.Remy == 49, "one play: combo +1, countdowns -1")
	check(rules.top.color == 0 and rules.top.number == 3 and rules.top.source == "player", "played card becomes pile top")
	check(spare.card.cost_value == 3 and hm.is_playable(spare), "hand card numbers stay as drawn")
	hp = remy.get_current_hp()
	await play(spare, "Remy")
	check(remy.get_current_hp() == hp - 12 and rules.combo == 3, "combo ×2: the next card's effect fires twice")
	rules.combo = 2
	var ticks: int = rules.ticks
	var illegal := make_card("重拳", 2, 5)
	await play(illegal, "Remy")
	check(rules.ticks == ticks and rules.combo == 2, "illegal card is refused without advancing time")

	# --- 摸牌：倒计时 -1、抽 1、Combo 回到 ×1 ---
	var count: int = hm._hand_count()
	await br.draw_action()
	check(hm._hand_count() == count + 1 and rules.ticks == ticks + 1 and rules.combo == 1, "draw: +1 card, countdown -1, combo cleared")

	# --- 敌人归零当场出手：攻击立即命中，掉血使 Combo -1，倒计时从头开始 ---
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	rules.enemies.Remy = 1
	rules.combo = 3
	var intent: Dictionary = br._v2_intents.Remy
	var target := br._find_unit(intent.target)
	target.reduce_shield(target.get_shield())
	var before: float = target.get_current_hp()
	clear_hand()
	make_card("速射", 3, 1)
	set_top(0, 3)
	await play(make_card("重拳", 0, 3), "Grey")
	check(rules.top.source == "enemy" and rules.top.uid == "Remy", "enemy at zero plays its card onto the pile top")
	check(target.get_current_hp() == before - intent.value, "enemy attack lands immediately when its countdown hits 0")
	check(rules.combo == 1, "actual HP damage resets combo from ×4 to ×1 after the play")
	check(rules.enemies.Remy == 3 and br._countdown_labels.Remy.text == "3", "countdown restarts in full right after acting")

	# --- 护盾完全吸收不清空 Combo ---
	rules.enemies.Remy = 1
	rules.combo = 3
	intent = br._v2_intents.Remy
	br._find_unit(intent.target).gain_shield(100)
	set_top(0, 3)
	await play(make_card("重拳", 0, 3), "Grey")
	check(rules.combo == 4, "attack fully absorbed by shield keeps the combo")

	# --- 反制牌：敌人的下一个动作是指定颜色攻击 → 反制并造成伤害 ---
	check(card_named("截击").effect_text(2).contains("红色攻击"), "counter card text names the color it counters")
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	rules.enemies.Remy = 2
	rules.combo = 1
	intent = br._v2_intents.Remy
	check(int(intent.color) == 0 and intent.stat == "damage", "Remy's next action is a red attack")
	target = br._find_unit(intent.target)
	var target_hp: float = target.get_current_hp() + target.get_shield()
	var remy_hp: float = remy.get_current_hp()
	set_top(0, 2)
	await play(make_card("截击", 0, 2), "Irene")
	check(rules.counters.size() == 1 and rules.enemies.Remy == 1, "counter card waits for the enemy's next action")
	check(br._counter_label.text == "反制：红色攻击 → 5伤", "HUD lists the waiting counter")
	await br.draw_action()
	check(rules.counters.is_empty() and target.get_current_hp() + target.get_shield() == target_hp, "matching red attack is countered")
	check(remy.get_current_hp() == remy_hp - 5, "counter deals 5 damage to the attacker")
	check(rules.enemies.Remy == 3, "countered enemy still restarts its countdown")

	# 颜色不符：反制作废，攻击照常命中。
	rules.enemies.Remy = 2
	intent = br._v2_intents.Remy
	target = br._find_unit(intent.target)
	target.reduce_shield(target.get_shield())
	before = target.get_current_hp()
	set_top(1, 3)
	await play(make_card("盾反", 1, 3), "Zhouzhou")
	await br.draw_action()
	check(rules.counters.is_empty() and target.get_current_hp() == before - intent.value, "non-matching counter is consumed and the attack lands")

	# Combo ×2：反制触发两次，伤害翻倍；任意色反制对任意攻击生效。
	rules.enemies.Remy = 2
	rules.combo = 2
	remy_hp = remy.get_current_hp()
	set_top(3, 1)
	await play(make_card("拆招", 3, 1), "Irene")
	check(rules.counters.size() == 2, "combo ×2 arms the counter twice")
	await br.draw_action()
	check(remy.get_current_hp() == remy_hp - 6, "any-color counter fires on any attack, 3 × 2 damage")

	# 敌人的下一个动作不是攻击：反制同样被消耗。
	var wangg := br._find_unit("Wangg")
	wangg.ai_memory["v2_actions"] = 2
	br._v2_next_intent(wangg)
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	rules.enemies.Wangg = 2
	set_top(3, 1)
	await play(make_card("拆招", 3, 1), "Irene")
	var shield_before: float = wangg.get_shield()
	await br.draw_action()
	check(rules.counters.is_empty() and wangg.get_shield() == shield_before + 5 and rules.top.number == 2, "enemy shield action consumes counters and resolves immediately")
	await br.draw_action()
	check(rules.enemies.Wangg == 3, "enemy restarts its countdown right after acting (4, then 3)")

	# --- 同时归零：从左到右在同一个动作里依次出手 ---
	rules.enemies.Remy = 1
	rules.enemies.Grey = 1
	var left: String = "Remy" if remy.position.x < grey.position.x else "Grey"
	var right: String = "Grey" if left == "Remy" else "Remy"
	await br.draw_action()
	check(rules.top.uid == right and rules.enemies.Remy > 0 and rules.enemies.Grey > 0, "simultaneous zero: both act in the same action, left first")

	# --- 友方技能：放入接不上的牌，改写颜色 / 点数；不算动作，进入冷却 ---
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	clear_hand()
	set_top(0, 3)
	rules.combo = 3
	var stuck := make_card("速射", 1, 2)
	var linkable := make_card("重拳", 0, 4)
	check(not br.skill_ready("Rocket", linkable), "skills only take cards that cannot be played")
	ticks = rules.ticks
	check(br.use_skill("Rocket", stuck) and stuck.base_number == 3 and hm.is_playable(stuck), "Rocket +1: blue 2 becomes blue 3 and can link")
	check(stuck.card._cost_label.text == "3", "card face shows the new number")
	check(rules.ticks == ticks and rules.combo == 3, "using a skill is not an action: no countdown, combo kept")
	check(rules.skill_cooldowns.Rocket == 3 and not br.use_skill("Rocket", make_card("速射", 1, 1)), "used skill goes on cooldown")
	var high := make_card("速射", 2, 4)
	check(br.use_skill("Irene", high) and high.base_number == 3, "Irene -1: yellow 4 becomes yellow 3")
	check(not AllySkills.can_apply("Irene", make_card("速射", 2, 1)), "Irene cannot lower a card below 1")
	var tint := make_card("速射", 1, 5)
	check(br.use_skill("Zhouzhou", tint) and tint.cost_color != 1 and tint.card.cost_color == tint.cost_color, "Zhouzhou recolors the card to a different color")
	await br.draw_action()
	check(rules.skill_cooldowns.Rocket == 2 and rules.skill_cooldowns.Irene == 2, "skill cooldowns tick down once per player action")
	check(br._intent_target_labels.Rocket.text.contains("技能") and not br._intent_target_labels.Rocket.text.contains("冷却") and br._countdown_labels.Rocket.text == "2" and br._countdown_labels.Zhouzhou.text == "3", "ally skill description and countdown group show separate information")
	check(br._countdown_labels.Rocket.text == "2" and br._countdown_labels.Irene.text == "2" and br._countdown_labels.Zhouzhou.text == "3", "ally countdown group shows skill cooldown")

	# --- 手牌打空：奖励补到 5，Combo 保留、护盾保留、不额外推进倒计时 ---
	clear_hand()
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	rules.combo = 3
	br._find_unit("Irene").gain_shield(20)
	var irene_shield: float = br._find_unit("Irene").get_shield()
	set_top(1, 2)
	var last := make_card("速射", 1, 2)
	ticks = rules.ticks
	await play(last, "Remy")
	check(hm._hand_count() == 5 and rules.combo == 4, "emptying the hand refills to 5 and keeps the combo")
	check(br._find_unit("Irene").get_shield() == irene_shield, "reward refill keeps player shields")
	check(rules.ticks == ticks + 1, "reward refill does not cost a countdown")

	# --- 超过十张仍可抽牌，不弃手、不重抽、不清护盾 ---
	hm._draw_common_pool(7)
	check(hm._hand_count() == 12, "draw effects allow hands larger than ten cards")
	var original_hand := hm._cards.duplicate()
	var original_discard_count: int = hm.deck.discard_pile.size()
	irene_shield = br._find_unit("Irene").get_shield()
	ticks = rules.ticks
	rules.combo = 4
	await br.draw_action()
	check(hm._hand_count() == 13 and rules.combo == 1 and rules.ticks == ticks + 1, "draw above ten adds one card and advances one action without redrawing")
	check(original_hand.all(func(hc: HandCard) -> bool: return is_instance_valid(hc) and hm._cards.has(hc)), "drawing above ten keeps every original hand card")
	check(hm.deck.discard_pile.size() == original_discard_count, "drawing above ten does not discard the hand")
	check(br._find_unit("Irene").get_shield() == irene_shield, "drawing above ten does not clear shields")
	check(not br._draw_button.tooltip_text.contains("重抽"), "draw button no longer advertises full-hand redraw")

	# --- 每次敌人攻击按实际掉血扣一级，部分护盾仍扣，最低 ×1 ---
	var victim := br._find_unit("Irene")
	victim.reduce_shield(victim.get_shield())
	victim.gain_shield(1)
	var victim_hp: float = victim.get_current_hp()
	br._v2_intents.Remy = {"uid": "Remy", "name": "扣级测试", "stat": "damage", "value": 2, "color": 0, "number": 2, "target": "Irene"}
	rules.combo = 4
	await br._enemy_play("Remy")
	check(victim.get_current_hp() == victim_hp - 1 and rules.combo == 1 and victim.get_shield() == 0, "partially absorbed enemy attack consumes shield once and resets combo")
	br._v2_intents.Remy = {"uid": "Remy", "name": "扣级测试", "stat": "damage", "value": 2, "color": 0, "number": 2, "target": "Irene"}
	await br._enemy_play("Remy")
	check(rules.combo == 1, "successive damaging enemy attacks leave combo at one")
	rules.combo = 1
	br._v2_intents.Remy = {"uid": "Remy", "name": "扣级测试", "stat": "damage", "value": 2, "color": 0, "number": 2, "target": "Irene"}
	await br._enemy_play("Remy")
	check(rules.combo == 1, "actual enemy attack cannot lower combo below ×1")

	var browser := br.get_node("CardDebugBrowser")
	var tab := InputEventKey.new()
	tab.keycode = KEY_TAB
	tab.pressed = true
	browser._input(tab)
	check(br.debug_card_browser_open and browser._panel.visible, "TAB opens card browser")
	browser._role.select(0)
	browser._search.text = ""
	browser._refresh()
	check(browser._list.get_child_count() == 10, "Zhouzhou debug list contains 11 through 20")
	browser._search.text = "zhouzhou_11"
	browser._refresh()
	check(browser._list.get_child_count() == 1, "browser searches selected role by card ID")
	var browser_before := hm._hand_count()
	var browser_card := load("res://data/cards/Zhouzhou/zhouzhou_20.tres") as CardData
	browser._add_card(browser_card)
	browser._add_card(browser_card)
	check(hm._hand_count() == browser_before + 2, "browser can add repeated ordinary cards")
	br.clock_busy = true
	browser._add_card(browser_card)
	check(hm._hand_count() == browser_before + 2, "browser cannot add during resolution")
	br.clock_busy = false
	browser._input(tab)
	check(not br.debug_card_browser_open and not browser._panel.visible, "TAB closes browser and restores battle input")
	check((load("res://data/starting_decks/zhouzhou.tres") as StartingDeckData).cards[0].id == "zhouzhou_11"
		and (load("res://data/starting_decks/zhouzhou.tres") as StartingDeckData).cards[4].id == "zhouzhou_15",
		"Zhouzhou starter uses 11 through 15")
	var qianjun := load("res://data/cards/Zhouzhou/zhouzhou_16.tres") as CardData
	for tier in range(1, 5):
		rules.counters.clear()
		br._team.remove_buff("morale")
		br._team.add_buff("morale", 1)
		remy.set_max_hp(500)
		remy.reduce_shield(remy.get_shield())
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": qianjun}, 3, tier)
		check(rules.counters.size() == 1 and br._team.get_buff_stacks("morale") == 1, "Qianjun arms one counter without consuming morale")
		br._team.add_buff("morale", 2)
		var matched := rules.take_counters({"stat": "shield", "color": 1, "behavior_type": 2})
		await br._counter_attack(remy, matched)
		check(remy.get_current_hp() == 500 - 10 * tier and br._team.get_buff_stacks("morale") == 0,
			"Qianjun reads success-time morale and saved Combo")
	br._team.add_buff("morale", 2)
	await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": qianjun}, 3, 3)
	check(rules.take_counters({"stat": "damage", "color": 1, "behavior_type": 0}).is_empty()
		and br._team.get_buff_stacks("morale") == 2, "blue attack does not match state counter or consume morale")
	br._team.remove_buff("morale")
	var shibu := load("res://data/cards/Zhouzhou/zhouzhou_20.tres") as CardData
	remy.set_max_hp(500)
	remy.reduce_shield(remy.get_shield())
	remy.gain_shield(20)
	br._team.remove_buff("morale")
	br._team.add_buff("morale", 3)
	await br._v2_play_effect({"uid": "Zhouzhou", "selected": "Remy", "cd": shibu}, 5, 2)
	check(remy.get_current_hp() == 492 and br.recorded_damage_for(shibu) == 28
		and br._team.get_buff_stacks("morale") == 0, "Shibu records shield absorption plus HP damage and consumes morale")
	var shibu_copy := shibu.duplicate() as CardData
	check(br.recorded_damage_for(shibu_copy) == 28 and shibu.effect_text(5, 2, 3, 28).contains("90"),
		"Shibu copies share battle record and preview includes record morale and Combo")
	remy.damage(491)
	await br._v2_play_effect({"uid": "Zhouzhou", "selected": "Remy", "cd": shibu_copy}, 5, 4)
	check(br.recorded_damage_for(shibu) == 112, "Shibu record retains overkill without truncation")
	remy.set_max_hp(500)
	br.damage_records.clear()
	var qianli := load("res://data/cards/Zhouzhou/zhouzhou_19.tres") as CardData
	var qianli_intents := br._v2_intents.duplicate(true)
	var qianli_timers := rules.enemies.duplicate()
	check(qianli.rarity == 3 and qianli.roll_number() == 2 and qianli.uno_color == 1, "Qianli is epic fixed blue 2")
	for tier in range(1, 5):
		for uid in br._v2_intents:
			br._v2_intents[uid].color = 0
		var qianli_before := br._v2_intents.duplicate(true)
		var qianli_shield := br._team.get_shield()
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": qianli}, 2, tier)
		for uid in br._v2_intents:
			var expected_intent: Dictionary = qianli_before[uid].duplicate()
			expected_intent.color = 1
			check(br._v2_intents[uid] == expected_intent, "Qianli changes only current intent color")
		check(rules.enemies == qianli_timers and br._team.get_shield() == qianli_shield + [0, 3, 7, 11][tier - 1],
			"Qianli preserves countdowns and grants 0/3/7/11 shield once")
		check(qianli.effect_text(2, 0, tier).contains("护盾") == (tier > 1), "Qianli hides zero shield sentence")
	br._v2_intents = qianli_intents
	var yanluo := load("res://data/cards/Zhouzhou/zhouzhou_18.tres") as CardData
	var yanluo_intents := br._v2_intents.duplicate(true)
	for tier in range(1, 5):
		var ids: Array[String] = ["Remy", "Wangg", "Grey"]
		for index in ids.size():
			var enemy := br._find_unit(ids[index])
			enemy.set_max_hp(500)
			enemy.reduce_shield(enemy.get_shield())
			br._v2_intents[ids[index]].color = index
			rules.enemies[ids[index]] = 10
		br._team.remove_buff("morale")
		br._team.add_buff("morale", 3)
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": "", "cd": yanluo}, 3, tier)
		for index in ids.size():
			check(br._find_unit(ids[index]).get_current_hp() == 500 - 5 * tier
				and rules.enemies[ids[index]] == (10 if index == 1 else 11),
				"Yanluo damages each enemy once; only nonblue intents delay")
		check(br._team.get_buff_stacks("morale") == 0 and yanluo.effect_text(3, 3, tier).contains(str(5 * tier)),
			"Yanluo preview matches morale-scaled damage and consumes morale")
	br._v2_intents = yanluo_intents
	var guanqi := load("res://data/cards/Zhouzhou/zhouzhou_17.tres") as CardData
	var observe_saved := hm.deck.draw_pile.duplicate()
	var blue_card := load("res://data/cards/Zhouzhou/zhouzhou_11.tres") as CardData
	var green_card := load("res://data/cards/Zhouzhou/zhouzhou_12.tres") as CardData
	for tier in range(1, 5):
		hm.deck.draw_pile.assign([blue_card, green_card, blue_card, green_card])
		var count_before := hm._hand_count()
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": guanqi}, 4, tier)
		var viewed: int = 4 if tier == 4 else 3
		check(hm._hand_count() == count_before + viewed and guanqi.effect_text(4, 0, tier).contains(str(viewed)),
			"Guanqi views 3/3/3/4 once with matching preview")
		check(hm.deck.draw_pile == ([green_card, green_card] if tier == 4 else [blue_card, green_card, green_card]),
			"Guanqi moves only viewed blue originals and preserves nonblue order")
		hm.expire_flashbacks()
	hm.deck.draw_pile.assign([green_card])
	var short_before := hm._hand_count()
	hm.observe_blue_cards(4)
	check(hm._hand_count() == short_before + 1 and hm.deck.draw_pile == [green_card], "short observation copies existing cards without reshuffling")
	hm.expire_flashbacks()
	hm.deck.draw_pile.clear()
	hm.observe_blue_cards(4)
	check(hm.deck.draw_pile.is_empty(), "empty observation does not reshuffle")
	hm.deck.draw_pile.assign(observe_saved)
	var tuna := load("res://data/cards/Zhouzhou/zhouzhou_15.tres") as CardData
	check(tuna.rarity == 2 and tuna.roll_number() == 3 and tuna.uno_color == 1, "Tuna is rare fixed blue 3")
	for tier in range(1, 5):
		br._team.remove_buff("morale")
		br._team.add_buff("morale", 2)
		var gain: int = [7, 7, 8, 8][tier - 1]
		check(tuna.effect_text(3, 0, tier).contains(str(gain)), "Tuna previews 7/7/8/8 morale")
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": tuna}, 3, tier)
		check(br._team.get_buff_stacks("morale") == 2 + gain, "Tuna adds team morale once and preserves existing stacks")
	br._team.remove_buff("morale")
	var wenyun := load("res://data/cards/Zhouzhou/zhouzhou_14.tres") as CardData
	var intent_saved: Dictionary = br._v2_intents.Remy.duplicate()
	var countdown_saved: int = rules.enemies.Remy
	var action_saved: int = remy.ai_memory.get("v2_actions", 0)
	for tier in range(1, 5):
		br._v2_intents.Remy = {"uid": "Remy", "name": "染色验收", "color": 0, "number": 4, "stat": "damage", "value": 2, "target": br._team.unit_id}
		rules.enemies.Remy = 1
		var wenyun_shield_before := br._team.get_shield()
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": "Remy", "cd": wenyun}, 3, tier)
		check(br._v2_intents.Remy.color == 1 and br._v2_intents.Remy.number == 4 and br._v2_intents.Remy.value == 2
			and br._v2_intents.Remy.stat == "damage" and rules.enemies.Remy == 0, "Wenyun dyes current intent blue and decrements countdown once")
		check(br._team.get_shield() == wenyun_shield_before + [0, 1, 3, 5][tier - 1],
			"Wenyun grants 0/1/3/5 team shield once")
		check(wenyun.effect_text(3, 0, tier).contains("护盾") == (tier > 1), "zero compensation is hidden from card preview")
	br._team.gain_shield(10)
	await br._enemy_play("Remy")
	check(rules.top.color == 1 and rules.top.number == 4, "zero-countdown enemy plays the dyed card to the central pile")
	br._v2_intents.Remy = intent_saved
	rules.enemies.Remy = countdown_saved
	remy.ai_memory["v2_actions"] = action_saved
	var tage := load("res://data/cards/Zhouzhou/zhouzhou_13.tres") as CardData
	var tage_pile := hm.deck.draw_pile.duplicate()
	var compound := load("res://tests/fixtures/composite_card.tres") as CardData
	hm.deck.draw_pile.assign([compound])
	for tier in range(1, 5):
		var hand_before := hm._hand_count()
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": tage}, 2, tier)
		var reward := 2 + floori(float(tier - 1) / 2.0)
		check(hm._hand_count() == hand_before + reward and tage.effect_text(2, 0, tier).contains(str(reward)),
			"Tage copies 2/2/3/3 compound attacks and previews reward")
		check(hm.deck.draw_pile == [compound], "Tage leaves source pile intact")
		hm.expire_flashbacks()
		check(hm._hand_count() == hand_before, "Tage copies expire on next play boundary")
	hm.deck.draw_pile.assign([load("res://data/cards/Zhouzhou/zhouzhou_12.tres") as CardData])
	var no_attack_before := hm._hand_count()
	hm.generate_attack_flashbacks(3)
	check(hm._hand_count() == no_attack_before and hm.deck.draw_pile.size() == 1, "non-damage cards excluded without reshuffle")
	hm.deck.draw_pile.assign(tage_pile)
	var youshen := load("res://data/cards/Zhouzhou/zhouzhou_12.tres") as CardData
	var saved_draw := hm.deck.draw_pile.duplicate()
	var blue_template := load("res://data/cards/Zhouzhou/zhouzhou_11.tres") as CardData
	hm.deck.draw_pile.assign([blue_template])
	for combo in range(1, 5):
		rules.counters.clear()
		var before_count := hm._hand_count()
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": br._team.unit_id, "cd": youshen}, 2, combo)
		check(rules.counters.size() == 1 and hm._hand_count() == before_count, "Youshen arms exactly once without immediate reward")
		var armed := rules.take_counters({"stat": "damage", "color": 1})
		await br._counter_attack(remy, armed)
		var expected := 1 + floori(float(combo - 1) / 2.0)
		check(hm._hand_count() == before_count + expected and hm.deck.draw_pile == [blue_template], "Youshen copies blue templates with replacement and recorded Combo")
		var flash_copies: Array[HandCard] = []
		for hc in hm._cards:
			if hc.is_flashback: flash_copies.append(hc)
		check(flash_copies.size() == expected and flash_copies.all(func(hc: HandCard) -> bool: return hc.cost_color == 1 and hc.base_number == 3), "flashbacks retain template color and fixed number")
		hm.expire_flashbacks()
		check(hm._hand_count() == before_count, "unused flashbacks expire without entering piles")
	hm.deck.draw_pile.clear()
	hm.generate_color_flashbacks(1, 2)
	check(hm.deck.draw_pile.is_empty(), "no blue candidates do not trigger reshuffle")
	rules.counters = [{"uid": "Zhouzhou", "color": 1, "value": 0, "blue_flashback": 2}]
	check(rules.take_counters({"stat": "shield", "color": 1}).is_empty() and rules.counters.is_empty(), "nonattack consumes Youshen without matching")
	hm.deck.draw_pile.assign(saved_draw)
	var bengquan := load("res://data/cards/Zhouzhou/zhouzhou_11.tres") as CardData
	check(bengquan.roll_number() == 3 and bengquan.uno_color == 1 and bengquan.rarity == 1, "Bengquan has fixed blue 3 and common rarity")
	for combo in range(1, 5):
		remy.set_max_hp(500)
		remy.reduce_shield(remy.get_shield())
		var source := br._find_unit("Zhouzhou")
		source.remove_buff("morale")
		source.add_buff("morale", 3)
		var hits: Array[float] = []
		var capture := func(current: float, _maximum: float) -> void: hits.append(current)
		remy._hp_component.hp_changed.connect(capture)
		check(bengquan.effect_text(3, 3, combo).contains(str(5 * combo)), "Bengquan preview includes morale and Combo")
		await br._v2_play_effect({"uid": "Zhouzhou", "selected": "Remy", "cd": bengquan}, 3, combo)
		remy._hp_component.hp_changed.disconnect(capture)
		check(hits.size() == 2 and remy.get_current_hp() == 500 - 10 * combo and source.get_buff_stacks("morale") == 0, "Bengquan resolves two scaled hits and consumes morale")
	var team := br._team
	var support := CardData.new()
	support.belong_to = "Rocket"
	support.card_name = "队伍验收"
	var defense := CardEffect.new()
	defense.kind = "shield"
	defense.target = "全体友方"
	defense.amount = 2
	defense.scales_with_number = false
	defense.hits = 2
	support.effects = [defense]
	check(br.v2_targets(support, br._find_unit("Rocket")) == [team], "team-only card confirms one canonical target")
	var team_before := team.get_shield()
	await br._v2_play_effect({"uid": "Rocket", "selected": team.unit_id, "cd": support}, 1, 2)
	check(team.get_shield() == team_before + 8, "party defense preserves hits and Combo without triple application")
	var strike := CardEffect.new()
	strike.kind = "damage"
	strike.amount = 1
	strike.scales_with_number = false
	defense.target = "选中友方"
	support.effects = [strike, defense]
	check(br.v2_targets(support, br._find_unit("Rocket")).all(
		func(unit: UnitRoot) -> bool: return unit.unit_faction == "enemy"), "mixed card selects enemy only")
	team.remove_buff("morale")
	team.add_buff("morale", 3)
	var enemy_before := remy.get_current_hp()
	team_before = team.get_shield()
	await br._v2_play_effect({"uid": "Rocket", "selected": "Remy", "cd": support}, 1, 2)
	check(remy.get_current_hp() == enemy_before - 8 and team.get_buff_stacks("morale") == 0,
		"one morale snapshot boosts both Combo repetitions then consumes once")
	check(team.get_shield() == team_before + 8, "mixed card automatically resolves selected-friendly defense to team")
	team.add_buff("morale", 3)
	support.effects = [defense]
	await br._v2_play_effect({"uid": "Rocket", "selected": team.unit_id, "cd": support}, 1, 1)
	check(team.get_buff_stacks("morale") == 3, "support card preserves morale")
	team.remove_buff("morale")
	var rocket := br._find_unit("Rocket")
	var team_hp := rocket.get_current_hp()
	rocket.reduce_shield(rocket.get_shield())
	rocket.gain_shield(2)
	check(victim.get_shield() == 2, "allies share shield")
	victim.damage(3)
	check(rocket.get_current_hp() == team_hp - 1, "allies share HP")
	rocket.add_buff("team_test", 2)
	check(victim.get_buff_stacks("team_test") == 2, "allies share buffs")
	victim.remove_buff("team_test")
	for role in ["Zhouzhou", "Irene", "Rocket"]:
		check(br._find_unit(role).get_current_hp() > 0, role + " remains alive after team damage")
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	set_top(0, 3)
	rules.combo = 1
	var punch := make_card("重拳", 0, 4)
	hp = remy.get_current_hp()
	await play(punch, "Remy")
	check(remy.get_current_hp() < hp, "Rocket card resolves while team is alive")

	# --- 牌库循环 ---
	var deck := DeckManager.new()
	deck.init_decks(["Zhouzhou"])
	var cards := deck.draw_common(8)
	check(deck.draw_common(1).is_empty(), "empty draw and discard piles do not manufacture cards")
	deck.discard_card("Zhouzhou", cards[0])
	check(deck.draw_common(1)[0] == cards[0], "discard pile reshuffles into shared draw pile")

	# --- 胜利后锁定 ---
	for unit in br._get_units():
		if unit.unit_faction == "enemy" and unit.get_current_hp() > 0:
			unit.damage(10000)
			unit.die()
	await get_tree().create_timer(0.5).timeout
	ticks = rules.ticks
	await br.draw_action()
	check(br._battle_ended and rules.ticks == ticks, "victory stops actions")
	br.queue_free()
	await get_tree().process_frame
	br = scene.instantiate() as BattleRoot
	get_tree().root.add_child(br)
	var ally := br._find_unit("Irene")
	check(ally.get_current_hp() == br.team_max_hp and not br._battle_ended, "new battle resets team HP and outcome")
	ally.damage(br.team_max_hp + ally.get_shield())
	check(br._battle_ended and br._find_unit("Rocket").get_current_hp() == 0, "zero team HP immediately ends battle for all allies")
	br.queue_free()
	await get_tree().process_frame
	br = scene.instantiate() as BattleRoot
	get_tree().root.add_child(br)
	check(br._find_unit("Rocket").get_current_hp() == br.team_max_hp and not br._battle_ended, "reentry after defeat restores team")
	br.queue_free()
	await get_tree().process_frame
	print("BATTLE_V2_TESTS checks=%d failures=%d" % [checks, failures])
	get_tree().quit(1 if failures else 0)
