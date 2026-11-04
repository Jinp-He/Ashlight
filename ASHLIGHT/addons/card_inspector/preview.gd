@tool
extends VBoxContainer

var data: CardData
var card: CardRoot
var number: SpinBox
var viewport: SubViewport
var info: Label
var signature: String = ""
var elapsed: float = 0.0
var last_base: int = -1

func _ready() -> void:
	var title := Label.new()
	title.text = "卡牌预览"
	add_child(title)
	number = SpinBox.new()
	number.prefix = "预览点数 "
	number.min_value = 1
	number.max_value = 9
	number.value = data.base_number
	number.value_changed.connect(func(_value: float) -> void: _refresh())
	add_child(number)
	var container := SubViewportContainer.new()
	container.custom_minimum_size = Vector2(240, 360)
	container.stretch = true
	container.size_flags_horizontal = Control.SIZE_EXPAND_FILL
	add_child(container)
	viewport = SubViewport.new()
	viewport.size = Vector2i(480, 720)
	viewport.transparent_bg = true
	viewport.disable_3d = true
	viewport.gui_disable_input = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	container.add_child(viewport)
	card = CardRoot.new()
	viewport.add_child(card)
	info = Label.new()
	info.autowrap_mode = TextServer.AUTOWRAP_WORD_SMART
	add_child(info)
	viewport.size_changed.connect(_layout_card)
	_refresh()

func _layout_card() -> void:
	if not is_instance_valid(card): return
	var dimensions := Vector2(viewport.size)
	card.position = dimensions * 0.5
	card.scale = Vector2.ONE * minf(dimensions.x / 460.0, dimensions.y / 660.0)

func _process(delta: float) -> void:
	elapsed += delta
	if elapsed < 0.2: return
	elapsed = 0.0
	_refresh()

func _refresh() -> void:
	if data == null or not is_instance_valid(card): return
	if last_base != data.base_number:
		last_base = data.base_number
		number.set_value_no_signal(data.base_number)
	# 包括嵌套效果字段；不依赖自定义 Resource 自动发出 changed 信号。
	var state: Array = [data.card_name, data.card_type, data.rarity, data.enhanced, data.art_path,
		data.description, data.uno_color, data.base_number, data.number_spread, number.value]
	for effect in data.effects:
		if effect == null:
			state.append(null)
			continue
		state.append([effect.kind, effect.target, effect.amount, effect.scales_with_number,
			effect.hits, effect.status_id, effect.counter_color, effect.exclude_self])
	var current: String = var_to_str(state)
	if current == signature: return
	signature = current
	var errors := data.validation_errors()
	if data.art_path.is_empty() or not ResourceLoader.exists(data.art_path): errors.append("未配置有效的卡牌插画")
	# 错误的颜色也可在 Inspector 修复，不让预览抛异常。
	card.visible = errors.is_empty()
	if errors.is_empty():
		card.apply_data(data, int(number.value))
		info.text = "词条：%s\n%s" % ["、".join(data.keyword_names()), data.rules_text()]
	else:
		info.text = "配置错误：\n" + "\n".join(errors)
	_layout_card()
