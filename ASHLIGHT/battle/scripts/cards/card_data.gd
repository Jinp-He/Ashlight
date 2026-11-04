@tool
class_name CardData
extends Resource

@export_group("卡牌信息")
@export var id: String = ""
@export var card_name: String = ""
@export_enum("Zhouzhou", "Irene", "Rocket") var belong_to: String = "Zhouzhou"
## 仅控制卡面分类；士气加成由伤害效果决定。
@export_enum("攻击:0", "状态:1", "防御/功能:2") var card_type: int = 0
@export_enum("1:1", "2:2", "3:3") var rarity: int = 1
@export var enhanced: bool = false
@export_file("*.png") var art_path: String = ""
## 补充说明，不再重复填写数值公式。
@export_multiline var description: String = ""

@export_group("接牌")
@export_enum("红:0", "蓝:1", "黄:2", "绿:3") var uno_color: int = 0
@export_range(1, 5) var base_number: int = 1
@export_range(0, 4) var number_spread: int = 1

@export_group("效果（按顺序执行）")
@export var effects: Array[CardEffect] = []

const NUMBER_MIN: int = 1
const NUMBER_MAX: int = 5

func roll_number() -> int:
	return clampi(base_number + randi_range(-number_spread, number_spread), NUMBER_MIN, NUMBER_MAX)

func effect_text(number: int = -1, morale_bonus: int = 0) -> String:
	var lines := PackedStringArray()
	for effect in effects:
		if effect != null:
			lines.append(effect.text(base_number if number < 0 else number, morale_bonus))
	return "；\n".join(lines)

func keyword_names() -> Array[String]:
	var names: Array[String] = []
	for effect in effects:
		if effect == null: continue
		for keyword in effect.keyword_names():
			if not keyword.is_empty() and not names.has(keyword): names.append(keyword)
	return names

func needs_selection() -> bool:
	return effects.any(func(effect: CardEffect) -> bool: return effect != null and effect.needs_selection())

func rules_text() -> String:
	var lines := PackedStringArray()
	for effect in effects:
		if effect == null: continue
		lines.append("点数 × %d" % effect.amount if effect.scales_with_number else "固定 %d" % effect.amount)
	return " / ".join(lines)

func validation_errors() -> PackedStringArray:
	var errors := PackedStringArray()
	if effects.is_empty(): errors.append("至少需要一个效果")
	var selected_faction: String = ""
	for i in effects.size():
		var effect := effects[i]
		if effect == null:
			errors.append("效果%d为空" % (i + 1))
			continue
		for error in effect.validation_errors(): errors.append("效果%d：%s" % [i + 1, error])
		if effect.needs_selection() and effect.faction() != "全场":
			if selected_faction != "" and selected_faction != effect.faction(): errors.append("一次出牌只能选一个单位，不能同时要求选中敌人和友方")
			selected_faction = effect.faction()
	return errors
