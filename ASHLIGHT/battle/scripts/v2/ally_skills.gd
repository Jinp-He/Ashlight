class_name AllySkills
extends RefCounted

## 友方角色技能：放入一张当前接不上的手牌来启动，改写这张牌的颜色或点数。
## 使用技能不算动作（不推进敌人倒计时、不影响 Combo），之后进入冷却，冷却按玩家动作递减。
## 占位数值，正式数据表到位后替换。
const SKILLS: Dictionary = {
	"Zhouzhou": {"name": "变色", "desc": "随机换成另一种颜色", "cooldown": 4},
	"Irene": {"name": "削减", "desc": "点数 -1", "cooldown": 3},
	"Rocket": {"name": "增幅", "desc": "点数 +1", "cooldown": 3},
}
const NUMBER_MIN: int = 1
## 增幅允许超过卡表上限 5，用来接敌人 6、7 点的牌。
const NUMBER_MAX: int = 9

static func get_skill(uid: String) -> Dictionary:
	return SKILLS.get(uid, {})

## 技能能否作用在这张牌上（不考虑冷却与可出判定）。
static func can_apply(uid: String, hc: HandCard) -> bool:
	match uid:
		"Irene":
			return hc.base_number > NUMBER_MIN
		"Rocket":
			return hc.base_number < NUMBER_MAX
		"Zhouzhou":
			return true
	return false

## 改写这张牌并刷新卡面。调用方负责先检查 can_apply。
static func apply(uid: String, hc: HandCard) -> void:
	match uid:
		"Zhouzhou":
			var colors: Array[int] = []
			for color in ChainRules.COLOR_NAMES.size():
				if color != hc.cost_color:
					colors.append(color)
			hc.cost_color = colors.pick_random()
			hc.card.cost_color = hc.cost_color
		"Irene":
			hc.base_number -= 1
		"Rocket":
			hc.base_number += 1
