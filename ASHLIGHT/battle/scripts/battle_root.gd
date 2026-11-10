class_name BattleRoot
extends Node2D

## 战斗入口：UNO 式接牌。玩家每个动作（出牌 / 摸牌 / 补牌）推进一次敌人倒计时；
## 归零的敌人把预告牌打到牌堆顶，攻击待结算，下一张同数字的牌可以反制。规则状态见 ChainRules。
const Z_FAR: int = -20
const Z_MID: int = 0
const Z_NEAR: int = 20
const MORALE_PER_LAYER: int = 1
const COUNTDOWN_SLIDE_TIME: float = 0.35
const SLOT_X: Dictionary = {1: 310.0, 2: 480.0, 3: 650.0, 4: 950.0, 5: 1120.0, 6: 1290.0}
const DISPLAY_NAMES: Dictionary = {"Zhouzhou": "周周", "Irene": "艾琳", "Rocket": "火炮"}
const CUTSCENE_GROUP2_SCRIPT := preload("res://battle/scripts/cutscene2/cutscene_group2.gd")
const CUTSCENE_DIRECTOR_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_director.gd")
const TRIGGER_PROMPT_SCRIPT := preload("res://battle/scripts/units/trigger_prompt.gd")
const TRIGGER_LAYER: int = 70

@export_range(1.0, 1000.0, 1.0, "or_greater") var team_max_hp: float = 60.0

@export_group("队伍 HUD")
@export_range(1.5, 2.0, 0.05) var team_bar_width_multiplier: float = 1.75
@export var team_hud_vertical_offset: float = 0.0
@export var team_number_offset: Vector2 = Vector2(0, -24)
var _team_panel_rect: Rect2

@export_group("演出组2")
@export_range(0.1, 3.0, 0.05) var group2_duration_multiplier: float = 1.0
@export var group2_friendly_wait: float = 0.5
@export var group2_transition_duration: float = 0.3

var rules := ChainRules.new()
var clock_busy: bool = false
var _irene_passive_used: int = 0
var summon: Dictionary = {}
var _skill_panels: Dictionary = {}
var _open_skill_uid: String = ""
var debug_card_browser_open: bool = false
var skip_battle_animations: bool = false
var _team: UnitRoot
var _battle_ended: bool = false
@export var reserve_fade_duration: float = 0.3
var _mafe_reserve_used: bool = false
var damage_records: Dictionary = {}

func recorded_damage_for(cd: CardData) -> int:
	return int(damage_records.get(cd.card_name, 11))

## 敌人 uid → 只读 MonsterData。生命、倒计时、循环进度均留在运行时。
var _monster_data: Dictionary = {}
var _enemy_shield_previous: Dictionary = {}
var _wangg_shield_candidates: Dictionary = {}
## 敌人 uid → 意图资源生成的独立预告快照（含锁定目标 target）。
var _v2_intents: Dictionary = {}
var _countdown_labels: Dictionary = {}

var _intent_target_labels: Dictionary = {}
var _combo_label: Label
var _top_flame: PileFlame
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
	for unit in _get_units():
		if unit.unit_faction == "player":
			if _team == null:
				_team = unit
				_team.set_max_hp(team_max_hp)
				_team._hp_component.hp_changed.connect(_on_team_hp_changed)
			else:
				unit.share_combat_state(_team)
	var roles: Array[String] = []
	for unit in _get_units():
		if unit.unit_faction == "enemy" or unit == _team:
			unit._buff_component.buff_fully_removed.connect(_on_irene_buff_removed.bind(unit))
		unit.died.connect(_on_unit_died)
		unit.buffs_changed.connect(_on_unit_buffs_changed)
		if unit.unit_faction == "player":
			roles.append(unit.unit_id)
			if not AllySkills.get_skill(unit.unit_id).is_empty():
				rules.skill_cooldowns[unit.unit_id] = 0
		else:
			var data := MonsterDatabase.get_data(unit.unit_id)
			if data == null: return
			_monster_data[unit.unit_id] = data
			unit.set_max_hp(data.max_hp)
			if unit.unit_id == "Mafe": unit._hp_component.hp_changed.connect(_on_mafe_hp_changed.bind(unit))
			rules.enemies[unit.unit_id] = data.countdown
			_v2_next_intent(unit)
		_countdown_labels[unit.unit_id] = unit._countdown_component.text_label
		if unit.unit_faction == "player":
			var panel := preload("res://battle/scripts/units/skill_panel.gd").new()
			panel.name = "SkillPanel"
			unit.add_child(panel)
			_skill_panels[unit.unit_id] = panel
		# 敌人身下：下一张牌；友方身下：技能与冷却。
		var target_label := Label.new()
		target_label.visible = false
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
	for unit in _get_units():
		if unit.unit_faction == "enemy" and _monster_data.has(unit.unit_id):
			var data: MonsterData = _monster_data[unit.unit_id]
			if data.speed_tier > 0: rules.enemies[unit.unit_id] = _roll_enemy_countdown(unit, data)
	for unit in _get_units():
		if unit.unit_faction == "enemy":
			_enemy_shield_previous[unit.unit_id] = unit.get_shield()
			unit._hp_component.shield_changed.connect(_on_enemy_shield_changed.bind(unit))
	_setup_team_hud()
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hm.allow_inactive_selection = true
	hm.card_selected.connect(func(_card: HandCard) -> void: _close_skill_panel())
	hm.init_decks(roles)
	_flip_starting_top(hm)
	hm.draw_opening()
	_bind_hud()
	_v2_refresh(false)

## 开局从牌库翻一张作为牌堆顶（进入弃牌堆），与 UNO 一致。
func _setup_team_hud() -> void:
	var hud := $HUD/TeamHUD as Node2D
	var left := INF
	var right := -INF
	for unit in _get_units():
		if unit.unit_faction == "player":
			left = minf(left, unit.position.x)
			right = maxf(right, unit.position.x)
	var hp := _team._hp_component
	var buffs := _team._buff_component
	var hp_y := _team.position.y + hp.position.y
	var buff_y := _team.position.y + buffs.position.y
	hud.position = Vector2((left + right) * 0.5, hp_y + team_hud_vertical_offset)
	hp.reparent(hud, false)
	buffs.reparent(hud, false)
	hp.position = Vector2.ZERO
	hp.set_width_multiplier(team_bar_width_multiplier)
	buffs.position = Vector2(0, buff_y - hp_y)
	buffs.buff_icons_per_row = 8
	var width := hp._bar_size.x
	_team_panel_rect = Rect2(Vector2(-width * 0.5, -8), Vector2(width, maxf(60, buff_y - hp_y + 32)))
	var anchor := $TeamDamageAnchor as Node2D
	anchor.reparent(hud, false)
	anchor.position = team_number_offset - DamageNumber.POPUP_OFFSET

func _flip_starting_top(_hm: HandManager) -> void:
	# 开局不翻牌、不消耗牌库：黑色和0分别代表任意颜色、任意点数。
	rules.top = {"color": ChainRules.ANY_COLOR, "number": 0, "source": "opening", "uid": "", "name": "开局"}

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
func _debug_adjust_combo(amount: int) -> void:
	if clock_busy or _battle_ended:
		return
	rules.combo = maxi(rules.combo + amount, 1) if amount > 0 else 1
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	hand.refresh_numbers()
	_v2_refresh_hud()

func _bind_hud() -> void:
	$HUD/ComboDebugButtons/AddOne.pressed.connect(_debug_adjust_combo.bind(1))
	$HUD/ComboDebugButtons/AddFive.pressed.connect(_debug_adjust_combo.bind(5))
	$HUD/ComboDebugButtons/Clear.pressed.connect(_debug_adjust_combo.bind(0))
	_top_flame = $HUD/PileTop/Flame
	_top_number = $HUD/PileTop/Number
	_float_pile_number(_top_number.position)
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
	var departing := _find_unit(uid)
	if uid in ["Remy", "Grey", "Wangg"] and not _mafe_reserve_used and not _battle_ended and is_instance_valid(_team) and _team.get_current_hp() > 0:
		_spawn_mafe_reserve(departing.position if is_instance_valid(departing) else Vector2(1120, 510))
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
	for u in units:
		if not (u is UnitRoot):
			continue
		var ur := u as UnitRoot
		if not is_instance_valid(ur) or ur.get_current_hp() <= 0.0:
			continue
		if ur.unit_faction == "enemy":
			enemy_alive += 1
	# 首个阵亡单位尚在淡出时，等待死亡回调完成替补，不能提前结算胜利。
	if enemy_alive == 0 and not _mafe_reserve_used and is_instance_valid(_team) and _team.get_current_hp() > 0:
		return
	if enemy_alive == 0:
		_battle_ended = true
		print("[Battle] 战斗胜利：敌方单位全灭。")
	elif not is_instance_valid(_team) or _team.get_current_hp() <= 0.0:
		_battle_ended = true
		print("[Battle] 战斗失败：玩家单位全灭。")

func _on_team_hp_changed(current: float, _maximum: float) -> void:
	if current > 0.0 or _battle_ended:
		return
	_check_battle_end()
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hm):
		hm.set_hand_locked(true)
	_v2_refresh()

func _on_unit_buffs_changed(unit_id: String) -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hm):
		if is_instance_valid(_find_unit(unit_id)) and _find_unit(unit_id).unit_faction == "player":
			for role in DISPLAY_NAMES:
				hm.refresh_morale_text_for_role(role)
		else:
			hm.refresh_morale_text_for_role(unit_id)

func _tick_bleed(unit: UnitRoot) -> void:
	if not is_instance_valid(unit):
		return
	var stacks: int = unit.get_buff_stacks("bleed")
	if stacks <= 0:
		return
	var invalid: int = mini(stacks, int(unit.get_shield()))
	var dealt := unit.damage(stacks)               # 护盾优先吸收，返回真实掉血量
	unit.spawn_tick_damage("出血", int(dealt), invalid)
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
	var tick_dealt: int = 0
	if stacks > 0:
		var dealt := unit.damage(stacks)              # 护盾优先吸收，返回真实掉血量
		tick_dealt = int(dealt)
		print("[BR-poison] %s 中毒结算：受到 %d 点血量伤害（层数=%d）" % [unit.unit_id, int(dealt), stacks])
	unit.spawn_tick_damage("中毒", tick_dealt, absorbed)
	# 中毒触发提示组：单位行动后中毒发作 → 弹出「中毒！」（按阵营决定滑入方向）。
	_spawn_trigger_prompt(unit.unit_id, TriggerPrompt.TriggerType.POISON, unit.unit_faction)
	if unit.get_current_hp() <= 0.0:
		unit.die()

func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton and event.pressed and event.button_index == MOUSE_BUTTON_RIGHT:
		_close_skill_panel()
		return
	var held_hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(held_hand) and is_instance_valid(held_hand.get_selected()):
		return
	if debug_card_browser_open:
		return
	if not event is InputEventMouseButton:
		return
	var mouse := event as InputEventMouseButton
	if mouse.pressed and mouse.button_index == MOUSE_BUTTON_LEFT and not clock_busy and not _battle_ended:
		if is_instance_valid(held_hand) and held_hand._pointer_over_button():
			return
		var click_point := get_canvas_transform().affine_inverse() * mouse.position
		for unit in _get_units():
			if unit.unit_faction == "player" and unit.is_point_inside(click_point):
				_toggle_skill_panel(unit.unit_id)
				get_viewport().set_input_as_handled()
				return
		_close_skill_panel()
	if not mouse.pressed or mouse.button_index != MOUSE_BUTTON_LEFT or clock_busy or _battle_ended:
		return
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if not is_instance_valid(hm):
		return
	var skill_card := hm.get_skill_card()
	if hm.get_selected() == null and not is_instance_valid(skill_card):
		return
	var point: Vector2 = get_canvas_transform().affine_inverse() * mouse.position
	if not is_instance_valid(skill_card):
		var hud := $HUD/TeamHUD as Node2D
		if _team_panel_rect.has_point(hud.get_global_transform_with_canvas().affine_inverse() * mouse.position):
			hm.notify_used_on_unit(_team)
			return
	var target: UnitRoot = null
	for unit in _get_units():
		if unit.get_current_hp() > 0 and not unit.is_dimmed() and unit.is_point_inside(point):
			if target == null or unit.z_index >= target.z_index:
				target = unit
	if not is_instance_valid(target):
		return
	if is_instance_valid(skill_card):
		use_skill(target.unit_id, skill_card)
	elif target.unit_faction != "player":
		hm.notify_used_on_unit(target)

## 技能能否对这张牌使用：角色存活、冷却完毕、牌在手里且当前接不上、技能对这张牌有效。
func _close_skill_panel() -> void:
	if _skill_panels.has(_open_skill_uid):
		_skill_panels[_open_skill_uid].close()
	_open_skill_uid = ""

func _toggle_skill_panel(uid: String) -> void:
	if _open_skill_uid != uid:
		_close_skill_panel()
		_open_skill_uid = uid
		_skill_panels[uid].show_skill(AllySkills.get_skill(uid))
		return
	_close_skill_panel()
	if int(rules.skill_cooldowns.get(uid, 0)) > 0:
		_show_skill_cooldown(uid)
		return
	match uid:
		"Zhouzhou": use_zhouzhou_skill()
		"Irene": use_irene_skill()
		"Rocket": use_rocket_skill()

func skill_ready(_uid: String, _hc: HandCard) -> bool:
	# 三名角色均已迁移到角色面板，不再接受指定手牌的旧技能。
	return false

## 使用技能：改写这张牌并进入冷却。不算动作：不推进敌人倒计时、不影响 Combo。
func _show_skill_cooldown(uid: String) -> void:
	_spawn_card_played_prompt(uid, "技能还在冷却", "player")
	var unit := _find_unit(uid)
	if is_instance_valid(unit):
		unit.play_cooldown_feedback()

func _grant_player_blue_reward() -> void:
	var unit := _find_unit("Zhouzhou")
	if is_instance_valid(unit) and is_instance_valid(_team) and _team.get_current_hp() > 0:
		_team.add_buff("morale", unit.get_mastery(), 1)
		_spawn_card_played_prompt("Zhouzhou", str(AllySkills.get_skill("Zhouzhou").passive), "player")

func _on_irene_buff_removed(unit: UnitRoot) -> void:
	var irene := _find_unit("Irene")
	if _battle_ended or not is_instance_valid(irene) or not is_instance_valid(_team) or _team.get_current_hp() <= 0:
		return
	if not is_instance_valid(unit) or unit.get_current_hp() <= 0 or unit._dead:
		return
	if _irene_passive_used >= maxi(0, irene.get_mastery()):
		return
	_irene_passive_used += 1
	_spawn_card_played_prompt("Irene", str(AllySkills.get_skill("Irene").passive), "player")
	var before: int = int(rules.skill_cooldowns.get("Irene", 0))
	rules.skill_cooldowns["Irene"] = maxi(0, before - 1)
	if before == 1:
		_spawn_card_played_prompt("Irene", "技能就绪", "player")
	_v2_refresh()

func use_irene_skill() -> bool:
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	var irene := _find_unit("Irene")
	if not is_instance_valid(irene) or not is_instance_valid(_team) or _team.get_current_hp() <= 0 or _battle_ended:
		return false
	var message := ""
	if clock_busy or hand._hand_locked or hand._cutscene_playing or hand._redrawing:
		message = "正在结算"
	elif int(rules.skill_cooldowns.get("Irene", 0)) > 0:
		message = "技能还在冷却"
	elif rules.top.is_empty() or int(rules.top.color) < 0 or int(rules.top.color) >= ChainRules.COLOR_NAMES.size():
		return false
	elif not hand.deck.draw_pile.any(func(cd: CardData) -> bool: return cd.uno_color == int(rules.top.color)):
		message = "牌库中没有与战场颜色相同的卡牌"
	if not message.is_empty():
		if message == "技能还在冷却":
			_show_skill_cooldown("Irene")
		else:
			_spawn_card_played_prompt("Irene", message, "player")
		return false
	rules.skill_cooldowns["Irene"] = 3
	_spawn_card_played_prompt("Irene", str(AllySkills.get_skill("Irene").name), "player")
	hand.clear_selection()
	hand.generate_color_flashbacks(int(rules.top.color), 1)
	_v2_refresh()
	return true

func use_zhouzhou_skill() -> bool:
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	var message := ""
	if clock_busy or hand._cutscene_playing or hand._redrawing or hand._hand_locked:
		message = "正在结算"
	elif _battle_ended or not is_instance_valid(_team) or _team.get_current_hp() <= 0:
		return false
	elif int(rules.skill_cooldowns.get("Zhouzhou", 0)) > 0:
		message = "技能还在冷却"
	elif rules.top.is_empty() or int(rules.top.color) == 1:
		message = "当前已为蓝色"
	if not message.is_empty():
		if message == "技能还在冷却":
			_show_skill_cooldown("Zhouzhou")
		else:
			_spawn_card_played_prompt("Zhouzhou", message, "player")
		return false
	rules.skill_cooldowns["Zhouzhou"] = 4
	_spawn_card_played_prompt("Zhouzhou", str(AllySkills.get_skill("Zhouzhou").name), "player")
	hand.clear_selection()
	rules.top.color = 1
	_grant_player_blue_reward()
	_v2_refresh()
	var unit := _find_unit("Zhouzhou")
	if not skip_battle_animations:
		_v2_set_busy(true)
		var previous := unit.current_art_state
		unit.set_art_state(UnitRoot.UnitArtState.CAST)
		var timer := get_tree().create_timer(group2_friendly_wait)
		timer.timeout.connect(func() -> void:
			if is_instance_valid(unit):
				unit.set_art_state(previous)
			_v2_set_busy(false)
			_v2_refresh(), CONNECT_ONE_SHOT)
	return true

func use_rocket_skill() -> bool:
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	var rocket := _find_unit("Rocket")
	if not is_instance_valid(hand) or not is_instance_valid(rocket) or not is_instance_valid(_team) or _team.get_current_hp() <= 0 or _battle_ended:
		return false
	var message := ""
	if clock_busy or hand._hand_locked or hand._cutscene_playing or hand._redrawing:
		message = "正在结算"
	elif int(rules.skill_cooldowns.get("Rocket", 0)) > 0:
		message = "技能还在冷却"
	elif rules.top.is_empty() or int(rules.top.color) < 0 or int(rules.top.color) >= ChainRules.COLOR_NAMES.size():
		return false
	var candidates: Array[HandCard] = []
	if message.is_empty():
		candidates = hand.rocket_color_candidates(int(rules.top.color))
		if candidates.is_empty(): message = "没有可改色的手牌"
	if not message.is_empty():
		if message == "技能还在冷却": _show_skill_cooldown("Rocket")
		else: _spawn_card_played_prompt("Rocket", message, "player")
		return false
	# 在刷新、动画及其他回调之前锁定本次使用，重复点击不会再次改色。
	rules.skill_cooldowns.Rocket = int(AllySkills.get_skill("Rocket").cooldown)
	hand.clear_selection()
	hand.clear_skill_card()
	var target: HandCard = candidates.pick_random()
	target.cost_color = int(rules.top.color)
	target.card.cost_color = target.cost_color
	target.card._apply_cost()
	if not skip_battle_animations: target.card.play_card_flash(0.0, 0.35)
	_spawn_card_played_prompt("Rocket", str(AllySkills.get_skill("Rocket").name), "player")
	hand.refresh_numbers()
	_v2_refresh()
	return true

## 保留旧调用入口，但不再接受手牌目标或增加点数。
func use_skill(uid: String, _hc: HandCard) -> bool:
	return use_rocket_skill() if uid == "Rocket" else false

func rocket_passive_shield(charge: int) -> int:
	var rocket := _find_unit("Rocket")
	if not is_instance_valid(rocket) or not is_instance_valid(_team) or _team.get_current_hp() <= 0:
		return 0
	return maxi(0, charge) * maxi(0, rocket.get_mastery())

## payload 独立保存本次充能，卡牌视觉提前回收不会影响正在结算的效果。
func _begin_charge_play(payload: Dictionary) -> void:
	var cd: CardData = payload.cd
	if not cd.has_charge: return
	var transfer: int = _team.get_buff_stacks("charge")
	payload.charge = maxi(0, int(payload.get("charge", 0))) + transfer
	if transfer > 0: _team.remove_buff("charge")
	var played = payload.get("hand_card")
	if is_instance_valid(played): played.charge_points = int(payload.charge)
	var shield: int = rocket_passive_shield(int(payload.charge))
	if shield <= 0: return
	_team.gain_shield(shield)
	_spawn_card_played_prompt("Rocket", str(AllySkills.get_skill("Rocket").passive), "player")
	if not skip_battle_animations:
		var number := DamageNumber.new()
		number.z_index = 47
		add_child(number)
		number.spawn({"type": "shield", "value": shield})
		number.play_on_unit(_team)

func _finish_charge_play(payload: Dictionary) -> void:
	if not (payload.cd as CardData).has_charge: return
	payload.charge = 0
	var played = payload.get("hand_card")
	if is_instance_valid(played): played.charge_points = 0

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

## 每个原子效果独立索敌。只有手选效果使用玩家点击的目标；自身、群体不受它影响。
func _effect_targets(effect: CardEffect, caster: UnitRoot, selected: UnitRoot = null) -> Array[UnitRoot]:
	var targets: Array[UnitRoot] = []
	if effect.target == "自身" or effect.kind in ["draw", "counter"]:
		targets.append(caster)
	else:
		targets = resolve_targets(caster, effect.faction())
	if effect.exclude_self:
		targets.erase(caster)
	var canonical: Array[UnitRoot] = []
	for unit in targets:
		var combat_target: UnitRoot = _team if unit.unit_faction == "player" else unit
		if not canonical.has(combat_target):
			canonical.append(combat_target)
	targets = canonical
	if effect.target == "随机敌方" and selected != null and not targets.is_empty():
		targets = [targets.pick_random()]
	if effect.needs_selection() and selected != null and not (effect.target == "选中友方" and caster.unit_faction == "player"):
		var valid: bool = targets.has(selected)
		targets.clear()
		if valid: targets.append(selected)
	return targets

func v2_targets(cd: CardData, caster: UnitRoot) -> Array[UnitRoot]:
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		return []
	var candidates: Array[UnitRoot] = []
	var selecting: bool = false
	for effect in cd.effects:
		if effect == null or not effect.needs_selection() or effect.target == "选中友方": continue
		var targets := _effect_targets(effect, caster)
		if not selecting:
			candidates = targets
		else:
			for unit in candidates.duplicate():
				if not targets.has(unit): candidates.erase(unit)
		selecting = true
	if selecting:
		return candidates
	# 无手选效果时允许点击任意实际作用单位；抽牌、反制可点击施法者。
	for effect in cd.effects:
		if effect == null: continue
		for unit in _effect_targets(effect, caster):
			if not candidates.has(unit): candidates.append(unit)
	return candidates

func effective_number(card: HandCard) -> int:
	if rules.can_play(card.cost_color, card.base_number): return card.base_number
	if rules.top.is_empty(): return card.base_number
	var needed: int = absi(card.base_number - int(rules.top.number))
	return int(rules.top.number) if needed <= _team.get_buff_stacks("adjustment") else card.base_number

func v2_payload(card: HandCard, target: UnitRoot) -> Dictionary:
	return {"uid": card.owner_group, "selected": target.unit_id if is_instance_valid(target) else "",
		"cd": card.data, "color": card.cost_color, "base": effective_number(card), "original_base": card.base_number, "previous_number": int(rules.top.get("number", 0)),
		"hand_card": card, "charge": card.charge_points}

func _v2_resolve_effect(payload: Dictionary, atom: CardEffect, number: int, morale_bonus: int) -> Dictionary:
	var caster := _find_unit(payload.uid)
	var selected := _find_unit(payload.get("selected", ""))
	var targets: Array[UnitRoot] = []
	if atom.needs_selection() and atom.target != "选中友方" and (not is_instance_valid(selected) or selected.get_current_hp() <= 0):
		return {}
	var team_targeted := false
	for unit in _effect_targets(atom, caster, selected):
		if not is_instance_valid(unit) or unit.get_current_hp() <= 0:
			continue
		if unit.unit_faction == "player":
			if team_targeted:
				continue
			team_targeted = true
		targets.append(unit)
	var resolved_value: int = recorded_damage_for(payload.cd) + morale_bonus if payload.cd.records_damage and atom.kind == "damage" else atom.value(number, morale_bonus)
	return {"caster": caster, "targets": targets, "value": resolved_value,
		"stat": atom.status_id if atom.kind == "apply_status" else atom.kind,
		"hits": atom.hits, "atom": atom, "kind": payload.cd.card_type, "cd": payload.cd}

func _v2_apply_resolved(effect: Dictionary) -> void:
	var caster: UnitRoot = effect.caster
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0 or effect.targets.is_empty():
		return
	var value: int = effect.value
	match effect.stat:
		"shield", "heal":
			for target in effect.targets:
				if not is_instance_valid(target) or target.get_current_hp() <= 0: continue
				for hit in effect.hits:
					if effect.stat == "shield": target.gain_shield(value)
					else: target.heal(value)
		"summon_fox", "summon_deer", "summon_snake":
			_set_summon(effect.stat, value)
		"copy_debuffs", "double_debuffs":
			for source in effect.targets:
				var snapshot: Array[Dictionary] = source._buff_component.debuff_snapshot()
				for entry in snapshot:
					var stacks: int = floori(float(entry.stack) / 2.0) if effect.stat == "copy_debuffs" else int(entry.stack)
					if stacks <= 0: continue
					var recipients: Array[UnitRoot] = [source]
					if effect.stat == "copy_debuffs":
						recipients = []
						for unit in _get_units():
							if unit != source and unit.unit_faction == "enemy" and unit.get_current_hp() > 0:
								recipients.append(unit)
					for recipient in recipients:
						recipient.add_buff(entry.id, stacks, 2, entry.name, entry.description)
		"observe_blue":
			var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
			hand.observe_blue_cards(value)
		"attack_flashback":
			var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
			hand.generate_attack_flashbacks(value)
		"draw":
			var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
			if is_instance_valid(hm): hm._draw_common_pool(value * effect.hits)
		"counter":
			var cd: CardData = effect.cd
			var atom: CardEffect = effect.atom
			for hit in effect.hits:
				rules.counters.append({"uid": caster.unit_id, "color": atom.counter_color, "value": value, "name": cd.card_name})
		"intent_color":
			for target in effect.targets:
				if _v2_intents.has(target.unit_id):
					_v2_intents[target.unit_id].color = clampi(value, 0, 3)
					_refresh_intent_display(target, _v2_intents[target.unit_id])
		"haste", "slow":
			for target in effect.targets:
				if rules.enemies.has(target.unit_id):
					rules.enemies[target.unit_id] = maxi(0, int(rules.enemies[target.unit_id]) + value * effect.hits * (-1 if effect.stat == "haste" else 1))
		_:
			_apply_stat(effect.stat, value, effect.hits, effect.targets, caster)
	_check_battle_end()

func _v2_start_animation(effect: Dictionary) -> CutsceneGroup2:
	if skip_battle_animations or not is_instance_valid(effect.caster):
		return null
	var group := CUTSCENE_GROUP2_SCRIPT.new() as CutsceneGroup2
	group.name = "演出组2"
	group.z_index = 46
	group.duration_multiplier = group2_duration_multiplier
	group.friendly_wait = group2_friendly_wait
	group.transition_duration = group2_transition_duration
	group.defense_fade = 0.2
	add_child(group)
	group.play(effect)
	return group

func _v2_animate(effect: Dictionary, _enemy: bool = false) -> void:
	var group := _v2_start_animation(effect)
	if is_instance_valid(group):
		await group.finished
## 卡牌效果按 Combo 触发 times 次：演出只播一次并显示合计，结算逐次执行。
func _v2_play_effect(payload: Dictionary, number: int, times: int) -> void:
	var caster := _find_unit(payload.uid)
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		return
	var cd: CardData = payload.cd
	var errors := cd.validation_errors()
	if not errors.is_empty():
		push_error("CardData %s：%s" % [cd.id, "；".join(errors)])
		return
	var morale_bonus: int = _morale_bonus(caster)
	if not summon.is_empty() and ((cd.card_type == 0 and summon.kind != "summon_snake") or (cd.card_type == 1 and summon.kind == "summon_snake")):
		await _act_summon()
	if cd.rocket_number > 0:
		await _play_rocket(payload, times, morale_bonus)
		_tick_poison(caster)
		return
	var damage_resolved := false
	var intent_colors: Dictionary = {}
	for uid in _v2_intents:
		intent_colors[uid] = int(_v2_intents[uid].color)
	for trigger in (1 if cd.execute_once or cd.combo_damage_growth or cd.blue_counter_flashback or cd.effects.any(func(e: CardEffect) -> bool: return e.kind == "attack_flashback") else times):
		for atom in cd.effects:
			if _battle_ended or caster.get_current_hp() <= 0: break
			var effect := _v2_resolve_effect(payload, atom, number, morale_bonus)
			if not effect.is_empty() and atom.excluded_intent_color >= 0:
				var eligible: Array[UnitRoot] = []
				for target in effect.targets:
					if intent_colors.has(target.unit_id) and intent_colors[target.unit_id] != atom.excluded_intent_color:
						eligible.append(target)
				effect.targets = eligible
			# 无目标只跳过当前效果，后续自身、抽牌等效果仍继续。
			if effect.is_empty() or effect.targets.is_empty(): continue
			if effect.stat == "summon_act":
				await _act_summon()
				continue
			if effect.stat == "summon_exhaust":
				while not summon.is_empty() and not _battle_ended:
					var remaining: int = summon.remaining
					await _act_summon()
					if not summon.is_empty() and int(summon.remaining) >= remaining: break
				continue
			if effect.stat == "mourn":
				effect.value *= maxi(1, times)
				await _mourn(effect, times)
				damage_resolved = true
				continue
			if atom.kind in ["summon_fox", "summon_deer", "summon_snake"]:
				effect.value = atom.summon_damage(morale_bonus, times)
			if atom.combo_linear_growth:
				effect.value *= maxi(1, times)
			if cd.combo_damage_growth and effect.stat == "damage":
				effect.value *= maxi(times, 1)
			if atom.combo_cautious_growth:
				effect.value += floori(float(maxi(times, 1) - 1) / 3.0)
			if atom.combo_shield_compensation:
				effect.value = floori(float(atom.amount * (maxi(times, 1) - 1)) / 3.0)
				if effect.value == 0:
					continue
			if effect.stat == "attack_flashback" or atom.combo_slow_growth:
				effect.value += floori(float(maxi(times, 1) - 1) / 2.0)
			var presentation: CutsceneGroup2 = null
			if trigger == 0:
				var display := effect.duplicate()
				display.chain = times
				if effect.stat == "damage": display.hits = effect.hits * (1 if cd.combo_damage_growth else times)
				elif effect.stat in ["shield", "heal"]: display.value = effect.value * effect.hits * (1 if cd.execute_once else times)
				presentation = _v2_start_animation(display)
			if cd.blue_status_counter:
				rules.counters.append({"uid": caster.unit_id, "color": 1, "value": 7,
					"name": cd.card_name, "combo": maxi(times, 1),
					"behavior_type": 2, "morale_on_success": true})
			elif cd.blue_counter_flashback:
				rules.counters.append({"uid": caster.unit_id, "color": 1, "value": 0,
					"name": cd.card_name, "combo": maxi(times, 1),
					"blue_flashback": 1 + floori(float(maxi(times, 1) - 1) / 2.0)})
			else:
				_v2_apply_resolved(effect)
				if cd.records_damage and effect.stat == "damage":
					damage_records[cd.card_name] = maxi(recorded_damage_for(cd), int(effect.value))
					var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
					hand.refresh_numbers()
			if effect.stat in ["damage", "summon_fox", "summon_deer"]:
				damage_resolved = true
			if is_instance_valid(presentation) and presentation._playing:
				await presentation.finished
	if damage_resolved:
		caster.remove_buff("morale")
	_tick_poison(caster)

## 出牌：接牌判定 → 卡牌效果 × Combo → Combo +1 → 倒计时 -1 → 归零敌人出牌 → 手牌打空则奖励补牌。
func play_card(payload: Dictionary) -> void:
	if clock_busy or _battle_ended:
		return
	var cd: CardData = payload.cd
	if not rules.can_play(payload.color, payload.base):
		return
	_irene_passive_used = 0
	_v2_set_busy(true)
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	var previous_charge_cards := hand.snapshot_charge_cards()
	hand.expire_flashbacks()
	_begin_charge_play(payload)
	var adjustment_cost: int = absi(int(payload.get("original_base", payload.base)) - int(payload.base))
	if adjustment_cost > 0: _team.remove_buff_stacks("adjustment", adjustment_cost)
	var number: int = payload.base
	var times: int = rules.combo
	# 合法牌打出时即更新牌堆顶，使火焰和点数与演出同步。
	var switched_blue: bool = not rules.top.is_empty() and int(rules.top.color) != 1 and int(payload.color) == 1
	rules.place_player_card(payload.color, number, payload.uid, cd.card_name)
	_v2_refresh_hud()
	if times > 1:
		_spawn_card_played_prompt(payload.uid, "COMBO ×%d" % times, "player")
	await _v2_play_effect(payload, number, times)
	_finish_charge_play(payload)
	if cd.id != "rocket_18": hand.grow_charge_cards(previous_charge_cards)
	if switched_blue:
		_grant_player_blue_reward()
	rules.combo_up()
	await _after_action()
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if hm._hand_count() == 0 and not _battle_ended:
		# 打空手牌是奖励：免费补到 5 张，不清空 Combo、不推进倒计时、不清护盾。
		hm._draw_common_pool(ChainRules.HAND_START)
	await _v2_finish()

## 摸牌：抽 1 张，倒计时 -1，Combo 清空（回到 ×1）。
## 手牌不设十张上限，不因张数触发弃牌、重抽或清护盾。
func draw_action() -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if clock_busy or _battle_ended or hm._cutscene_playing or hm._hand_locked or hm._redrawing:
		return
	hm.clear_selection()
	_v2_set_busy(true)
	rules.reset_combo()
	var loot_triggered: bool = _team.get_buff_stacks("loot") > 0
	if loot_triggered: _team.remove_buff_stacks("loot", 1)
	hm._draw_common_pool(1)
	await _after_action()
	if loot_triggered and not _battle_ended: await _after_action(true)
	await _v2_finish()

## 敌方攻击只对共享队伍结算一次，三个角色节点同时提供受击演出。
func _enemy_attack(caster: UnitRoot, card: Dictionary) -> bool:
	if not is_instance_valid(_team) or _team.get_current_hp() <= 0:
		return false
	var targets: Array[UnitRoot] = []
	for unit in _get_units():
		if unit.unit_faction == "player":
			targets.append(unit)
	var value: int = card.value
	var hits: int = maxi(1, int(card.get("hits", 1)))
	await _v2_animate({"caster": caster, "targets": targets, "stat": "damage",
		"value": value, "hits": hits, "blocked": value * hits <= _team.get_shield(),
		"number_target": _team, "number_anchor": $HUD/TeamHUD/TeamDamageAnchor}, true)
	var lost := false
	for hit in hits:
		if _team.get_current_hp() <= 0: break
		lost = _team.damage(value) > 0.0 or lost
	return lost

## 反制成功：取消这次攻击，并对出手的敌人造成反制牌的伤害（多张反制叠加）。
func _counter_attack(enemy: UnitRoot, counters: Array[Dictionary]) -> void:
	var total: int = 0
	for counter in counters:
		if counter.get("morale_on_success", false):
			total += (int(counter.value) + _morale_bonus(_team)) * int(counter.combo)
		else:
			total += int(counter.value)
		if counter.has("adjustment"):
			_team.add_buff("adjustment", int(counter.adjustment), 1)
		if counter.has("blue_flashback"):
			var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
			hand.generate_color_flashbacks(1, int(counter.blue_flashback))
	_spawn_card_played_prompt(enemy.unit_id, "反制！", "player")
	print("[Battle] 反制 %s：%d 张反制牌，共 %d 伤害" % [enemy.unit_id, counters.size(), total])
	if total <= 0:
		return
	var owner := _find_unit(counters[0].uid)
	if is_instance_valid(owner) and owner.get_current_hp() > 0:
		var targets: Array[UnitRoot] = [enemy]
		await _v2_animate({"caster": owner, "targets": targets, "stat": "damage", "value": total, "hits": 1})
	enemy.damage(total)
	if counters.any(func(counter: Dictionary) -> bool: return counter.get("morale_on_success", false)):
		_team.remove_buff("morale")
	if enemy.get_current_hp() <= 0:
		enemy.die()
	_check_battle_end()

## 一次玩家动作之后：全场倒计时 -1、出血结算；归零的敌人从左到右依次当场出牌。
func _after_action(enemy_only: bool = false) -> void:
	if _battle_ended:
		return
	if enemy_only:
		for uid in rules.enemies: rules.enemies[uid] = maxi(0, int(rules.enemies[uid]) - 1)
	else:
		rules.tick()
		for unit in _get_units():
			if unit.get_current_hp() > 0 and (unit.unit_faction != "player" or unit == _team):
				_tick_bleed(unit)
	_v2_refresh()
	var ready := rules.ready_enemies()
	if not ready.is_empty() and not skip_battle_animations:
		await get_tree().create_timer(COUNTDOWN_SLIDE_TIME).timeout
		await _wait_number_popups()
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
	var data: MonsterData = _monster_data.get(caster.unit_id)
	if data == null: return
	var intent := data.intent_at(int(caster.ai_memory.get("v2_actions", 0)))
	var locked_target: UnitRoot = null
	if caster.unit_id == "Wangg":
		var index := 0
		if int(caster.ai_memory.get("club_count", 0)) >= 2:
			index = 1
		elif str(caster.ai_memory.get("last_intent", "")) != "wangg_shield":
			var candidates: Array[UnitRoot] = []
			for unit in _get_units():
				if unit != caster and unit.unit_faction == "enemy" and unit.get_current_hp() > 0 and unit.get_shield() > 0 and _wangg_shield_candidates.has(unit.unit_id):
					candidates.append(unit)
			candidates.sort_custom(func(a: UnitRoot, b: UnitRoot) -> bool:
				return a.position.x < b.position.x if is_equal_approx(a.get_shield(), b.get_shield()) else a.get_shield() > b.get_shield())
			if not candidates.is_empty():
				index = 2
				locked_target = candidates[0]
		_wangg_shield_candidates.clear()
		intent = data.intent_at(index)
	if caster.unit_id == "Mafe":
		if bool(caster.ai_memory.get("envenom_pending", false)) and not bool(caster.ai_memory.get("envenom_used", false)):
			intent = data.intent_at(2)
			caster.ai_memory["envenom_used"] = true
			caster.ai_memory["envenom_pending"] = false
		else:
			intent = data.intent_at(int(caster.ai_memory.get("regular_index", 0)))


	if intent == null: return
	var target := _team if intent.stat in ["damage", "loot"] and is_instance_valid(_team) else caster
	if is_instance_valid(locked_target): target = locked_target
	var card := intent.make_payload(caster.unit_id, target.unit_id)
	_v2_intents[caster.unit_id] = card
	_refresh_intent_display(caster, card)

func _refresh_intent_display(caster: UnitRoot, card: Dictionary) -> void:
	var shield: bool = card.stat == "shield"
	if card.uid == "Mafe":
		var description := "对自身施加2层【中毒】，并进入【淬毒】状态。"
		if card.stat == "damage":
			description = "造成%d次%d点伤害。" % [int(card.get("hits", 1)), int(card.value)] if int(card.get("hits", 1)) > 1 else "造成%d点伤害，施加1层【赃物】。" % int(card.value)
		caster.set_intent("state" if card.stat == "envenom" else "att", str(card.value), "我方" if card.stat == "envenom" else "敌方", "单体", "", description, card.color, card.number)
		return

	var intent_type: int = int(card.get("behavior_type", 1 if shield else 0))
	if card.stat == "timed_shield":
		caster.set_intent("shield", str(card.value), "全场", "全体", "", "使倒计时≥1的单位获得%d点护盾。" % int(card.value), card.color, card.number)
		return
	if int(card.get("cooldown_on_hp_hit", 0)) > 0:
		caster.set_intent("att", str(card.value), "敌方", "单体", "", "造成%d点伤害。若造成有效伤害，使随机1名角色技能倒计时＋%d。" % [int(card.value), int(card.cooldown_on_hp_hit)], card.color, card.number)
		return
	if card.stat in ["loot", "steal_shield"]:
		var description := "施加%d层【赃物】。" % int(card.value) if card.stat == "loot" else "偷取友军的全部护盾。"
		caster.set_intent("state", str(card.value), "敌方" if card.stat == "loot" else "我方", "单体", "", description, card.color, card.number)
		return
	caster.set_intent(["att", "shield", "state"][clampi(intent_type, 0, 2)], str(card.value), "我方" if shield else "敌方", "单体", "",
		"%s：%s %%VALUE%% 点%s" % [card.name, "获得" if shield else "造成", "护盾" if shield else "伤害"], card.color, card.number)

## 敌人出牌：预告牌压到牌堆顶。待命的反制在此刻结算；攻击未被反制则当场命中，护盾立即生效。
func _enemy_play(uid: String) -> void:
	var caster := _find_unit(uid)
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		rules.remove_unit(uid)
		return
	var card: Dictionary = _v2_intents.get(uid, {})
	if card.is_empty():
		return
	if rules.can_play(int(card.color), int(card.number)):
		rules.combo_up()
	else:
		rules.reset_combo()
	rules.place_enemy_card(card, uid)
	_v2_refresh_hud()
	_spawn_card_played_prompt(uid, "%s %s%d" % [card.name, ChainRules.color_name(card.color), card.number], "enemy")
	var countered := rules.take_counters(card)
	if not countered.is_empty():
		await _counter_attack(caster, countered)
	elif card.stat == "damage":
		var old_loot: int = _team.get_buff_stacks("loot")
		var lost_hp: bool = await _enemy_attack(caster, card)
		if caster.unit_id == "Mafe":
			if old_loot > 0:
				_team.remove_buff_stacks("loot", 1)
				caster.ai_memory["loot_acceleration"] = 1
			if int(card.get("loot_on_attack", 0)) > 0: _team.add_buff("loot", int(card.loot_on_attack), 2)
			if caster.get_buff_stacks("envenomed") > 0:
				var monster: MonsterData = _monster_data.Mafe
				_team.add_buff("poison", monster.envenomed_poison_per_attack, 2)

		if lost_hp and int(card.get("cooldown_on_hp_hit", 0)) > 0:
			var roles: Array[String] = ["Zhouzhou", "Irene", "Rocket"]
			var chosen: String = roles.pick_random()
			rules.skill_cooldowns[chosen] = int(rules.skill_cooldowns.get(chosen, 0)) + int(card.cooldown_on_hp_hit)
			_v2_refresh()
	elif card.stat == "envenom":
		var targets: Array[UnitRoot] = [caster]
		var presentation := _v2_start_animation({"caster": caster, "targets": targets, "stat": "envenom", "value": card.value, "hits": 1, "kind": 1})
		caster.add_buff("poison", int(card.value), 2)
		var monster: MonsterData = _monster_data.Mafe
		caster.add_buff("envenomed", 1, 1, "淬毒", "持续至战斗结束。攻击行为成功执行时，额外施加%d层【中毒】，每次行为只施加1次。" % monster.envenomed_poison_per_attack)
		if is_instance_valid(presentation) and presentation._playing: await presentation.finished
	elif card.stat == "timed_shield":
		await _grant_countdown_shields(caster, int(card.value))
	elif card.stat == "loot":
		var targets: Array[UnitRoot] = [_team]
		var effect := {"caster": caster, "targets": targets, "stat": "loot", "value": card.value, "hits": 1, "kind": 1}
		var presentation := _v2_start_animation(effect)
		_team.add_buff("loot", int(card.value), 2)
		if is_instance_valid(presentation) and presentation._playing: await presentation.finished
	elif card.stat == "steal_shield":
		await _steal_enemy_shield(caster, card)
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
	var data: MonsterData = _monster_data.get(card.uid)
	if data == null: return
	if caster.unit_id != "Grey":
		rules.enemies[card.uid] = _roll_enemy_countdown(caster, data) if data.speed_tier > 0 else data.countdown + int(card.get("delay", 0))
	caster.ai_memory["v2_actions"] = int(caster.ai_memory.get("v2_actions", 0)) + 1
	if caster.unit_id == "Mafe":
		if card.get("intent_id", "") != "mafe_envenom":
			caster.ai_memory["regular_index"] = 1 - int(caster.ai_memory.get("regular_index", 0))
		rules.enemies[card.uid] = maxi(1, int(rules.enemies[card.uid]) - int(caster.ai_memory.get("loot_acceleration", 0)))
		caster.ai_memory["loot_acceleration"] = 0

	if caster.unit_id == "Wangg":
		var used: String = card.get("intent_id", "")
		if used == "wangg_ice": caster.ai_memory["club_count"] = int(caster.ai_memory.get("club_count", 0)) + 1
		elif used == "wangg_lightning": caster.ai_memory["club_count"] = 0
		caster.ai_memory["last_intent"] = used

	_v2_next_intent(caster)
	if caster.unit_id == "Grey":
		rules.enemies[card.uid] = _roll_enemy_countdown(caster, data) if data.speed_tier > 0 else data.countdown + int(card.get("delay", 0))

func _v2_set_busy(value: bool) -> void:
	if value:
		_close_skill_panel()
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
	await _wait_number_popups()
	_v2_set_busy(false)

func _wait_number_popups() -> void:
	# 只等待数字弹起与回弹；逗留及淡出不再占用操作锁。
	if skip_battle_animations:
		return
	while not get_tree().get_nodes_in_group("blocking_damage_numbers").is_empty():
		await get_tree().process_frame

func _display_name(uid: String) -> String:
	var data: MonsterData = _monster_data.get(uid)
	if data != null and not data.monster_name.is_empty(): return data.monster_name
	return DISPLAY_NAMES.get(uid, uid)

## 随机落点始终以初始位置为基准。
func _float_pile_number(anchor: Vector2) -> void:
	var offset := Vector2(randf_range(-3.0, 3.0), randf_range(-3.0, 3.0))
	var motion := _top_number.create_tween()
	motion.tween_property(_top_number, "position", anchor + offset, randf_range(0.55, 0.95)).set_trans(Tween.TRANS_SINE).set_ease(Tween.EASE_IN_OUT)
	motion.tween_callback(_float_pile_number.bind(anchor))

func _v2_refresh_hud() -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hm) and hm.deck != null:
		for group in ["deck_pile", "discard_pile"]:
			var pile := get_tree().get_first_node_in_group(group)
			if is_instance_valid(pile):
				var badge := pile.get_node_or_null("Badge") as PileBadge
				if is_instance_valid(badge):
					badge.set_count(hm.deck.draw_pile.size() if group == "deck_pile" else hm.deck.discard_pile.size())
	if is_instance_valid(_top_flame):
		if rules.top.is_empty():
			_top_flame.set_color_index(-1)
			_top_number.text = "0"
		else:
			_top_flame.set_color_index(int(rules.top.color))
			_top_number.text = str(rules.top.number)
		_combo_label.text = "COMBO ×%d" % rules.combo
		var lines: Array[String] = []
		for counter in rules.counters:
			var behavior: String = "状态行为" if int(counter.get("behavior_type", 0)) == 2 else "攻击"
			if counter.has("adjustment"):
				lines.append("反制：防御 → %d层调节" % counter.adjustment)
			elif counter.has("blue_flashback"):
				lines.append("反制：%s → %d张蓝色闪回" % [behavior, counter.blue_flashback])
			elif counter.get("morale_on_success", false):
				lines.append("反制：%s → %d伤" % [behavior, (int(counter.value) + _morale_bonus(_team)) * int(counter.combo)])
			else:
				lines.append("反制：%s → %d伤" % [behavior, counter.value])
		_counter_label.text = "\n".join(lines)
	if is_instance_valid(_draw_button):
		_draw_button.tooltip_text = "摸牌：抽1张 / 倒计时-1 / Combo回到×1"
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
			# 血条上方：技能冷却/就绪，与敌方行动倒计时共用图标组。
			text = "就绪" if cooldown == 0 else str(cooldown)
			_intent_target_labels[unit.unit_id].text = "技能「%s」\n%s" % [skill.name, skill.desc]
		elif _intent_target_labels.has(unit.unit_id):
			_intent_target_labels[unit.unit_id].text = ""
		unit._countdown_component.set_value(text, unit.unit_faction == "player")
	_v2_refresh_hud()

func resolve_targets(caster: UnitRoot, target_faction: String) -> Array[UnitRoot]:
	var result: Array[UnitRoot] = []
	if not is_instance_valid(caster) or caster.get_current_hp() <= 0:
		return result
	for unit in _get_units():
		if unit.get_current_hp() <= 0 or (caster.unit_faction == "enemy" and unit.unit_faction == "player" and unit != _team):
			continue
		var same: bool = unit.unit_faction == caster.unit_faction
		if target_faction == "全场" or (target_faction in ["我方", "友方"] and same) or (target_faction == "敌方" and not same):
			result.append(unit)
	return result

func _process(_delta: float) -> void:
	var point := get_global_mouse_position()
	var hovered_uid: String = ""
	for unit in _get_units():
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

## 召唤的运行时状态归本场战斗所有，图标层数表示剩余行动次数。
func _set_summon(kind: String, damage: int) -> void:
	if not summon.is_empty():
		var old_id: String = summon.kind
		summon.clear()
		_team.remove_buff(old_id)
	summon = {"kind": kind, "damage": damage, "remaining": 2 if kind == "summon_deer" else 3}
	_team.add_buff(kind, int(summon.remaining), 1, KeywordGlossary.name_of(kind), _summon_description())

func _summon_description() -> String:
	var title: String = {"summon_fox": "狐", "summon_deer": "鹿", "summon_snake": "蛇"}.get(summon.kind, "")
	var detail := KeywordGlossary.summon_description(summon.kind, int(summon.damage), int(summon.remaining))
	var sections: Array[Dictionary] = [{"title": title, "description": detail},
		{"title": "召唤物", "description": KeywordGlossary.ENTRIES["召唤物"]}]
	if summon.kind == "summon_snake":
		sections.append({"title": "中毒", "description": KeywordGlossary.desc_of("poison")})
	return JSON.stringify(sections)

func _act_summon() -> void:
	if summon.is_empty() or _battle_ended:
		return
	if _team.get_buff_stacks(summon.kind) <= 0:
		summon.clear()
		return
	var targets: Array[UnitRoot] = []
	for unit in _get_units():
		if unit.unit_faction == "enemy" and unit.get_current_hp() > 0:
			targets.append(unit)
	if targets.is_empty():
		return
	if summon.kind == "summon_fox":
		targets.sort_custom(func(a: UnitRoot, b: UnitRoot) -> bool:
			return a.position.x < b.position.x if is_equal_approx(a.get_current_hp(), b.get_current_hp()) else a.get_current_hp() < b.get_current_hp())
		targets = [targets[0]]
	if summon.kind == "summon_snake":
		targets.sort_custom(func(a: UnitRoot, b: UnitRoot) -> bool:
			var ac: int = int(rules.enemies.get(a.unit_id, 0))
			var bc: int = int(rules.enemies.get(b.unit_id, 0))
			return a.position.x < b.position.x if ac == bc else ac < bc)
		targets = [targets[0]]
	var kind: String = summon.kind
	var damage: int = summon.damage
	var action := {"caster": _find_unit("Irene"), "targets": targets, "stat": "poison" if kind == "summon_snake" else "damage", "value": damage, "hits": 1}
	var presentation := _v2_start_animation(action)
	_apply_stat(action.stat, damage, 1, targets, action.caster)
	if is_instance_valid(presentation) and presentation._playing:
		await presentation.finished
	if not summon.is_empty() and summon.kind == kind:
		summon.remaining -= 1
		if summon.remaining <= 0:
			summon.clear()
			_team.remove_buff(kind)
		else:
			_team._buff_component.update_buff_details(kind, int(summon.remaining), _summon_description())
	_check_battle_end()

## 在第一段伤害前一次锁定所有目标的段数，易伤衰减不改变本次追加次数。
func _mourn(effect: Dictionary, combo: int) -> void:
	var locked: Array[Dictionary] = []
	for target in effect.targets:
		locked.append({"target": target, "hits": 1 + target._buff_component.debuff_snapshot().size()})
	for item in locked:
		var target: UnitRoot = item.target
		if not is_instance_valid(target) or target.get_current_hp() <= 0: continue
		var hit := effect.duplicate()
		hit.stat = "damage"
		var single_target: Array[UnitRoot] = [target]
		hit.targets = single_target
		hit.hits = item.hits
		hit.chain = combo
		var presentation := _v2_start_animation(hit)
		_v2_apply_resolved(hit)
		if is_instance_valid(presentation) and presentation._playing:
			await presentation.finished

func _apply_stat(stat: String, value: int, hits: int, targets: Array[UnitRoot], caster: UnitRoot) -> void:
	for target in targets:
		if not is_instance_valid(target) or target.get_current_hp() <= 0:
			continue
		match stat:
			"damage":
				for hit in maxi(hits, 1):
					if target.get_current_hp() <= 0: break
					target.damage(value)
				if target.get_current_hp() <= 0:
					target.die()
			"morale", "charge", "adjustment":
				target.add_buff(stat, value * maxi(hits, 1), 1)
			"bleed", "poison", "vulnerable":
				target.add_buff(stat, value * maxi(hits, 1), 2)

func _play_rocket(payload: Dictionary, combo: int, morale: int) -> void:
	var cd: CardData = payload.cd
	var q: int = int(payload.get("charge", 0))
	var n: int = cd.rocket_number
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	var caster := _find_unit(payload.uid)
	var status_presentation: CutsceneGroup2 = null
	if n in [13, 14, 17] or (n in [18, 20] and combo == 1):
		var team_targets: Array[UnitRoot] = [_team]
		status_presentation = _v2_start_animation({"caster": caster, "targets": team_targets, "stat": "charge", "value": 0, "hits": 1, "kind": 1})
	var damage := 0
	var hits := 1
	match n:
		11: damage = (2 + q + morale) * combo; hits = 2
		12: await _rocket_shield((10 if q > 3 else 5) * combo, caster)
		13: rules.counters.append({"uid": payload.uid, "value": 0, "behavior_type": 1, "adjustment": 1 + floori(float(combo - 1) / 3), "name": cd.card_name})
		14, 17:
			var candidates := hand.snapshot_charge_cards()
			if n == 14 and not candidates.is_empty():
				var target: HandCard = candidates.pick_random()
				target.add_charge(mini(floori(float(q) / 2), 3))
			elif n == 17 and q > 3:
				for target in candidates: target.add_charge(2 + floori(float(combo - 1) / 2))
		15:
			var spent: int = mini(5, int(_team.get_shield()))
			_team.reduce_shield(spent)
			damage = (5 + morale) * combo + spent
		16: damage = (7 + morale) * combo
		18:
			var total := 0
			for target in hand.snapshot_charge_cards():
				total += target.charge_points
				target.charge_points = 0
			_team.add_buff("charge", floori(float(total) / 2), 1)
			await _rocket_shield(floori(float(7 * (combo - 1)) / 3), caster)
		19: damage = (6 + morale) * combo; hits = 1 + q
		20:
			_team.add_buff("adjustment", 2, 1)
			await _rocket_shield(floori(float(11 * (combo - 1)) / 3), caster)
	if damage > 0 and hits > 0:
		var effect := _v2_resolve_effect(payload, cd.effects[0], int(payload.base), morale)
		if not effect.is_empty() and not effect.targets.is_empty():
			effect.value = damage
			effect.hits = hits
			effect.stat = "damage"
			var presentation := _v2_start_animation(effect)
			_v2_apply_resolved(effect)
			if is_instance_valid(presentation) and presentation._playing: await presentation.finished
			_team.remove_buff("morale")
	if is_instance_valid(status_presentation) and status_presentation._playing: await status_presentation.finished
	if n == 16 and int(payload.base) == int(payload.get("previous_number", -1)):
		_team.add_buff("adjustment", 1, 1)
	hand.refresh_numbers()

func _rocket_shield(amount: int, caster: UnitRoot) -> void:
	if amount <= 0: return
	var targets: Array[UnitRoot] = [_team]
	var effect := {"caster": caster, "targets": targets, "stat": "shield", "value": amount, "hits": 1}
	var presentation := _v2_start_animation(effect)
	_v2_apply_resolved(effect)
	if is_instance_valid(presentation) and presentation._playing: await presentation.finished

func _roll_enemy_countdown(caster: UnitRoot, data: MonsterData) -> int:
	var low: int = 2 + (data.speed_tier - 1) * 3
	var candidates: Array[int] = []
	for value in range(low, low + 3):
		var occupied := false
		for other in _get_units():
			if other != caster and other.unit_faction == "enemy" and other.get_current_hp() > 0 and int(rules.enemies.get(other.unit_id, -1)) == value:
				occupied = true
				break
		if not occupied: candidates.append(value)
	if candidates.is_empty():
		push_error("倒计时候选耗尽：" + caster.unit_id)
		return low
	return candidates.pick_random()

func _on_enemy_shield_changed(value: float, unit: UnitRoot) -> void:
	var previous: float = float(_enemy_shield_previous.get(unit.unit_id, 0))
	_enemy_shield_previous[unit.unit_id] = value
	if unit.unit_id != "Wangg" and value > previous and unit.get_current_hp() > 0:
		_wangg_shield_candidates[unit.unit_id] = true

func _steal_enemy_shield(caster: UnitRoot, card: Dictionary) -> void:
	var target := _find_unit(str(card.target))
	var targets: Array[UnitRoot] = [caster]
	var effect := {"caster": caster, "targets": targets, "stat": "steal_shield", "value": 0, "hits": 1, "kind": 1}
	var presentation := _v2_start_animation(effect)
	if is_instance_valid(target) and target != caster and target.unit_faction == "enemy" and target.get_current_hp() > 0:
		var amount: float = target.get_shield()
		if amount > 0:
			target.reduce_shield(amount)
			caster.gain_shield(amount)
	if is_instance_valid(presentation) and presentation._playing: await presentation.finished

## 行为结算时一次快照筛选；队伍按角色数量累计，只结算一份共享护盾。
func _grant_countdown_shields(caster: UnitRoot, amount: int) -> void:
	var enemies: Array[UnitRoot] = []
	var player_count := 0
	for unit in _get_units():
		if unit.get_current_hp() <= 0: continue
		if unit.unit_faction == "enemy":
			if int(rules.enemies.get(unit.unit_id, 0)) >= 1: enemies.append(unit)
		elif int(rules.skill_cooldowns.get(unit.unit_id, 0)) >= 1:
			player_count += 1
	var effects: Array[Dictionary] = []
	if not enemies.is_empty(): effects.append({"caster": caster, "targets": enemies, "stat": "shield", "value": amount, "hits": 1, "kind": 2})
	if player_count > 0:
		var team_targets: Array[UnitRoot] = [_team]
		effects.append({"caster": caster, "targets": team_targets, "stat": "shield", "value": amount * player_count, "hits": 1, "kind": 2})
	for effect in effects:
		var presentation := _v2_start_animation(effect)
		_v2_apply_resolved(effect)
		if is_instance_valid(presentation) and presentation._playing: await presentation.finished

func _on_mafe_hp_changed(current: float, maximum: float, caster: UnitRoot) -> void:
	if current <= 0 or current > maximum * 0.5 or bool(caster.ai_memory.get("envenom_used", false)): return
	caster.ai_memory["envenom_pending"] = true

func _spawn_mafe_reserve(spawn_position: Vector2) -> void:
	var data := MonsterDatabase.get_data("Mafe")
	if data == null: return
	# 在节点入树及回调前标记，多个敌人同刻阵亡也只生成1次。
	_mafe_reserve_used = true
	var unit := UnitRoot.new()
	unit.name = "MafeReserve"
	unit.unit_id = "Mafe"
	unit.unit_faction = "enemy"
	unit.position = spawn_position
	unit.modulate.a = 0.0
	_units.add_child(unit)
	unit.set_max_hp(data.max_hp)
	_monster_data[unit.unit_id] = data
	unit.died.connect(_on_unit_died)
	unit.buffs_changed.connect(_on_unit_buffs_changed)
	unit._buff_component.buff_fully_removed.connect(_on_irene_buff_removed.bind(unit))
	unit._hp_component.hp_changed.connect(_on_mafe_hp_changed.bind(unit))
	_enemy_shield_previous[unit.unit_id] = unit.get_shield()
	unit._hp_component.shield_changed.connect(_on_enemy_shield_changed.bind(unit))
	rules.enemies[unit.unit_id] = _roll_enemy_countdown(unit, data)
	_countdown_labels[unit.unit_id] = unit._countdown_component.text_label
	var label := Label.new()
	label.visible = false
	unit.add_child(label)
	_intent_target_labels[unit.unit_id] = label
	_v2_next_intent(unit)
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hand) and is_instance_valid(hand.get_selected()):
		var selected := hand.get_selected()
		unit.set_dimmed(not v2_targets(selected.data, _find_unit(selected.owner_group)).has(unit))
	var fade := unit.create_tween()
	fade.tween_property(unit, "modulate:a", 1.0, reserve_fade_duration)
