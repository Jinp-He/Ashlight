class_name BattleRoot
extends Node2D

## 战斗入口：UNO 式接牌。玩家每个动作（出牌 / 摸牌 / 补牌）推进一次敌人倒计时；
## 归零的敌人把预告牌打到牌堆顶，攻击待结算，下一张同数字的牌可以反制。规则状态见 ChainRules。
const Z_FAR: int = -20
const Z_MID: int = 0
const Z_NEAR: int = 20
const PLAYER_HP: float = 60.0
const MORALE_PER_LAYER: int = 1
const COUNTDOWN_SLIDE_TIME: float = 0.35
const SLOT_X: Dictionary = {1: 310.0, 2: 480.0, 3: 650.0, 4: 950.0, 5: 1120.0, 6: 1290.0}
const DISPLAY_NAMES: Dictionary = {"Zhouzhou": "周周", "Irene": "艾琳", "Rocket": "火炮"}
const CUTSCENE_GROUP_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_group.gd")
const CUTSCENE_DIRECTOR_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_director.gd")
const TRIGGER_PROMPT_SCRIPT := preload("res://battle/scripts/units/trigger_prompt.gd")
const TRIGGER_LAYER: int = 70

var rules := ChainRules.new()
var clock_busy: bool = false
var skip_battle_animations: bool = false
var _battle_ended: bool = false
## 敌人 uid → 预告牌（MonsterDatabase 卡牌 + 锁定目标 target）。
var _v2_intents: Dictionary = {}
var _countdown_labels: Dictionary = {}
var _countdown_art_tops: Dictionary = {}
var _intent_target_labels: Dictionary = {}
var _combo_label: Label
var _top_diamond: ColorRect
var _top_number: Label
var _counter_label: Label
var _draw_button: BaseButton
var _v2_log: Label
var cutscene_director: CutsceneDirector
var _units: Node2D
var _spot_uid: String = ""
@onready var layer_far: Node2D = $Background
@onready var layer_mid: Node2D = $Midground
@onready var layer_near: Node2D = $Foreground

func _ready() -> void:
	add_to_group("battle_root")
	_apply_layer_order()
	_units = get_node("Units") as Node2D
	cutscene_director = CUTSCENE_DIRECTOR_SCRIPT.new() as CutsceneDirector
	add_child(cutscene_director)
	var roles: Array[String] = []
	for unit in _get_units():
		unit.died.connect(_on_unit_died)
		unit.buffs_changed.connect(_on_unit_buffs_changed)
		if unit.unit_faction == "player":
			roles.append(unit.unit_id)
			unit.set_max_hp(PLAYER_HP)
			if not AllySkills.get_skill(unit.unit_id).is_empty():
				rules.skill_cooldowns[unit.unit_id] = 0
		else:
			var data: Dictionary = MonsterDatabase.get_data(unit.unit_id)
			unit.set_max_hp(float(data.get("max_hp", 60)))
			rules.enemies[unit.unit_id] = int(data.get("countdown", 4))
			_v2_next_intent(unit)
		var label := Label.new()
		label.name = "OverheadCountdown"
		label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		label.size = Vector2(220, 0)
		label.add_theme_font_override("font", preload("res://assets/fonts/Biantao.ttf"))
		label.z_as_relative = false
		label.z_index = 60
		label.add_theme_color_override("font_outline_color", Color.BLACK)
		label.add_theme_constant_override("outline_size", 4)
		label.add_theme_font_size_override("font_size", 32)
		if unit.unit_faction == "player":
			label.add_theme_color_override("font_color", Color(0.55, 0.85, 1.0))
		unit.add_child(label)
		_countdown_labels[unit.unit_id] = label
		# 敌人身下：下一张牌；友方身下：技能与冷却。
		var target_label := Label.new()
		target_label.position = Vector2(-82, 65)
		target_label.size.x = 164
		target_label.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
		target_label.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		target_label.mouse_filter = Control.MOUSE_FILTER_IGNORE
		target_label.z_as_relative = false
		target_label.z_index = 60
		target_label.add_theme_font_size_override("font_size", 18)
		target_label.add_theme_color_override("font_outline_color", Color.BLACK)
		target_label.add_theme_constant_override("outline_size", 4)
		unit.add_child(target_label)
		_intent_target_labels[unit.unit_id] = target_label
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hm.allow_inactive_selection = true
	hm.init_decks(roles)
	_flip_starting_top(hm)
	hm.draw_opening()
	_bind_hud()
	_v2_refresh(false)

## 开局从牌库翻一张作为牌堆顶（进入弃牌堆），与 UNO 一致。
func _flip_starting_top(hm: HandManager) -> void:
	var flipped := hm.deck.draw_common(1)
	if flipped.is_empty():
		return
	var cd: CardData = flipped[0]
	rules.place_player_card(cd.uno_color, cd.roll_number(), "", cd.card_name)
	hm.deck.discard_card(cd.belong_to, cd)

func _spawn_trigger_prompt(unit_id: String, trigger_type: TriggerPrompt.TriggerType, faction: String) -> void:
	var prompt := TRIGGER_PROMPT_SCRIPT.new() as TriggerPrompt
	if not is_instance_valid(prompt):
		return
	prompt.setup(unit_id, trigger_type, faction)
	var layer := get_node_or_null("TriggerLayer") as CanvasLayer
	if layer == null:
		layer = CanvasLayer.new()
		layer.name = "TriggerLayer"
		layer.layer = TRIGGER_LAYER
		add_child(layer)
	layer.add_child(prompt)

func _spawn_card_played_prompt(unit_id: String, card_name: String, faction: String) -> void:
	var prompt := TRIGGER_PROMPT_SCRIPT.new() as TriggerPrompt
	if not is_instance_valid(prompt):
		return
	prompt.setup_card(unit_id, card_name, faction)
	var layer := get_node_or_null("TriggerLayer") as CanvasLayer
	if layer == null:
		layer = CanvasLayer.new()
		layer.name = "TriggerLayer"
		layer.layer = TRIGGER_LAYER
		add_child(layer)
	layer.add_child(prompt)

## HUD 节点摆放在 Battle.tscn 的 HUD 层里，可在编辑器中直接拖动；这里只取引用并接线。
## 摸牌由右下角弃牌堆按钮触发。
func _bind_hud() -> void:
	_top_diamond = $HUD/PileTop/Diamond
	_top_number = $HUD/PileTop/Number
	_combo_label = $HUD/InfoPanel/ComboLabel
	_counter_label = $HUD/InfoPanel/CounterLabel
	_v2_log = $HUD/InfoPanel/ActionLog
	_draw_button = get_tree().get_first_node_in_group("discard_pile") as BaseButton
	if is_instance_valid(_draw_button):
		_draw_button.pressed.connect(draw_action)
	# 原“重新发牌”按钮由摸牌取代，避免保留免费换牌入口。
	for button in get_tree().get_nodes_in_group("redraw_button"):
		button.visible = false

func _apply_layer_order() -> void:
	if is_instance_valid(layer_far):
		layer_far.z_index = Z_FAR
	if is_instance_valid(layer_mid):
		layer_mid.z_index = Z_MID
	if is_instance_valid(layer_near):
		layer_near.z_index = Z_NEAR

func _assign_faction_slots(order: Array[String], slots: Array[int], animate: bool) -> void:
	for i in order.size():
		if i >= slots.size():
			break   # 当前每阵营固定 3 单位；溢出者保持原位
		var unit := _find_unit(order[i])
		if not is_instance_valid(unit):
			continue
		var target_x: float = SLOT_X.get(slots[i], unit.position.x)
		var dest := Vector2(target_x, unit.position.y)
		if animate:
			var tw := create_tween()
			tw.tween_property(unit, "position", dest, 0.35).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		else:
			unit.position = dest

func _on_unit_died(uid: String) -> void:
	rules.remove_unit(uid)
	_v2_intents.erase(uid)
	_check_battle_end()
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if _battle_ended:
		hm.set_hand_locked(true)
	elif not clock_busy:
		hm.refresh_numbers()
	_v2_refresh()

func _check_battle_end() -> void:
	if _battle_ended:
		return
	var units := _get_units()
	var enemy_alive := 0
	var player_alive := 0
	for u in units:
		if not (u is UnitRoot):
			continue
		var ur := u as UnitRoot
		if not is_instance_valid(ur) or ur.get_current_hp() <= 0.0:
			continue
		if ur.unit_faction == "enemy":
			enemy_alive += 1
		else:
			player_alive += 1
	if enemy_alive == 0:
		_battle_ended = true
		print("[Battle] 战斗胜利：敌方单位全灭。")
	elif player_alive == 0:
		_battle_ended = true
		print("[Battle] 战斗失败：玩家单位全灭。")

func _on_unit_buffs_changed(unit_id: String) -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hm):
		hm.refresh_morale_text_for_role(unit_id)

func _tick_bleed(unit: UnitRoot) -> void:
	if not is_instance_valid(unit):
		return
	var stacks: int = unit.get_buff_stacks("bleed")
	if stacks <= 0:
		return
	var dealt := unit.damage(stacks)               # 护盾优先吸收，返回真实掉血量
	unit.spawn_tick_damage("出血", int(dealt))
	print("[BR-bleed] %s 出血结算：受到 %d 点伤害（层数=%d → 本 tick 后 -1）" % [unit.unit_id, int(dealt), stacks])
	# 出血触发提示组：单位前进(进高亮框)时按出血量受伤 → 弹出「出血！」（按阵营决定滑入方向）。
	_spawn_trigger_prompt(unit.unit_id, TriggerPrompt.TriggerType.BLEED, unit.unit_faction)
	unit.remove_buff_stacks("bleed", 1)   # 每次推进 -1 层
	if unit.get_current_hp() <= 0.0:
		unit.die()

func _tick_poison(unit: UnitRoot) -> void:
	if not is_instance_valid(unit):
		return
	var stacks: int = unit.get_buff_stacks("poison")
	if stacks <= 0:
		return
	var shield: float = unit.get_shield()
	var absorbed: int = mini(stacks, int(shield))
	if absorbed > 0:
		unit.reduce_shield(float(absorbed))          # 护盾 -absorbed
		unit.remove_buff_stacks("poison", absorbed)   # 中毒 -absorbed（1:1 抵消层数）
		stacks -= absorbed
		print("[BR-poison] %s 中毒结算：护盾抵消 %d 层（剩余层数=%d，护盾=%d）" % [unit.unit_id, absorbed, stacks, int(shield) - absorbed])
	if stacks > 0:
		var dealt := unit.damage(stacks)              # 护盾优先吸收，返回真实掉血量
		unit.spawn_tick_damage("中毒", int(dealt))
		print("[BR-poison] %s 中毒结算：受到 %d 点血量伤害（层数=%d）" % [unit.unit_id, int(dealt), stacks])
	# 中毒触发提示组：单位行动后中毒发作 → 弹出「中毒！」（按阵营决定滑入方向）。
	_spawn_trigger_prompt(unit.unit_id, TriggerPrompt.TriggerType.POISON, unit.unit_faction)
	if unit.get_current_hp() <= 0.0:
		unit.die()

func _input(event: InputEvent) -> void:
	if not event is InputEventMouseButton:
		return
	var mouse := event as InputEventMouseButton
	if not mouse.pressed or mouse.button_index != MOUSE_BUTTON_LEFT or clock_busy or _battle_ended:
		return
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if not is_instance_valid(hm):
		return
	var skill_card := hm.get_skill_card()
	if hm.get_selected() == null and not is_instance_valid(skill_card):
		return
	var point: Vector2 = get_canvas_transform().affine_inverse() * mouse.position
	var target: UnitRoot = null
	for unit in _get_units():
		if unit.get_current_hp() > 0 and not unit.is_dimmed() and unit.is_point_inside(point):
			if target == null or unit.z_index >= target.z_index:
				target = unit
	if not is_instance_valid(target):
		return
	if is_instance_valid(skill_card):
		use_skill(target.unit_id, skill_card)
	else:
		hm.notify_used_on_unit(target)

## 技能能否对这张牌使用：角色存活、冷却完毕、牌在手里且当前接不上、技能对这张牌有效。
func skill_ready(uid: String, hc: HandCard) -> bool:
	var unit := _find_unit(uid)
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	return is_instance_valid(unit) and unit.get_current_hp() > 0 and unit.unit_faction == "player" \
		and rules.skill_cooldowns.has(uid) and int(rules.skill_cooldowns[uid]) == 0 \
		and is_instance_valid(hc) and hm._cards.has(hc) and not hm.is_playable(hc) and AllySkills.can_apply(uid, hc)

## 使用技能：改写这张牌并进入冷却。不算动作：不推进敌人倒计时、不影响 Combo。
func use_skill(uid: String, hc: HandCard) -> bool:
	if clock_busy or _battle_ended or not skill_ready(uid, hc):
		return false
	var skill := AllySkills.get_skill(uid)
	AllySkills.apply(uid, hc)
	rules.skill_cooldowns[uid] = int(skill.cooldown)
	_spawn_card_played_prompt(uid, str(skill.name), "player")
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hm.clear_skill_card()
	hm.refresh_numbers()
	_v2_refresh()
	return true

func _find_unit(uid: String) -> UnitRoot:
	var units := get_node_or_null("Units") as Node2D
	if units == null:
		return null
	for u in units.get_children():
		if u is UnitRoot and (u as UnitRoot).unit_id == uid:
			return u as UnitRoot
	return null

func _get_units() -> Array[UnitRoot]:
	if not is_instance_valid(_units):
		_units = get_node_or_null("Units") as Node2D
	var out: Array[UnitRoot] = []
	if not is_instance_valid(_units):
		return out
	for u in _units.get_children():
		if u is UnitRoot:
			out.append(u as UnitRoot)
	return out

func clear_all_dim() -> void:
	for u in _get_units():
		var ur := u as UnitRoot
		if is_instance_valid(ur):
			ur.set_dimmed(false)

func _morale_bonus(caster: UnitRoot) -> int:
	if not is_instance_valid(caster):
		return 0
	return caster.get_buff_stacks("morale") * MORALE_PER_LAYER

func morale_bonus_for_role(role: String) -> int:
	var u := _find_unit(role)
	if not is_instance_valid(u):
		return 0
	return u.get_buff_stacks("morale") * MORALE_PER_LAYER

func _enrich_units_fx(units: Array[Dictionary], stat: String, value: int, hits: int, caster_id: String) -> void:
	if stat == "" or stat == "move":
		return
	var total: int = value * maxi(hits, 1)
	var self_stat: bool = (stat == "shield" or stat == "heal")
	for u in units:
		var uid: String = u.get("id", "")
		var is_caster: bool = (uid == caster_id)
		if self_stat and not is_caster:
			continue
		if not self_stat and is_caster:
			continue
		match stat:
			"damage":
				# 护盾优先吸收：按目标当前护盾预填 absorbed_amount，使飘字准确显示"吸收 N"。
				# 演出是伤害结算的预览（结算在演出结束后执行），此刻护盾值=实际结算时护盾值，预填一致。
				var absorbed: int = 0
				var tgt: UnitRoot = _find_unit(uid)
				if is_instance_valid(tgt):
					absorbed = mini(total, int(tgt.get_shield()))
				u["dmg_payload"] = {"type": "normal", "value": total, "absorbed_amount": absorbed}
			"heal":
				u["dmg_payload"] = {"type": "heal", "value": total}
			"shield":
				u["dmg_payload"] = {"type": "shield", "value": total}
			"morale":
				u["buff_name"] = "士气"
				u["buff_tone"] = 1
			"bleed":
				u["buff_name"] = "出血"
				u["buff_tone"] = 2

func v2_targets(cd: CardData, caster: UnitRoot) -> Array[UnitRoot]:
	# 角色已倒下：这张牌只能用来接牌，点任意存活单位即可打出，没有效果。
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		var anyone: Array[UnitRoot] = []
		for unit in _get_units():
			if unit.get_current_hp() > 0:
				anyone.append(unit)
		return anyone
	var targets := resolve_targets(caster, cd.target_faction)
	if cd.target_self:
		var self_targets: Array[UnitRoot] = []
		self_targets.append(caster)
		return self_targets
	if cd.exclude_self:
		targets.erase(caster)
	return targets

func v2_payload(card: HandCard, target: UnitRoot) -> Dictionary:
	var cd: CardData = card.data
	var caster := _find_unit(card.owner_group)
	var targets := v2_targets(cd, caster)
	if cd.scope != "群体" and cd.scope != "全场":
		var valid_target: bool = targets.has(target)
		targets.clear()
		if valid_target:
			targets.append(target)
	var ids: Array[String] = []
	for unit in targets:
		ids.append(unit.unit_id)
	return {"uid": card.owner_group, "targets": ids, "cd": cd,
		"color": card.cost_color, "base": card.base_number}

func _v2_resolve_payload(payload: Dictionary, number: int) -> Dictionary:
	var caster := _find_unit(payload.uid)
	var cd: CardData = payload.cd
	var targets: Array[UnitRoot] = []
	for uid in payload.targets:
		var target := _find_unit(uid)
		if is_instance_valid(target) and target.get_current_hp() > 0:
			targets.append(target)
	var value: int = cd.effect_value(number)
	if cd.card_type == 0:
		value += _morale_bonus(caster)
	return {"caster": caster, "targets": targets, "value": value,
		"stat": cd.archetype_stat(), "hits": cd.hits, "cd": cd}

func _v2_apply_resolved(effect: Dictionary) -> void:
	var caster: UnitRoot = effect.caster
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0 or effect.targets.is_empty():
		return
	var value: int = effect.value
	match effect.stat:
		"shield":
			for target in effect.targets:
				if is_instance_valid(target) and target.get_current_hp() > 0:
					target.gain_shield(value)
		"draw":
			var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
			hm._draw_common_pool(value)
		"counter":
			var cd: CardData = effect.cd
			rules.counters.append({"uid": caster.unit_id, "color": cd.counter_color, "value": value, "name": cd.card_name})
		_:
			_apply_stat(effect.stat, value, effect.hits, effect.targets, caster)
	_check_battle_end()

func _v2_animate(effect: Dictionary, enemy: bool = false) -> void:
	if skip_battle_animations:
		return
	var caster: UnitRoot = effect.caster
	if not is_instance_valid(caster):
		return
	var cast_state: int = UnitRoot.UnitArtState.ATTACK if effect.stat == "damage" else UnitRoot.UnitArtState.CAST
	if effect.stat == "shield":
		cast_state = UnitRoot.UnitArtState.CHARGE
	var units: Array[Dictionary] = [{"id": caster.unit_id, "faction": caster.unit_faction, "state": cast_state}]
	for target in effect.targets:
		if is_instance_valid(target) and target != caster:
			units.append({"id": target.unit_id, "faction": target.unit_faction,
				"state": UnitRoot.UnitArtState.HIT if effect.stat == "damage" else UnitRoot.UnitArtState.IDLE})
	if effect.stat == "shield":
		for item in units:
			for target in effect.targets:
				if item.id == target.unit_id:
					item["dmg_payload"] = {"type": "shield", "value": effect.value}
	else:
		_enrich_units_fx(units, effect.stat, effect.value, effect.hits, caster.unit_id)
	var completed: Array[bool] = [false]
	cutscene_director.enqueue(
		func() -> CutsceneGroup:
			var group := CUTSCENE_GROUP_SCRIPT.new() as CutsceneGroup
			group.z_index = 46
			add_child(group)
			group.play_units(units, "enemy" if enemy else "player", 0)
			return group,
		func() -> void: completed[0] = true)
	while not completed[0]:
		await get_tree().process_frame

## 卡牌效果按 Combo 触发 times 次：演出只播一次并显示合计，结算逐次执行。
func _v2_play_effect(payload: Dictionary, number: int, times: int) -> void:
	var effect := _v2_resolve_payload(payload, number)
	var caster: UnitRoot = effect.caster
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0 or effect.targets.is_empty():
		return
	var display := effect.duplicate()
	if effect.stat == "damage":
		display.hits = effect.hits * times
	elif effect.stat == "shield":
		display.value = effect.value * times
	await _v2_animate(display)
	for i in times:
		_v2_apply_resolved(effect)
	_tick_poison(caster)

## 出牌：接牌判定 → 卡牌效果 × Combo → Combo +1 → 倒计时 -1 → 归零敌人出牌 → 手牌打空则奖励补牌。
func play_card(payload: Dictionary) -> void:
	if clock_busy or _battle_ended:
		return
	var cd: CardData = payload.cd
	if not rules.can_play(payload.color, payload.base):
		return
	_v2_set_busy(true)
	var number: int = payload.base
	var times: int = rules.combo
	if times > 1:
		_spawn_card_played_prompt(payload.uid, "COMBO ×%d" % times, "player")
	await _v2_play_effect(payload, number, times)
	rules.place_player_card(payload.color, number, payload.uid, cd.card_name)
	rules.combo_up()
	await _after_action()
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if hm._hand_count() == 0 and not _battle_ended:
		# 打空手牌是奖励：免费补到 5 张，不清空 Combo、不推进倒计时、不清护盾。
		hm._draw_common_pool(ChainRules.HAND_START)
	await _v2_finish()

## 摸牌：抽 1 张，倒计时 -1，Combo 清空（回到 ×1）。
## 手牌已满时改为弃掉整手重抽，避免满手接不上时卡死。
func draw_action() -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if clock_busy or _battle_ended or hm._cutscene_playing or hm._hand_locked or hm._redrawing:
		return
	hm.clear_selection()
	_v2_set_busy(true)
	if hm._hand_count() >= ChainRules.HAND_CAP:
		hm.discard_hand()
		await _refill()
	else:
		rules.reset_combo()
		hm._draw_common_pool(1)
		await _after_action()
	await _v2_finish()

## 满手重抽：Combo 回到 ×1、清空我方护盾、补到 5 张、倒计时 -1。
func _refill() -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	rules.reset_combo()
	for unit in _get_units():
		if unit.unit_faction == "player" and unit.get_shield() > 0:
			unit.reduce_shield(unit.get_shield())
	hm._draw_common_pool(ChainRules.HAND_START - hm._hand_count())
	for step in ChainRules.REFILL_TICKS:
		await _after_action()

## 敌方攻击当场结算。返回是否造成了实际生命值伤害（= Combo 清空）。
func _enemy_attack(caster: UnitRoot, card: Dictionary) -> bool:
	var target := _find_unit(card.target)
	if not is_instance_valid(target) or target.get_current_hp() <= 0:
		var alive := resolve_targets(caster, "敌方")
		if alive.is_empty():
			return false
		target = alive.pick_random()
	var targets: Array[UnitRoot] = [target]
	await _v2_animate({"caster": caster, "targets": targets, "stat": "damage",
		"value": card.value, "hits": 1}, true)
	var lost: float = target.damage(card.value)
	if target.get_current_hp() <= 0:
		target.die()
	_check_battle_end()
	return lost > 0.0

## 反制成功：取消这次攻击，并对出手的敌人造成反制牌的伤害（多张反制叠加）。
func _counter_attack(enemy: UnitRoot, counters: Array[Dictionary]) -> void:
	var total: int = 0
	for counter in counters:
		total += int(counter.value)
	_spawn_card_played_prompt(enemy.unit_id, "反制！", "player")
	print("[Battle] 反制 %s：%d 张反制牌，共 %d 伤害" % [enemy.unit_id, counters.size(), total])
	if total <= 0:
		return
	var owner := _find_unit(counters[0].uid)
	if is_instance_valid(owner) and owner.get_current_hp() > 0:
		var targets: Array[UnitRoot] = [enemy]
		await _v2_animate({"caster": owner, "targets": targets, "stat": "damage", "value": total, "hits": 1})
	enemy.damage(total)
	if enemy.get_current_hp() <= 0:
		enemy.die()
	_check_battle_end()

## 一次玩家动作之后：全场倒计时 -1、出血结算；归零的敌人从左到右依次当场出牌。
func _after_action() -> void:
	if _battle_ended:
		return
	rules.tick()
	for unit in _get_units():
		if unit.get_current_hp() > 0:
			_tick_bleed(unit)
	_v2_refresh()
	if not skip_battle_animations:
		await get_tree().create_timer(COUNTDOWN_SLIDE_TIME).timeout
	var ready := rules.ready_enemies()
	ready.sort_custom(func(a: String, b: String) -> bool: return _v2_slot_priority(a) < _v2_slot_priority(b))
	for uid in ready:
		if _battle_ended:
			break
		await _enemy_play(uid)
		_v2_refresh()

func _v2_slot_priority(uid: String) -> float:
	var unit := _find_unit(uid)
	if not is_instance_valid(unit):
		return 99999
	return unit.position.x

func _v2_next_intent(caster: UnitRoot) -> void:
	var cards: Array = MonsterDatabase.get_data(caster.unit_id).get("cards", [])
	if cards.is_empty():
		return
	var card: Dictionary = (cards[int(caster.ai_memory.get("v2_actions", 0)) % cards.size()] as Dictionary).duplicate()
	var target := caster
	if card.stat == "damage":
		var candidates := resolve_targets(caster, "敌方")
		if not candidates.is_empty():
			target = candidates.pick_random()
	card["target"] = target.unit_id
	card["uid"] = caster.unit_id
	_v2_intents[caster.unit_id] = card
	var shield: bool = card.stat == "shield"
	caster.set_intent("shield" if shield else "att", str(card.value), "我方" if shield else "敌方", "单体", "",
		"%s %d · %s：%s %d" % [ChainRules.color_name(card.color), card.number, _display_name(target.unit_id),
		"护盾" if shield else "伤害", card.value])

## 敌人出牌：预告牌压到牌堆顶。待命的反制在此刻结算；攻击未被反制则当场命中，护盾立即生效。
func _enemy_play(uid: String) -> void:
	var caster := _find_unit(uid)
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		rules.remove_unit(uid)
		return
	var card: Dictionary = _v2_intents.get(uid, {})
	if card.is_empty():
		return
	rules.place_enemy_card(card, uid)
	_spawn_card_played_prompt(uid, "%s %s%d" % [card.name, ChainRules.color_name(card.color), card.number], "enemy")
	var countered := rules.take_counters(card)
	if not countered.is_empty():
		await _counter_attack(caster, countered)
	elif card.stat == "damage":
		if await _enemy_attack(caster, card):
			rules.reset_combo()
	else:
		var targets: Array[UnitRoot] = [caster]
		var effect: Dictionary = {"caster": caster, "targets": targets, "stat": "shield", "value": card.value, "hits": 1}
		await _v2_animate(effect, true)
		_v2_apply_resolved(effect)
	_tick_poison(caster)
	_end_enemy_action(card)

## 敌人这次出手结束：倒计时从头开始，并预告下一张牌。
func _end_enemy_action(card: Dictionary) -> void:
	var caster := _find_unit(card.uid)
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0 or _battle_ended:
		return
	var data: Dictionary = MonsterDatabase.get_data(card.uid)
	rules.enemies[card.uid] = int(data.get("countdown", 4)) + int(card.get("delay", 0))
	caster.ai_memory["v2_actions"] = int(caster.ai_memory.get("v2_actions", 0)) + 1
	_v2_next_intent(caster)

func _v2_set_busy(value: bool) -> void:
	clock_busy = value
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hm.set_hand_locked(value or _battle_ended)
	hm.set_cutscene_lock(value)
	_v2_refresh_hud()

func _v2_finish() -> void:
	_check_battle_end()
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hm.refresh_numbers()
	_v2_refresh()
	_update_battle_positions()
	if not skip_battle_animations:
		await get_tree().create_timer(COUNTDOWN_SLIDE_TIME).timeout
	_v2_set_busy(false)

func _display_name(uid: String) -> String:
	return DISPLAY_NAMES.get(uid, uid)

func _v2_refresh_hud() -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hm) and hm.deck != null:
		for group in ["deck_pile", "discard_pile"]:
			var pile := get_tree().get_first_node_in_group(group)
			if is_instance_valid(pile):
				var badge := pile.get_node_or_null("Badge") as PileBadge
				if is_instance_valid(badge):
					badge.set_count(hm.deck.draw_pile.size() if group == "deck_pile" else hm.deck.discard_pile.size())
	if is_instance_valid(_top_diamond):
		if rules.top.is_empty():
			_top_diamond.color = Color(0.3, 0.3, 0.3)
			_top_number.text = "?"
		else:
			_top_diamond.color = CardRoot.COST_COLORS[clampi(int(rules.top.color), 0, CardRoot.COST_COLORS.size() - 1)]
			_top_number.text = str(rules.top.number)
		_combo_label.text = "COMBO ×%d" % rules.combo
		var lines: Array[String] = []
		for counter in rules.counters:
			lines.append("反制：%s色攻击 → %d伤" % [ChainRules.color_name(counter.color), counter.value])
		_counter_label.text = "\n".join(lines)
	if is_instance_valid(_draw_button):
		var full: bool = is_instance_valid(hm) and hm._hand_count() >= ChainRules.HAND_CAP
		_draw_button.tooltip_text = "弃手重抽：补到5张 / 倒计时-1" if full else "摸牌：抽1张 / 倒计时-1"
		_draw_button.disabled = clock_busy or _battle_ended
	if is_instance_valid(_v2_log):
		_v2_log.text = "动作：%d 次" % rules.ticks
		if _battle_ended:
			var alive: bool = false
			for unit in _get_units():
				if unit.unit_faction == "player" and unit.get_current_hp() > 0:
					alive = true
			_v2_log.text += "  战斗胜利" if alive else "  战斗失败"

func _v2_refresh(_animated: bool = true) -> void:
	for unit in _get_units():
		var text: String = ""
		if unit.get_current_hp() > 0 and unit.unit_faction == "enemy":
			text = str(rules.enemies.get(unit.unit_id, 0))
			if _v2_intents.has(unit.unit_id):
				var card: Dictionary = _v2_intents[unit.unit_id]
				_intent_target_labels[unit.unit_id].text = "下一张 %s%d「%s」\n→ %s · %s %d" % [
					ChainRules.color_name(card.color), card.number, card.name,
					_display_name(card.target), "护盾" if card.stat == "shield" else "伤害", card.value]
		elif unit.get_current_hp() > 0 and rules.skill_cooldowns.has(unit.unit_id):
			var skill := AllySkills.get_skill(unit.unit_id)
			var cooldown: int = rules.skill_cooldowns[unit.unit_id]
			# 头顶：技能冷却（蓝色，与敌人白色的行动倒计时区分）。
			text = "就绪" if cooldown == 0 else str(cooldown)
			_intent_target_labels[unit.unit_id].text = "技能「%s」%s\n%s" % [skill.name,
				"就绪" if cooldown == 0 else "冷却 %d" % cooldown, skill.desc]
		elif _intent_target_labels.has(unit.unit_id):
			_intent_target_labels[unit.unit_id].text = ""
		if _countdown_labels.has(unit.unit_id):
			var label: Label = _countdown_labels[unit.unit_id]
			label.text = text
			_position_overhead_countdown(unit, label)
	_v2_refresh_hud()

func _position_overhead_countdown(unit: UnitRoot, label: Label) -> void:
	var sprite: Sprite2D = unit._art_sprite
	if not is_instance_valid(sprite) or sprite.texture == null:
		return
	var texture_key: String = sprite.texture.resource_path
	if not _countdown_art_tops.has(texture_key):
		var image := sprite.texture.get_image()
		var top: float = 0.0
		if image != null and not image.is_empty():
			# 部分原贴图有近透明噪点；只把明显可见的像素算作人物边缘。
			if image.is_compressed():
				image.decompress()
			image.convert(Image.FORMAT_RGBA8)
			var pixels: PackedByteArray = image.get_data()
			var width: int = image.get_width()
			var found: bool = false
			for y in image.get_height():
				for x in width:
					if pixels[(y * width + x) * 4 + 3] > 128:
						top = y
						found = true
						break
				if found:
					break
		_countdown_art_tops[texture_key] = top
	var local_top := Vector2(0, sprite.get_rect().position.y + float(_countdown_art_tops[texture_key]))
	var head: Vector2 = unit.to_local(sprite.to_global(local_top))
	label.size = Vector2(220, label.get_minimum_size().y)
	label.position = Vector2(head.x - label.size.x * 0.5, head.y - label.size.y - 6)

func resolve_targets(caster: UnitRoot, target_faction: String) -> Array[UnitRoot]:
	var result: Array[UnitRoot] = []
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		return result
	for unit in _get_units():
		if unit.get_current_hp() <= 0:
			continue
		var same: bool = unit.unit_faction == caster.unit_faction
		if target_faction == "全场" or (target_faction in ["我方", "友方"] and same) or (target_faction == "敌方" and not same):
			result.append(unit)
	return result

func _process(_delta: float) -> void:
	var point := get_global_mouse_position()
	var hovered_uid: String = ""
	for unit in _get_units():
		if _countdown_labels.has(unit.unit_id):
			_position_overhead_countdown(unit, _countdown_labels[unit.unit_id])
		if unit.get_current_hp() > 0 and unit.is_point_inside(point):
			hovered_uid = unit.unit_id
	if hovered_uid != _spot_uid:
		_spot_uid = hovered_uid
		for unit in _get_units():
			unit.set_external_highlight(unit.unit_id == _spot_uid)

func _update_battle_positions() -> void:
	# 我方不再有倒计时，站位固定；敌方按剩余倒计时排序，越快行动越靠前。
	var units: Array[UnitRoot] = []
	for unit in _get_units():
		if unit.unit_faction == "enemy" and unit.get_current_hp() > 0:
			units.append(unit)
	units.sort_custom(func(a: UnitRoot, b: UnitRoot) -> bool:
		var left: int = rules.enemies.get(a.unit_id, 0)
		var right: int = rules.enemies.get(b.unit_id, 0)
		return a.position.x < b.position.x if left == right else left < right)
	var ids: Array[String] = []
	for unit in units:
		ids.append(unit.unit_id)
	var slots: Array[int] = []
	slots.assign([4, 5, 6])
	_assign_faction_slots(ids, slots, true)

func _apply_stat(stat: String, value: int, hits: int, targets: Array[UnitRoot], caster: UnitRoot) -> void:
	for target in targets:
		if not is_instance_valid(target) or target.get_current_hp() <= 0:
			continue
		match stat:
			"damage":
				target.damage(value * maxi(hits, 1))
				if target.get_current_hp() <= 0:
					target.die()
			"morale":
				target.add_buff("morale", value, 1)
			"bleed", "poison":
				target.add_buff(stat, value, 2)
	if stat == "damage" and is_instance_valid(caster):
		caster.remove_buff("morale")
