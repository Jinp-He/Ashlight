@tool
class_name EnemyIntentData
extends Resource

## 敌方一张意图牌的静态配置。伤害作用于友方队伍，护盾作用于怪物自身。
@export var id: String = ""
@export var intent_name: String = ""
## 旧资源自动按效果推导；新资源可独立配置分类。
@export_enum("按效果自动:-1", "攻击:0", "防御:1", "状态:2") var behavior_type: int = -1
@export_enum("红:0", "蓝:1", "黄:2", "绿:3") var color: int = 0
@export_range(1, 99) var number: int = 1
@export_enum("damage", "shield", "loot", "steal_shield", "timed_shield", "envenom") var stat: String = "damage"
@export_range(0, 9999) var value: int = 1
@export_range(1, 99) var hits: int = 1
@export_range(0, 99) var loot_on_attack: int = 0
@export_range(0, 99) var cooldown_on_hp_hit: int = 0
## 该意图出手后，下次倒计时在怪物基础 countdown 上额外增加的格数。
@export_range(0, 99) var delay: int = 0

## 新建独立的运行时快照；目标和施法者不写入可共享的配置资源。
func make_payload(caster_id: String, target_id: String) -> Dictionary:
	return {"intent_id": id, "uid": caster_id, "target": target_id, "name": intent_name,
		"color": color, "number": number, "stat": stat, "behavior_type": resolved_behavior_type(), "value": value, "hits": hits, "loot_on_attack": loot_on_attack, "delay": delay, "cooldown_on_hp_hit": cooldown_on_hp_hit}

func resolved_behavior_type() -> int:
	return behavior_type if behavior_type >= 0 else (0 if stat == "damage" else 1)

func validation_errors() -> PackedStringArray:
	var errors := PackedStringArray()
	if id.is_empty(): errors.append("意图ID不能为空")
	if intent_name.is_empty(): errors.append("意图名称不能为空")
	if color < 0 or color > 3: errors.append("意图颜色必须为0至3")
	if number < 1: errors.append("意图点数至少为1")
	if stat not in ["damage", "shield", "loot", "steal_shield", "timed_shield", "envenom"]: errors.append("未知意图效果：" + stat)
	if value < 0: errors.append("意图数值不能为负")
	if delay < 0: errors.append("额外倒计时不能为负")
	return errors
