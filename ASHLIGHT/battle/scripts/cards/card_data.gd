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
## 新卡按关键伤害数值成长，整牌只执行一次。
@export var combo_damage_growth: bool = false
@export var blue_counter_flashback: bool = false
@export var blue_status_counter: bool = false
@export var execute_once: bool = false
@export var records_damage: bool = false
## 是否具有充能词条；具体点数只保存在本次入手的 HandCard 中。
@export var has_charge: bool = false
@export_range(0, 20) var rocket_number: int = 0
@export_file("*.png") var art_path: String = ""
## 补充说明，不再重复填写数值公式。
@export_multiline var description: String = ""
## 文案占位符对应效果序号，仅用于显示。
@export_multiline var effect_display_template: String = ""

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

func effect_text(number: int = -1, morale_bonus: int = 0, combo: int = 1, recorded_damage: int = 11) -> String:
	return finish_effect_text(_raw_effect_text(number, morale_bonus, combo, recorded_damage))

static func finish_effect_text(text: String) -> String:
	var result := text.strip_edges()
	if result.is_empty(): return result
	return result.trim_suffix("。").trim_suffix(".").trim_suffix("；") + "。"

func _raw_effect_text(number: int, morale_bonus: int, combo: int, recorded_damage: int) -> String:
	if rocket_number > 0: return rocket_text(0, morale_bonus, combo, 0)
	if not effect_display_template.is_empty():
		var values: Array[String] = []
		for effect in effects:
			var multiplier := maxi(combo, 1) if combo_damage_growth or effect.kind == "attack_flashback" or effect.combo_shield_compensation or effect.combo_slow_growth or effect.combo_cautious_growth or effect.combo_linear_growth or effect.kind in ["summon_fox", "summon_deer", "summon_snake"] else 1
			values.append(effect.preview_value(base_number if number < 0 else number, morale_bonus, multiplier, base_number))
		if blue_status_counter:
			values[0] = EffectTextFormat.changed_value((7 + morale_bonus) * maxi(combo, 1), 7)
		elif records_damage:
			values[0] = EffectTextFormat.changed_value((recorded_damage + morale_bonus) * maxi(combo, 1), 11)
		elif blue_counter_flashback:
			values[0] = EffectTextFormat.changed_value(1 + floori(float(maxi(combo, 1) - 1) / 2.0), 1)
		return effect_display_template.format(values)
	if blue_status_counter:
		return "【反制】状态，成功时对其造成%s点【伤害】。" % EffectTextFormat.changed_value((7 + morale_bonus) * maxi(combo, 1), 7)
	if records_damage:
		return "对一个敌人造成%s点【伤害】。本场最高伤害记录：%s，后续以记录作为基础伤害。" % [EffectTextFormat.changed_value((recorded_damage + morale_bonus) * maxi(combo, 1), 11), EffectTextFormat.changed_value(recorded_damage, 11)]
	if blue_counter_flashback:
		return "【反制】攻击，成功时获得%s张蓝色牌【闪回】。" % EffectTextFormat.changed_value(1 + floori(float(maxi(combo, 1) - 1) / 2.0), 1)
	var lines := PackedStringArray()
	for effect in effects:
		if effect != null:
			if effect.combo_shield_compensation and combo <= 1:
				continue
			lines.append(effect.text(base_number if number < 0 else number, morale_bonus, maxi(combo, 1) if combo_damage_growth or effect.kind == "attack_flashback" or effect.combo_shield_compensation or effect.combo_slow_growth or effect.combo_cautious_growth or effect.combo_linear_growth or effect.kind in ["summon_fox", "summon_deer", "summon_snake"] else 1, base_number))
	return "；\n".join(lines)

func keyword_names() -> Array[String]:
	var names: Array[String] = []
	if has_charge: names.append("充能")
	for effect in effects:
		if effect == null: continue
		for keyword in effect.keyword_names():
			if not keyword.is_empty() and not names.has(keyword): names.append(keyword)
	return names

func needs_selection() -> bool:
	return effects.any(func(effect: CardEffect) -> bool: return effect != null and effect.needs_selection() and effect.target != "选中友方")

func rules_text() -> String:
	if blue_counter_flashback:
		return "布置时记录连锁，闪回数量为1／1／2／2；覆盖下一次敌方行动。"
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
		if effect.needs_selection() and effect.faction() != "全场" and effect.target != "选中友方":
			if selected_faction != "" and selected_faction != effect.faction(): errors.append("一次出牌只能选一个单位，不能同时要求选中敌人和友方")
			selected_faction = effect.faction()
	return errors

func rocket_text(q: int, m: int, c: int, shield: int, displayed_charge: int = -1) -> String:
	var text := _rocket_body(q, m, c, shield)
	if has_charge:
		var stacks: int = q if displayed_charge < 0 else displayed_charge
		text += "【充能】" + (str(stacks) if stacks > 0 else "") + "。"
	return text

func _rocket_body(q: int, m: int, c: int, _shield: int) -> String:
	c = maxi(1, c)
	var extra: int = floori(float((7 if rocket_number == 18 else 11) * (c - 1)) / 3)
	var suffix := "获得%s点护盾。" % EffectTextFormat.changed_value(extra, 0) if extra > 0 else ""
	match rocket_number:
		11: return "对敌人造成2次%s点伤害，每层充能增加1点伤害。" % EffectTextFormat.changed_value((2 + q + m) * c, 2)
		12: return "获得%s点护盾，【充能】大于3时，额外获得%s点护盾。" % [EffectTextFormat.changed_value(5*c, 5), EffectTextFormat.changed_value(5*c, 5)]
		13: return "【反制】防御，成功时获得%s层【调节】。" % EffectTextFormat.changed_value(1 + floori(float(c-1)/3), 1)
		14: return "将本牌一半【充能】给随机一张非本牌的【充能】牌，最多3点。"
		15: return "对全体敌人造成%s点伤害，并且消耗护盾，每消耗1点护盾额外造成1点伤害，最多额外消耗5点护盾。" % EffectTextFormat.changed_value((5+m)*c, 5)
		16: return "对敌人造成%s点伤害，与战场点数相同时，获得1层【调节】。" % EffectTextFormat.changed_value((7+m)*c, 7)
		17: return "【充能】大于3时，使所有留手的充能牌各获得%s点充能。" % EffectTextFormat.changed_value(2+floori(float(c-1)/2),2)
		18: return "清空所有手牌【充能】，转换其总量一半为【充能】buff。" + suffix
		19: return "对敌人造成1次%s点伤害。每点【充能】额外造成一次伤害。" % EffectTextFormat.changed_value((6+m)*c,6)
		20: return "获得2层【调节】。" + suffix
	return ""
