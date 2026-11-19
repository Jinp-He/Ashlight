class_name DeckManager
extends RefCounted

var piles: Dictionary = {}
var draw_pile: Array[CardData] = []
var discard_pile: Array[CardData] = []

const STARTING_DECK_DIR: String = "res://data/starting_decks"

## 只读取初始卡组清单，不再把角色的整个卡牌库自动加入开局牌库。
func init_decks(roles: Array[String]) -> bool:
	var configs: Array[StartingDeckData] = []
	var seen: Array[String] = []
	for role in roles:
		if seen.has(role): continue
		seen.append(role)
		var path: String = STARTING_DECK_DIR.path_join(role.to_snake_case() + ".tres")
		if not ResourceLoader.exists(path):
			push_error("DeckManager: 缺少角色初始卡组：" + path)
			return false
		var config := load(path) as StartingDeckData
		if config == null or config.role_id != role:
			push_error("DeckManager: 初始卡组类型或角色ID不匹配：" + path)
			return false
		configs.append(config)
	var initialized := init_from_starting_decks(configs)
	RunProgress.ensure()
	if initialized and not RunProgress.cards.is_empty():
		draw_pile.assign(RunProgress.cards);discard_pile.clear();draw_pile.shuffle()
	elif initialized:
		RunProgress.cards.assign(draw_pile)
	return initialized

## 支持自定义战斗传入卡组资源；验证完成后再替换运行时牌堆，不修改原始资源。
func init_from_starting_decks(configs: Array[StartingDeckData]) -> bool:
	var next_piles: Dictionary = {}
	var next_cards: Array[CardData] = []
	for config in configs:
		if config == null:
			push_error("DeckManager: 初始卡组资源为空")
			return false
		var errors := config.validation_errors()
		if not errors.is_empty():
			push_error("DeckManager: %s：%s" % [config.resource_path, "；".join(errors)])
			return false
		if next_piles.has(config.role_id):
			push_error("DeckManager: 重复的角色初始卡组：" + config.role_id)
			return false
		next_piles[config.role_id] = true
		next_cards.append_array(config.cards)
	piles = next_piles
	draw_pile = next_cards
	discard_pile.clear()
	draw_pile.shuffle()
	return true

func has_role(role: String) -> bool:
	return piles.has(role)

func draw_common(n: int) -> Array[CardData]:
	var out: Array[CardData] = []
	for i in n:
		if draw_pile.is_empty():
			draw_pile.append_array(discard_pile)
			discard_pile.clear()
			draw_pile.shuffle()
		if draw_pile.is_empty():
			break
		out.append(draw_pile.pop_back())
	return out

func draw(_role: String, n: int) -> Array[CardData]:
	return draw_common(n)

func discard_card(_role: String, cd: CardData) -> void:
	discard_pile.append(cd)

func debug_status() -> String:
	return "公共牌库[抽%d/弃%d]" % [draw_pile.size(), discard_pile.size()]

## 有条件抽牌：候选不足时只洗回一次，原抽牌堆仍保留。
func draw_filtered(count: int, predicate: Callable) -> Array[CardData]:
	var out:Array[CardData]=[]
	var recycled:=false
	while out.size()<count:
		var candidates:Array[int]=[]
		for i in draw_pile.size():
			if predicate.call(draw_pile[i]):candidates.append(i)
		if candidates.is_empty():
			if recycled or discard_pile.is_empty():break
			draw_pile.append_array(discard_pile);discard_pile.clear();draw_pile.shuffle();recycled=true
			continue
		var index:int=candidates.pick_random()
		out.append(draw_pile[index]);draw_pile.remove_at(index)
	return out
func has_filtered(predicate: Callable) -> bool:
	return draw_pile.any(predicate) or discard_pile.any(predicate)
