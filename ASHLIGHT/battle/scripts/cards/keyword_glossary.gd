@tool
class_name KeywordGlossary
extends RefCounted

## 卡牌词条 / Buff 文案 单一来源（真源）。
## 两份消费方共享同一份文字，杜绝日后分叉：
##  - combo_glossary.gd（卡牌快照右侧词条面板）按 CardEffect 导出的关键词查 ENTRIES
##  - unit_buff.gd（单位 buff 悬停提示框）按 buff_id 经 ID_TO_NAME → ENTRIES 解析
##
## 设计纪律（游戏设计师视角）：词条只解释「机制内核」，不堆修饰词。每条回答同一个玩家问题——
## 「这个状态/关键词在我按下这张牌时到底制造了什么新决策/后果」。
## 新增关键词时：在 ENTRIES 追加一行（显示名→说明），并在 ID_TO_NAME 登记对应的 buff_id 映射。

const ENTRIES: Dictionary = {
	"淬毒": "持续至战斗结束。攻击行为成功执行时，额外对玩家施加2层【中毒】，每次行为只施加1次；攻击被反制时不触发。",
	"赃物": "每次主动摸牌时，额外推进1次敌方倒计时，并消耗1层。每次主动摸牌最多触发1次，卡牌效果抽牌、技能和闪回生成不触发。",
	"召唤物": "根据对应的行为自动行动，消耗完行动次数时消失，重复召唤刷新行动次数。",
	"闪回": "临时卡牌副本，获得后的下一张牌打出时销毁。",
	"反制": "对下个行为进行应对，成功时打断该行为。",
	"调节": "每层使战场点数获得修正，触发修正时减少所需层数。",
	"充能": "入手后，每打出1张其他牌获得1点充能。",
	"手影蛇": "当队伍打出状态牌时，对倒计时最少的敌方单位施加{value}层【中毒】，剩余{remaining}次行动。" ,
	"易伤": "受到有效伤害时，额外造成当前层数的伤害。",
	"手影狐": "当队伍打出攻击牌时，对血量最低的敌方单位造成{value}点伤害，剩余{remaining}次行动。",
	"手影鹿": "当队伍打出攻击牌时，对全体敌方单位各造成{value}点伤害，剩余{remaining}次行动。",
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
	"envenomed": "淬毒",
	"loot": "赃物",
	"adjustment": "调节",
	"charge": "充能",
	"morale": "士气",
	"bleed": "出血",
	"poison": "中毒",
	"vulnerable": "易伤",
	"summon_fox": "手影狐",
	"summon_deer": "手影鹿",
	"summon_snake": "手影蛇",
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

static func summon_description(kind: String, value: int, remaining: int) -> String:
	return desc_of(kind).format({"value": value, "remaining": remaining})
