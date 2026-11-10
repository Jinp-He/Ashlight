class_name AllySkills
extends RefCounted

## 友方角色技能：点击角色查看，再次点击使用；无需选择手牌。
## 使用技能不算动作（不推进敌人倒计时、不影响 Combo），之后进入冷却，冷却按玩家动作递减。
## 冷却由有效出牌或摸牌按钮推进，卡牌效果抽牌不推进。
const SKILLS: Dictionary = {
	"Zhouzhou": {"name": "行云", "passive": "避柳", "desc": "将战场颜色改为蓝色，保留当前点数。", "cooldown": 4},
	"Irene": {"name": "灌注", "passive": "幽灵诡计", "desc": "获得1张与战场颜色相同的卡牌【闪回】。", "cooldown": 3},
	"Rocket": {"name": "反应堆", "passive": "退火", "desc": "随机将1张手牌改为战场颜色，优先选择具有【充能】词条的异色牌。", "cooldown": 3},
}
static func get_skill(uid: String) -> Dictionary:
	return SKILLS.get(uid, {})

## 旧的指定手牌技能接口保留为空操作，避免旧调用恢复点数增幅。
static func can_apply(_uid: String, _hc: HandCard) -> bool:
	return false

static func apply(_uid: String, _hc: HandCard) -> void:
	pass
