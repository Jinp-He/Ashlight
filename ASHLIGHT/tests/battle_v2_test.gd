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
	for card in PlaceholderCards.all_cards():
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

func run() -> void:
	seed(22)
	var scene := load("res://battle/Battle.tscn") as PackedScene
	br = scene.instantiate() as BattleRoot
	get_tree().root.add_child(br)
	hm = get_tree().get_first_node_in_group("hand_manager") as HandManager
	br.skip_battle_animations = true
	await get_tree().create_timer(0.8).timeout
	var rules := br.rules
	check(hm._hand_count() == 5, "opening is exactly 5 shared cards")
	check(hm.deck.draw_pile.size() == 18 and hm.deck.discard_pile.size() == 1, "24-card deck: 5 drawn, 1 flipped as starting top")
	check(not rules.top.is_empty() and rules.top.source == "player", "starting top is a flipped player card")
	check(rules.combo == 1, "combo starts at ×1")
	var initial := rules.enemies.duplicate()
	await get_tree().create_timer(0.2).timeout
	check(rules.ticks == 0 and rules.enemies == initial, "idle does not advance countdowns")
	check(initial.Remy == 3 and initial.Wangg == 4 and initial.Grey == 5, "enemy countdowns come from MonsterDatabase")

	# --- 纯规则：接牌、Combo ---
	var r := ChainRules.new()
	r.place_player_card(0, 3, "", "x")
	check(r.can_play(0, 1) and r.can_play(1, 3) and not r.can_play(1, 2), "legal = same color or same current number")
	r.combo_up()
	check(not r.can_play(2, 2) and r.can_play(2, 3), "combo does not change card numbers")
	r.combo_up(); r.combo_up(); r.combo_up()
	check(r.combo == ChainRules.MAX_COMBO, "combo caps at ×4")
	var cd := card_named("重拳")
	var rolls: Dictionary = {}
	for i in 200:
		rolls[cd.roll_number()] = true
	check(rolls.keys().all(func(n: int) -> bool: return n >= 2 and n <= 4) and rolls.size() == 3, "drawn number floats within base ±1")
	check(card_named("爆破").roll_number() <= CardData.NUMBER_MAX, "floating number clamps to 1-5")
	check(cd.effect_value(4) == 8 and not card_named("整理").scales_with_number, "effect value = current number × per-point factor")

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

	# --- 摸牌：倒计时 -1、抽 1、连锁保留 ---
	var count: int = hm._hand_count()
	await br.draw_action()
	check(hm._hand_count() == count + 1 and rules.ticks == ticks + 1 and rules.combo == 1, "draw: +1 card, countdown -1, combo cleared")

	# --- 敌人归零当场出手：攻击立即命中，掉血清空 Combo，倒计时从头开始 ---
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
	check(rules.combo == 1, "actual HP damage resets the combo to ×1")
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
	check(card_named("截击").derived_effect_text(5, -1, 0, 5).contains("红色攻击"), "counter card text names the color it counters")
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

	# --- 手牌上限与满手重抽 ---
	hm._draw_common_pool(20)
	check(hm._hand_count() == ChainRules.HAND_CAP, "draw effects stop at the hand cap")
	await br.draw_action()
	check(hm._hand_count() == ChainRules.HAND_START and rules.combo == 1, "draw at full hand discards and refills to 5")

	# --- 角色倒下：牌仍可接，没有效果 ---
	var rocket := br._find_unit("Rocket")
	rocket.damage(10000)
	rocket.die()
	await get_tree().process_frame
	for uid in rules.enemies:
		rules.enemies[uid] = 50
	set_top(0, 3)
	rules.combo = 1
	var blank := make_card("重拳", 0, 4)
	hp = remy.get_current_hp()
	ticks = rules.ticks
	check(br.v2_targets(blank.data, rocket).size() > 0, "dead character's card can target any living unit")
	rules.skill_cooldowns.Rocket = 0
	check(not br.skill_ready("Rocket", make_card("速射", 1, 1)), "a fallen character cannot use their skill")
	await play(blank, "Remy")
	check(remy.get_current_hp() == hp and rules.ticks > ticks and rules.combo == 2, "dead character's card links and ticks but has no effect")

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
	print("BATTLE_V2_TESTS checks=%d failures=%d" % [checks, failures])
	br.queue_free()
	await get_tree().process_frame
	get_tree().quit(1 if failures else 0)
