@tool
class_name CardEffect
extends Resource

## 一个可独立配置、按列表顺序结算的原子效果。目标仅在出牌时锁定，不写回资源。
@export_enum("damage", "shield", "heal", "draw", "apply_status", "counter", "haste", "slow", "attack_flashback", "intent_color", "observe_blue", "summon_fox", "summon_deer", "summon_snake", "summon_act", "summon_exhaust", "copy_debuffs", "double_debuffs", "mourn") var kind: String = "damage"
@export_enum("选中敌人", "选中友方", "选中单位", "自身", "全体敌人", "全体友方", "全场", "随机敌方") var target: String = "选中敌人"
@export_range(0, 999) var amount: int = 2
@export var scales_with_number: bool = true
@export_range(1, 99) var hits: int = 1
@export_enum("morale", "bleed", "poison", "vulnerable", "charge") var status_id: String = "bleed"
@export_enum("任意:-1", "红:0", "蓝:1", "黄:2", "绿:3") var counter_color: int = -1
@export var exclude_self: bool = false
@export_enum("无:-1", "红:0", "蓝:1", "黄:2", "绿:3") var excluded_intent_color: int = -1
@export var combo_shield_compensation: bool = false
@export var combo_slow_growth: bool = false
@export var combo_linear_growth: bool = false
@export var combo_cautious_growth: bool = false

const KINDS: Array[String] = ["damage", "shield", "heal", "draw", "apply_status", "counter", "haste", "slow", "attack_flashback", "intent_color", "observe_blue", "summon_fox", "summon_deer", "summon_snake", "summon_act", "summon_exhaust", "copy_debuffs", "double_debuffs", "mourn"]
const TARGETS: Array[String] = ["选中敌人", "选中友方", "选中单位", "自身", "全体敌人", "全体友方", "全场", "随机敌方"]

func needs_selection() -> bool:
	return target.begins_with("选中") and kind not in ["draw", "counter"]

func faction() -> String:
	if target in ["选中敌人", "全体敌人", "随机敌方"]:
		return "敌方"
	if target in ["选中友方", "全体友方", "自身"]:
		return "我方"
	return "全场"

func value(number: int, morale_bonus: int = 0) -> int:
	var base: int = amount * number if scales_with_number else amount
	return base + (morale_bonus if kind in ["damage", "mourn"] else 0)

func keyword_names() -> Array[String]:
	var names: Array[String] = []
	match kind:
		"damage", "counter", "mourn": names.append("伤害")
		"summon_fox", "summon_deer", "summon_snake": names.append(KeywordGlossary.name_of(kind))
		"shield": names.append("护盾")
		"heal": names.append("血量")
		"apply_status": names.append(KeywordGlossary.name_of(status_id))
	return names

func summon_damage(morale: int, combo: int) -> int:
	if kind == "summon_snake":
		return ceili(float(amount) * maxi(1, combo) / 3.0)
	if kind == "summon_fox":
		return ceili(float(amount + morale) * maxi(1, combo) / 3.0)
	return ceili((float(amount) / 3.0 + morale) * maxi(1, combo) / 2.0)

func preview_value(number: int, morale_bonus: int = 0, damage_multiplier: int = 1, baseline_number: int = -1) -> String:
	var n: Variant = value(number, morale_bonus) * (damage_multiplier if kind in ["damage", "mourn"] else 1)
	if combo_linear_growth:
		n *= maxi(1, damage_multiplier)
	if kind in ["summon_fox", "summon_deer", "summon_snake"]:
		n = summon_damage(morale_bonus, damage_multiplier)
	if combo_cautious_growth:
		n += floori(float(maxi(damage_multiplier, 1) - 1) / 3.0)
	if combo_shield_compensation:
		n = floori(float(amount * (maxi(damage_multiplier, 1) - 1)) / 3.0)
	if kind == "attack_flashback" or combo_slow_growth:
		n += floori(float(maxi(damage_multiplier, 1) - 1) / 2.0)
	var baseline: int = 0 if combo_shield_compensation else value(number if baseline_number < 0 else baseline_number, 0)
	if kind in ["summon_fox", "summon_deer", "summon_snake"]:
		baseline = summon_damage(0, 1)
	return EffectTextFormat.changed_value(int(n), baseline)

func text(number: int, morale_bonus: int = 0, damage_multiplier: int = 1, baseline_number: int = -1) -> String:
	var n := preview_value(number, morale_bonus, damage_multiplier, baseline_number)
	var who: String = target.replace("选中", "选中的")
	if target in ["自身", "选中友方", "全体友方"] and kind in ["shield", "heal", "apply_status"] and not exclude_self:
		who = "队伍"
	if exclude_self:
		who += "（自身除外）"
	var repeat: String = "%s次" % hits if hits > 1 else ""
	var line: String
	match kind:
		"summon_fox": line = "召唤【狐】，拥有3次行动。队伍打出攻击牌时，先对血量最低的敌人造成%s点伤害。" % n
		"summon_deer": line = "召唤【鹿】，拥有2次行动。队伍打出攻击牌时，先对全体敌人各造成%s点伤害。" % n
		"summon_snake": line = "召唤【蛇】，拥有3次行动。队伍打出状态牌时，先对倒计时最少的敌人施加%s层【中毒】。" % n
		"summon_exhaust": line = "令当前召唤物连续行动，直至剩余行动次数耗尽。"
		"copy_debuffs": line = "将一个敌人身上各类减益的一半复制给其他敌人，层数向下取整。"
		"double_debuffs": line = "全体敌人的减益层数翻倍。"
		"mourn": line = "对全体敌人造成%s点【伤害】。每个敌人每有一种减益，对其额外造成一次伤害。" % n
		"summon_act": line = "令当前召唤物立即行动一次。"
		"damage": line = "对%s造成%s%s点【伤害】" % [who, repeat, n]
		"shield": line = "为%s获得%s%s点【护盾】" % [who, repeat, n]
		"heal": line = "为%s回复%s%s点【血量】" % [who, repeat, n]
		"intent_color": line = "将选中敌人的当前意图染为%s色" % ["红", "蓝", "黄", "绿"][clampi(amount, 0, 3)]
		"observe_blue": line = "查看公共牌库顶%s张牌，蓝色牌加入手牌，其他颜色牌生成闪回副本加入手牌。" % n
		"attack_flashback": line = "随机获得%s张攻击牌闪回。" % n
		"draw": line = "从公共牌库抽%s张牌" % n
		"apply_status": line = "为%s施加%s%s层【%s】" % [who, repeat, n, KeywordGlossary.name_of(status_id)]
		"counter":
			line = "【反制】攻击，成功时对其造成%s点【伤害】" % n
		"haste": line = "使%s倒计时减少%s%s" % [who, repeat, n]
		"slow":
			line = "使%s倒计时增加%s%s" % [who, repeat, n]
			if excluded_intent_color >= 0:
				line = "使意图非%s色的存活敌人倒计时增加%s" % [["红", "蓝", "黄", "绿"][excluded_intent_color], n]
		_: line = "无效效果：" + kind
	if hits > 1 and kind in ["draw", "counter"]:
		line += "（重复%s次）" % hits
	return line

func validation_errors() -> PackedStringArray:
	var errors := PackedStringArray()
	if kind not in KINDS: errors.append("未知效果类型：" + kind)
	if target not in TARGETS: errors.append("未知目标：" + target)
	if amount < 0 or hits < 1: errors.append("效果数值不能为负，次数至少为1")
	if kind == "apply_status" and not KeywordGlossary.ID_TO_NAME.has(status_id): errors.append("未知状态：" + status_id)
	if kind == "counter" and (counter_color < -1 or counter_color > 3): errors.append("反制颜色必须为-1至3")
	if kind in ["haste", "slow"] and faction() != "敌方": errors.append("倒计时效果目前只支持敌方")
	if target == "自身" and exclude_self: errors.append("自身目标不能排除自身")
	return errors
