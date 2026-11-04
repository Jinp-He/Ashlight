@tool
class_name MonsterData
extends Resource

## 怪物配置。场景中的 unit_id 对应 id，意图数组按顺序循环，可重复引用同一意图。
@export var id: String = ""
@export var monster_name: String = ""
@export_range(1, 99999) var max_hp: float = 30.0
@export_range(1, 99) var countdown: int = 3
@export var intents: Array[EnemyIntentData] = []

func intent_at(action_count: int) -> EnemyIntentData:
	if intents.is_empty(): return null
	return intents[maxi(0, action_count) % intents.size()]

func validation_errors() -> PackedStringArray:
	var errors := PackedStringArray()
	if id.is_empty(): errors.append("怪物ID不能为空")
	if not is_finite(max_hp) or max_hp <= 0: errors.append("最大生命值必须大于0且为有限数")
	if countdown < 1: errors.append("基础倒计时至少为1")
	if intents.is_empty(): errors.append("至少需要一个意图")
	for i in intents.size():
		var intent := intents[i]
		if intent == null:
			errors.append("第%d个意图为空" % (i + 1))
			continue
		for error in intent.validation_errors():
			errors.append("第%d个意图：%s" % [i + 1, error])
	return errors
