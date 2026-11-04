@tool
class_name CardEffect
extends Resource

## 一个可独立配置、按列表顺序结算的原子效果。目标仅在出牌时锁定，不写回资源。
@export_enum("damage", "shield", "heal", "draw", "apply_status", "counter", "haste", "slow") var kind: String = "damage"
@export_enum("选中敌人", "选中友方", "选中单位", "自身", "全体敌人", "全体友方", "全场") var target: String = "选中敌人"
@export_range(0, 999) var amount: int = 2
@export var scales_with_number: bool = true
@export_range(1, 99) var hits: int = 1
@export_enum("morale", "bleed", "poison") var status_id: String = "bleed"
@export_enum("任意:-1", "红:0", "蓝:1", "黄:2", "绿:3") var counter_color: int = -1
@export var exclude_self: bool = false

const KINDS: Array[String] = ["damage", "shield", "heal", "draw", "apply_status", "counter", "haste", "slow"]
const TARGETS: Array[String] = ["选中敌人", "选中友方", "选中单位", "自身", "全体敌人", "全体友方", "全场"]

func needs_selection() -> bool:
	return target.begins_with("选中") and kind not in ["draw", "counter"]

func faction() -> String:
	if target in ["选中敌人", "全体敌人"]:
		return "敌方"
	if target in ["选中友方", "全体友方", "自身"]:
		return "我方"
	return "全场"

func value(number: int, morale_bonus: int = 0) -> int:
	var base: int = amount * number if scales_with_number else amount
	return base + (morale_bonus if kind == "damage" else 0)

func keyword_names() -> Array[String]:
	var names: Array[String] = []
	match kind:
		"damage", "counter": names.append("伤害")
		"shield": names.append("护盾")
		"heal": names.append("血量")
		"apply_status": names.append(KeywordGlossary.name_of(status_id))
	return names

func text(number: int, morale_bonus: int = 0) -> String:
	var n: int = value(number, morale_bonus)
	var who: String = target.replace("选中", "选中的")
	if exclude_self:
		who += "（自身除外）"
	var repeat: String = "%d次" % hits if hits > 1 else ""
	var line: String
	match kind:
		"damage": line = "对%s造成%s%d点【伤害】" % [who, repeat, n]
		"shield": line = "为%s获得%s%d点【护盾】" % [who, repeat, n]
		"heal": line = "为%s回复%s%d点【血量】" % [who, repeat, n]
		"draw": line = "从公共牌库抽%d张牌" % n
		"apply_status": line = "为%s施加%s%d层【%s】" % [who, repeat, n, KeywordGlossary.name_of(status_id)]
		"counter":
			var color: String = "任意" if counter_color < 0 else (["红", "蓝", "黄", "绿"][counter_color] + "色" if counter_color < 4 else "无效颜色")
			line = "敌人下个动作若是%s攻击：反制，并造成%d点【伤害】" % [color, n]
		"haste": line = "使%s倒计时减少%s%d" % [who, repeat, n]
		"slow": line = "使%s倒计时增加%s%d" % [who, repeat, n]
		_: line = "无效效果：" + kind
	if hits > 1 and kind in ["draw", "counter"]:
		line += "（重复%d次）" % hits
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
