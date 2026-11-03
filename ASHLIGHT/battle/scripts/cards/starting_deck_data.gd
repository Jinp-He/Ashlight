@tool
class_name StartingDeckData
extends Resource

## 角色初始卡组。每个数组元素是一张实体牌；重复引用同一 CardData 表示多张副本。
@export_enum("Zhouzhou", "Irene", "Rocket") var role_id: String = "Zhouzhou"
@export var deck_name: String = ""
@export var cards: Array[CardData] = []

func validation_errors() -> PackedStringArray:
	var errors := PackedStringArray()
	if role_id.is_empty(): errors.append("角色ID不能为空")
	if cards.is_empty(): errors.append("初始卡组不能为空")
	for i in cards.size():
		var card := cards[i]
		if card == null:
			errors.append("第%d张卡为空" % (i + 1))
			continue
		if card.belong_to != role_id:
			errors.append("第%d张卡%s属于%s，与卡组角色%s不一致" % [i + 1, card.id, card.belong_to, role_id])
		for error in card.validation_errors():
			errors.append("第%d张卡%s：%s" % [i + 1, card.id, error])
	return errors
