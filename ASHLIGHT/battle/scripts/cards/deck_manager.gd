class_name DeckManager
extends RefCounted

var piles: Dictionary = {}
var draw_pile: Array[CardData] = []
var discard_pile: Array[CardData] = []

func init_decks(roles: Array[String]) -> void:
	piles.clear()
	draw_pile.clear()
	discard_pile.clear()
	for role in roles:
		piles[role] = true
		for card in CardDatabase.new().get_by_role(role):
			draw_pile.append(card)
	draw_pile.shuffle()

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
