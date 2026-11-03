class_name CardDatabase
extends RefCounted

## 卡牌数据入口：当前提供占位卡，后续替换为正式卡牌表。
## 后续可无缝替换为 .tres 资源加载，不影响 HandManager（单一数据入口，便于切换来源）。

## 全部卡牌。
func get_all() -> Array[CardData]:
	return PlaceholderCards.all_cards()

## 按角色筛选（周周/艾琳/火炮），供公共牌库初始化使用。
func get_by_role(role: String) -> Array[CardData]:
	var out: Array[CardData] = []
	for c in get_all():
		if c.belong_to == role:
			out.append(c)
	return out
