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
	return init_from_starting_decks(configs)

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
