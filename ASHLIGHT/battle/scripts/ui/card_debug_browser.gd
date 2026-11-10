extends CanvasLayer

var _panel: PanelContainer
var _list: VBoxContainer
var _search: LineEdit
var _role: OptionButton
var _feedback: Label
var _cards: Array[CardData] = []
var _battle: BattleRoot

func _ready() -> void:
	layer = 100
	_battle = get_parent() as BattleRoot
	_cards = CardDatabase.new().get_all().filter(func(cd: CardData) -> bool:
		if cd.belong_to == "Rocket" and cd.id in ["rocket_01", "rocket_02", "rocket_03", "rocket_04", "rocket_05", "rocket_06", "rocket_07", "rocket_08"]:
			return false
		if cd.belong_to == "Irene" and cd.id in ["irene_01", "irene_02", "irene_03", "irene_04", "irene_05", "irene_06", "irene_07", "irene_08"]:
			return false
		return cd.belong_to != "Zhouzhou" or cd.id in ["zhouzhou_11", "zhouzhou_12", "zhouzhou_13", "zhouzhou_14", "zhouzhou_15", "zhouzhou_16", "zhouzhou_17", "zhouzhou_18", "zhouzhou_19", "zhouzhou_20"])
	_panel = PanelContainer.new()
	_panel.position = Vector2(300, 80)
	_panel.size = Vector2(1000, 720)
	_panel.visible = false
	add_child(_panel)
	var margin := MarginContainer.new()
	for side in ["left", "right", "top", "bottom"]:
		margin.add_theme_constant_override("margin_" + side, 20)
	_panel.add_child(margin)
	var column := VBoxContainer.new()
	margin.add_child(column)
	var title := Label.new()
	title.text = "卡牌调试检索器 · TAB 关闭 · 点击卡牌加入手牌"
	column.add_child(title)
	_role = OptionButton.new()
	for label in ["周周", "艾琳", "火炮"]:
		_role.add_item(label)
	_role.item_selected.connect(func(_index: int) -> void: _refresh())
	column.add_child(_role)
	_search = LineEdit.new()
	_search.placeholder_text = "搜索卡名或编号"
	_search.text_changed.connect(func(_text: String) -> void: _refresh())
	column.add_child(_search)
	_feedback = Label.new()
	column.add_child(_feedback)
	var scroll := ScrollContainer.new()
	scroll.custom_minimum_size = Vector2(940, 540)
	scroll.size_flags_vertical = Control.SIZE_EXPAND_FILL
	column.add_child(scroll)
	_list = VBoxContainer.new()
	_list.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	scroll.add_child(_list)
	_refresh()

func _input(event: InputEvent) -> void:
	if event is InputEventKey and event.pressed and not event.echo and event.keycode == KEY_TAB:
		_panel.visible = not _panel.visible
		_battle.debug_card_browser_open = _panel.visible
		if _panel.visible:
			var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
			hand.clear_selection()
			_refresh()
		get_viewport().set_input_as_handled()

func _refresh() -> void:
	for child in _list.get_children():
		_list.remove_child(child)
		child.queue_free()
	var roles: Array[String] = ["Zhouzhou", "Irene", "Rocket"]
	var query := _search.text.strip_edges().to_lower()
	for cd in _cards:
		if cd.belong_to != roles[_role.selected]:
			continue
		if not query.is_empty() and not cd.card_name.to_lower().contains(query) and not cd.id.to_lower().contains(query):
			continue
		var button := Button.new()
		button.alignment = HORIZONTAL_ALIGNMENT_LEFT
		button.text = "%s  %s  %s%d\n%s" % [cd.id, cd.card_name, ChainRules.COLOR_NAMES[cd.uno_color], cd.base_number,
			cd.effect_text(cd.base_number, _battle.morale_bonus_for_role(cd.belong_to), _battle.rules.combo, _battle.recorded_damage_for(cd))]
		button.focus_mode = Control.FOCUS_NONE
		button.pressed.connect(_add_card.bind(cd))
		_list.add_child(button)

func _add_card(cd: CardData) -> void:
	var hand := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if _battle.clock_busy or _battle._battle_ended or hand._cutscene_playing or hand._redrawing or hand._hand_locked:
		_feedback.text = "当前正在结算或战斗已结束，暂不能加牌"
		return
	hand.debug_add_card(cd)
	_feedback.text = "已加入手牌：" + cd.card_name