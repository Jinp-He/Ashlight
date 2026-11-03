class_name KeywordGlossary
extends RefCounted

## 卡牌词条 / Buff 文案 单一来源（真源）。
## 两份消费方共享同一份文字，杜绝日后分叉：
##  - combo_glossary.gd（卡牌快照右侧词条面板）按卡面【…】显示名查 ENTRIES
##  - unit_buff.gd（单位 buff 悬停提示框）按 buff_id 经 ID_TO_NAME → ENTRIES 解析
##
## 设计纪律（游戏设计师视角）：词条只解释「机制内核」，不堆修饰词。每条回答同一个玩家问题——
## 「这个状态/关键词在我按下这张牌时到底制造了什么新决策/后果」。
## 新增关键词时：在 ENTRIES 追加一行（显示名→说明），并在 ID_TO_NAME 登记对应的 buff_id 映射。

const ENTRIES: Dictionary = {
	# 攻击牌基础通道——出现极频繁，仍然解释，因为新手需要「伤害/护盾/血量」的边界定义。
	"伤害": "攻击行为造成的直接数值削减，命中后优先扣减护盾、再扣生命。",
	"护盾": "先于生命抵消伤害，可以叠加，不随时间自然消退。",
	"血量": "恢复生命数值，不超过单位生命上限。",
	# 状态类关键词——这是词条面板真正要服务的内容（机制内核解释）。
	# 约定：所有效果默认「作用于持有单位自身」，故文案不再写「持有单位」三字。
	"士气": "增益，每层使攻击牌关键数值 +1，攻击结算后全部消耗。",
	"出血": "减益，全场时间每推进一格，受到等同层数的伤害，并减少 1 层。",
	"中毒": "减益，每次行动后受到等同层数的伤害；护盾可 1:1 抵消层数（每抵消 1 层消耗 1 点护盾）。",
}

## buff_id（战斗结算 _apply_stat / 测试注入用）→ 显示名（对应 ENTRIES 的 key）。
## 战斗里的真实 buff id 即 morale / bleed / poison；测试池也改用这些 id，统一走此处解析。
const ID_TO_NAME: Dictionary = {
	"morale": "士气",
	"bleed": "出血",
	"poison": "中毒",
}

## 按 buff_id 取显示名；未知返回空串（调用方回退为 buff_id 本身）。
static func name_of(buff_id: String) -> String:
	return ID_TO_NAME.get(buff_id, "")

## 按 buff_id 取说明文案；未知返回空串。
static func desc_of(buff_id: String) -> String:
	var nm: String = ID_TO_NAME.get(buff_id, "")
	if nm == "":
		return ""
	return ENTRIES.get(nm, "")
