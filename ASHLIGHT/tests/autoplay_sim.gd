extends Node

## 无头自动对局：走真实出牌入口（选牌 → 点目标），每个动作后检查不变量，并统计胜负与 Combo。
## 运行：Godot --headless --path . res://tests/autoplay_sim.tscn -- games=20 policy=greedy
const ACTION_CAP: int = 400
const STUCK_SECONDS: float = 20.0

var games: int = 20
var policy: String = "greedy"
var br: BattleRoot
var hm: HandManager
var issues: Dictionary = {}       # 问题文字 → 出现次数
var issue_examples: Dictionary = {}
var results: Array[Dictionary] = []
var game_index: int = 0
var action_index: int = 0

func _ready() -> void:
	for arg in OS.get_cmdline_user_args():
		var kv := arg.split("=")
		if kv.size() == 2 and kv[0] == "games": games = int(kv[1])
		if kv.size() == 2 and kv[0] == "policy": policy = kv[1]
	Engine.time_scale = 4.0
	call_deferred("run")

func issue(text: String, detail: String = "") -> void:
	issues[text] = int(issues.get(text, 0)) + 1
	if not issue_examples.has(text):
		issue_examples[text] = "game %d action %d %s" % [game_index, action_index, detail]

func run() -> void:
	print_card_pool()
	for g in games:
		game_index = g
		await play_game(1000 + g)
	report()
	get_tree().quit(0)

func print_card_pool() -> void:
	for role in ["zhouzhou", "irene", "rocket"]:
		var config := load("res://data/starting_decks/%s.tres" % role) as StartingDeckData
		var names: Array[String] = []
		for cd in config.cards:
			names.append("%s(%s%d %s)" % [cd.card_name, ChainRules.color_name(cd.uno_color), cd.base_number, cd.effect_text().replace("
", " ")])
		print("[POOL] %s: %s" % [role, ", ".join(names)])

func alive_enemies() -> Array[UnitRoot]:
	var out: Array[UnitRoot] = []
	for u in br._get_units():
		if u.unit_faction == "enemy" and u.get_current_hp() > 0 and not u._dead:
			out.append(u)
	return out

func hand_cards() -> Array[HandCard]:
	var out: Array[HandCard] = []
	for hc in hm._cards:
		if is_instance_valid(hc) and hc.state in [HandCard.STATE_HAND, HandCard.STATE_IDLE]:
			out.append(hc)
	return out

func dealing() -> bool:
	return hm._cards.any(func(hc: HandCard) -> bool: return is_instance_valid(hc) and hc.state == HandCard.STATE_IDLE)

func wait_idle() -> bool:
	var start := Time.get_ticks_msec()
	await get_tree().process_frame
	while br.clock_busy or hm._hand_locked and not br._battle_ended or hm._redrawing or hm._cutscene_playing or hm._visual_return_busy or dealing():
		if Time.get_ticks_msec() - start > STUCK_SECONDS * 1000.0:
			issue("卡死：操作锁超过 %ds 未释放" % int(STUCK_SECONDS), "busy=%s locked=%s redraw=%s cutscene=%s visual=%s" % [br.clock_busy, hm._hand_locked, hm._redrawing, hm._cutscene_playing, hm._visual_return_busy])
			return false
		await get_tree().process_frame
	for i in 3: await get_tree().process_frame
	return true

func targets_for(hc: HandCard) -> Array[UnitRoot]:
	return br.v2_targets(hc.data, br._find_unit(hc.owner_group))

func playable_cards() -> Array[HandCard]:
	var out: Array[HandCard] = []
	for hc in hand_cards():
		if hc.state == HandCard.STATE_HAND and hm.is_playable(hc) and not targets_for(hc).is_empty():
			out.append(hc)
	return out

## 打出这张牌后，剩余手牌里还有几张能接上（同色或同点数）。
func followups(hc: HandCard) -> int:
	var n := 0
	var number := br.effective_number(hc)
	for other in hand_cards():
		if other != hc and (other.cost_color == hc.cost_color or other.base_number == number):
			n += 1
	return n

func pick_card(cards: Array[HandCard]) -> HandCard:
	if policy == "random":
		return cards.pick_random()
	var best: HandCard = null
	var best_score := -INF
	for hc in cards:
		var score := float(followups(hc)) * 10.0 + float(br.effective_number(hc))
		if hc.data.card_type == 0: score += 3.0
		if score > best_score:
			best_score = score
			best = hc
	return best

func pick_target(hc: HandCard) -> UnitRoot:
	var targets := targets_for(hc)
	var enemies := targets.filter(func(u: UnitRoot) -> bool: return u.unit_faction == "enemy")
	if not enemies.is_empty():
		enemies.sort_custom(func(a: UnitRoot, b: UnitRoot) -> bool: return a.get_current_hp() < b.get_current_hp())
		return enemies[0]
	return targets[0]

func deck_total() -> int:
	var n := hm.deck.draw_pile.size() + hm.deck.discard_pile.size()
	for hc in hand_cards():
		if not hc.is_flashback: n += 1
	return n

func check_invariants(stats: Dictionary) -> void:
	var team := br._team
	if team.get_current_hp() < 0 or team.get_current_hp() > br.team_max_hp:
		issue("队伍血量越界", str(team.get_current_hp()))
	if br.rules.combo < 1:
		issue("Combo < 1", str(br.rules.combo))
	if deck_total() != stats.deck_size:
		issue("牌数不守恒（牌库+弃牌+手牌 ≠ 起始张数）", "now=%d start=%d draw=%d discard=%d hand=%d" % [deck_total(), stats.deck_size, hm.deck.draw_pile.size(), hm.deck.discard_pile.size(), hand_cards().size()])
		stats.deck_size = deck_total()
	if br._battle_ended:
		return
	for e in alive_enemies():
		if not br.rules.enemies.has(e.unit_id):
			issue("存活敌人没有倒计时", e.unit_id)
		elif int(br.rules.enemies[e.unit_id]) <= 0:
			issue("动作结束后仍有敌人倒计时为 0（该出手没出手）", "%s=%d" % [e.unit_id, br.rules.enemies[e.unit_id]])
		if not br._v2_intents.has(e.unit_id):
			issue("存活敌人没有预告意图", e.unit_id)
	for uid in br.rules.enemies:
		var u := br._find_unit(uid)
		if not is_instance_valid(u) or u.get_current_hp() <= 0:
			issue("已死亡敌人仍在倒计时表里", uid)
	if hand_cards().is_empty() and hm.deck.draw_pile.size() + hm.deck.discard_pile.size() > 0:
		issue("手牌为空但未自动补牌")
	for e in alive_enemies():
		if e.get_current_hp() > e._hp_component._max_hp + 0.01:
			issue("敌人血量超过上限", e.unit_id)

func try_skill() -> String:
	var cds: Dictionary = br.rules.skill_cooldowns
	if int(cds.get("Rocket", 1)) == 0 and br.use_rocket_skill(): return "Rocket"
	if int(cds.get("Zhouzhou", 1)) == 0 and br.use_zhouzhou_skill(): return "Zhouzhou"
	if int(cds.get("Irene", 1)) == 0 and br.use_irene_skill(): return "Irene"
	return ""

func play_game(game_seed: int) -> void:
	seed(game_seed)
	var scene := load("res://battle/Battle.tscn") as PackedScene
	br = scene.instantiate() as BattleRoot
	br.skip_battle_animations = true
	get_tree().root.add_child(br)
	hm = get_tree().get_first_node_in_group("hand_manager") as HandManager
	await get_tree().create_timer(0.8).timeout
	var stats := {"seed": game_seed, "plays": 0, "draws": 0, "skills": 0, "refills": 0, "max_hand": 0,
		"combo_hist": {}, "max_combo": 1, "result": "cap", "team_hp": 0.0, "deck_size": deck_total(),
		"enemy_hp": {}, "streak": 0, "max_streak": 0, "skill_use": {}, "no_play_turns": 0, "mafe": false}
	action_index = 0
	while not br._battle_ended and action_index < ACTION_CAP:
		if not await wait_idle():
			stats.result = "stuck"
			break
		action_index += 1
		stats.max_hand = maxi(stats.max_hand, hand_cards().size())
		var cards := playable_cards()
		if cards.is_empty():
			var used := try_skill()
			if used != "":
				stats.skills += 1
				stats.skill_use[used] = int(stats.skill_use.get(used, 0)) + 1
				await wait_idle()
				cards = playable_cards()
		if cards.is_empty():
			stats.no_play_turns += 1
			stats.draws += 1
			stats.streak = 0
			await br.draw_action()
		else:
			var hc := pick_card(cards)
			var combo: int = br.rules.combo
			stats.combo_hist[combo] = int(stats.combo_hist.get(combo, 0)) + 1
			stats.max_combo = maxi(stats.max_combo, combo)
			stats.streak += 1
			stats.max_streak = maxi(stats.max_streak, stats.streak)
			var hand_before := hand_cards().size()
			var target := pick_target(hc)
			hm.select_card(hc)
			await get_tree().process_frame
			if hm._combo_chain.is_empty():
				issue("select_card 后没有形成可打出的选择", hc.data.card_name)
				hm.clear_selection()
				await br.draw_action()
				continue
			var ticks_before: int = br.rules.ticks
			await hm.notify_used_on_unit(target)
			stats.plays += 1
			if hand_before == 1 and hand_cards().size() >= 1:
				stats.refills += 1
			if br.rules.ticks == ticks_before and not br._battle_ended:
				issue("出牌后牌堆顶没有变化（出牌可能被静默拒绝）", "%s %s%d" % [hc.data.card_name, ChainRules.color_name(hc.cost_color), br.effective_number(hc)])
		if not await wait_idle():
			stats.result = "stuck"
			break
		if alive_enemies().any(func(u: UnitRoot) -> bool: return u.unit_id == "Mafe"): stats.mafe = true
		check_invariants(stats)
	if br._battle_ended:
		stats.result = "win" if br._team.get_current_hp() > 0 else "loss"
	stats.team_hp = br._team.get_current_hp()
	for e in br._get_units():
		if e.unit_faction == "enemy": stats.enemy_hp[e.unit_id] = e.get_current_hp()
	stats.actions = action_index
	results.append(stats)
	print("[GAME] seed=%d result=%s actions=%d plays=%d draws=%d skills=%d refills=%d maxcombo=%d maxstreak=%d maxhand=%d teamhp=%d enemies=%s combo=%s skill_use=%s" % [
		game_seed, stats.result, stats.actions, stats.plays, stats.draws, stats.skills, stats.refills, stats.max_combo, stats.max_streak, stats.max_hand, int(stats.team_hp), stats.enemy_hp, stats.combo_hist, stats.skill_use])
	br.queue_free()
	await get_tree().process_frame
	await get_tree().process_frame

func report() -> void:
	var wins := results.filter(func(r: Dictionary) -> bool: return r.result == "win").size()
	var losses := results.filter(func(r: Dictionary) -> bool: return r.result == "loss").size()
	var actions := 0.0
	var draws := 0.0
	var plays := 0.0
	var refills := 0.0
	var hist: Dictionary = {}
	for r in results:
		actions += r.actions
		draws += r.draws
		plays += r.plays
		refills += r.refills
		for k in r.combo_hist: hist[k] = int(hist.get(k, 0)) + int(r.combo_hist[k])
	var n := float(maxi(1, results.size()))
	print("[SUMMARY] policy=%s games=%d win=%d loss=%d other=%d avg_actions=%.1f avg_plays=%.1f avg_draws=%.1f avg_refills=%.1f draw_ratio=%.2f" % [policy, results.size(), wins, losses, results.size() - wins - losses, actions / n, plays / n, draws / n, refills / n, draws / maxf(1.0, draws + plays)])
	var keys := hist.keys()
	keys.sort()
	var parts: Array[String] = []
	for k in keys: parts.append("x%d:%d" % [k, hist[k]])
	print("[SUMMARY] combo at play time: " + " ".join(parts))
	for text in issues:
		print("[ISSUE] %s ×%d  e.g. %s" % [text, issues[text], issue_examples[text]])
	if issues.is_empty():
		print("[ISSUE] none")
