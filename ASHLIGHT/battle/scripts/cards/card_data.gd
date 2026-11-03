class_name CardData
extends Resource


## 下拉框字段在 .tres 里仍存数字 / 字符串；选项值与旧数据一一对应，改选项时不要改动已有的值。
@export var id: String = ""
@export var card_name: String = ""
@export_enum("Zhouzhou", "Irene", "Rocket") var belong_to: String = "Zhouzhou"
## 仅供美术态/兜底；效果以 effect_id 为准。
@export_enum("攻击:0", "状态:1", "防御/功能:2") var card_type: int = 0
## 玩法字段，菱形色由 cost_color 驱动。
@export_enum("即时:0", "执行:1") var cost_type: int = 0
@export var cost_value: int = 1
## 费用色(菱形色+1+1匹配轴)：蓝(00a1ff) 橙(ff8c33) 绿(70ab3b)。
@export_enum("蓝:0", "橙:1", "绿:2") var cost_color: int = 0
## 兼容性保留字段，目标由阵营与范围决定。
@export_enum("SingleEnemy", "Self") var target_type: String = "SingleEnemy"
@export_enum("单体", "群体") var scope: String = "单体"
## target_faction 相对施法者：敌方(对立阵营) / 我方(同阵营) / 全场(全部)。
## 范围多名目标时：怪物=随机选一(att/bleed)；角色卡=玩家点选(蓝卡手动)；无有效目标→角色卡灰掉不可打(全场卡恒可打)。
@export_enum("敌方", "我方", "全场") var target_faction: String = "敌方"
## effect_id 指向 EFFECT_ARCHETYPES 中的原型 key；关键数值落在 key_value
## effects_text 由 derived_effect_text() 自动重建，不再手填原始标签。
## 新增原型时同步补进这里的选项。
@export_enum("damage_single", "damage_multi", "damage_all", "shield", "heal", "haste", "slow", "draw", "move", "morale", "bleed", "counter") var effect_id: String = "damage_single"
@export var key_value: int = 2
@export var hits: int = 1
@export_enum("1:1", "2:2", "3:3") var rarity: int = 1
@export var enhanced: bool = false
@export var art_id: String = ""                # 卡面文件名（无下划线，如 Zhouzhou001.png）
@export var effects_text: String = ""          # 效果标签（derived_effect_text 重建后的展示文案，解析兜底用）
@export var keywords: String = ""
@export var special: String = ""              # 预留扩展标记，目前占位卡不使用
@export var wait_ticks: int = 0
@export var target_self: bool = false
@export var exclude_self: bool = false
@export var description: String = ""
## 接牌玩法：颜色 + 基础点数。抽到手时在 ±number_spread 内浮动一次。
@export_enum("红:0", "蓝:1", "黄:2", "绿:3") var uno_color: int = 0
@export var base_number: int = 1
@export var number_spread: int = 1
## true：效果值 = 点数 × key_value；false：效果值固定为 key_value（如抽牌）。
@export var scales_with_number: bool = true
## 反制牌：敌人下个动作若是这个颜色的攻击（任意 = -1）则反制，并造成 key_value 点伤害。
@export_enum("任意:-1", "红:0", "蓝:1", "黄:2", "绿:3") var counter_color: int = -1

const NUMBER_MIN: int = 1
const NUMBER_MAX: int = 5

## 抽到手时确定本张的基础点数，之后直到离开手牌都不变。
func roll_number() -> int:
	return clampi(base_number + randi_range(-number_spread, number_spread), NUMBER_MIN, NUMBER_MAX)

func effect_value(current_number: int) -> int:
	return current_number * key_value if scales_with_number else key_value

## 可复用效果原型（8 款式 + 衍生）。stat 决定 apply_card_effect 落到哪个数值通道。
## tpl：阵营/范围前缀为纯文本(不带【】)；关键数值位为纯数字(不带【】，由卡面加左右留空位+扁桃体放大)；
##   仅词条(伤害/护盾/血量/士气/出血…)保留【…】(黑色，不另上色)；damage_multi 用 %d(段数)+%s(每段威力含箭头)。
const EFFECT_ARCHETYPES: Dictionary = {
	"damage_single": {"ct": 0, "stat": "damage", "tpl": "对%s造成%s点【伤害】"},
	"damage_multi":  {"ct": 0, "stat": "damage", "tpl": "对%s造成%d次%s点【伤害】"},
	"damage_all":    {"ct": 0, "stat": "damage", "tpl": "对%s造成%s点【伤害】"},
	"shield":        {"ct": 2, "stat": "shield", "tpl": "获得%s点【护盾】"},
	"heal":          {"ct": 2, "stat": "heal",   "tpl": "回复%s点【血量】"},
	"haste":         {"ct": 1, "stat": "haste", "tpl": "使%s倒计时减少%s"},
	"slow":          {"ct": 1, "stat": "slow", "tpl": "使%s倒计时增加%s"},
	"draw":          {"ct": 1, "stat": "draw", "tpl": "从公共牌库抽%s张牌"},
	"move":          {"ct": 2, "stat": "move",   "tpl": "使单个目标前进%s格"},
	"morale":        {"ct": 1, "stat": "morale", "tpl": "为%s获得%s层【士气】"},
	"bleed":         {"ct": 1, "stat": "bleed",  "tpl": "对%s造成%s层【出血】"},
	"counter":       {"ct": 1, "stat": "counter", "tpl": "敌人下个动作若是%s攻击：反制，并造成%s点【伤害】"},
}

## 返回原型对应 stat（未知→"damage" 兜底）。
func archetype_stat() -> String:
	var a: Dictionary = EFFECT_ARCHETYPES.get(effect_id, {}) as Dictionary
	if a.is_empty():
		return "damage"
	return (a.get("stat", "damage") as String)

## override_key>=0 时用它替换 key_value（连锁提升数值时由 HandManager 调用，显示放大后的数值）。
## morale_bonus>=0 时把士气加成叠加到关键数值上（攻击牌实时显示「基础+士气」；非攻击牌由调用方传 0）。
## 关键数值后附升降箭头：显示值 > 基础值(key_value，含士气/连锁提升)→▲；<→▼；相等→空。
## base_override>=0 时作为箭头比较基准（接牌玩法传入按点数算出的值，不显示箭头）。
func derived_effect_text(override_key: int = -1, override_hits: int = -1, morale_bonus: int = 0, base_override: int = -1) -> String:
	var a: Dictionary = EFFECT_ARCHETYPES.get(effect_id, {}) as Dictionary
	if a.is_empty():
		return _legacy_effect_text()
	var base_key: int = base_override if base_override >= 0 else key_value
	var key: int = (override_key if override_key >= 0 else key_value) + morale_bonus
	var h: int = override_hits if override_hits >= 0 else hits
	var arrow: String = _arrow_for(key, base_key)
	var key_str: String = "%d%s" % [key, arrow]
	var prefix: String = ""
	if not target_self:
		prefix = target_faction   # 阵营前缀为纯文本，不带【】
	match effect_id:
		"damage_multi":
			return (a["tpl"] as String) % [prefix, h, key_str]
		"shield", "heal", "draw":
			return (a["tpl"] as String) % [key_str]
		"counter":
			var color: String = "任意" if counter_color < 0 else ChainRules.color_name(counter_color) + "色"
			return (a["tpl"] as String) % [color, key_str]
		_:
			return (a["tpl"] as String) % [prefix, key_str]

## 关键数值升降箭头：显示值 > 基础值(卡牌原始 key_value，含士气加成/连锁提升)→▲(增益)；
## 显示值 < 基础值(被削减)→▼；相等→空。供卡面实时呈现数值涨跌。
func _arrow_for(key: int, base: int) -> String:
	if key > base:
		return "▲"
	elif key < base:
		return "▼"
	return ""

## 连锁提升后（已 ×M(ring_count)）的卡面文字：先叠加士气加成到基础值，再一次性乘 M(ring_count) 重建。
## morale_bonus 由调用方按「该卡是否为攻击牌」计算（攻击牌=施法者士气层数×每层加成，非攻击牌=0）。
func boosted_text(ring_count: int, morale_bonus: int = 0) -> String:
	var m: float = 1.0 + 0.25 * float(ring_count) + 0.05 * float(ring_count) * float(ring_count)
	var boosted: int = int(roundf(float(key_value + morale_bonus) * m))
	return derived_effect_text(boosted, -1, 0)

## 兼容旧数据（无 effect_id）：按 card_type 占位（攻击2伤/防御2盾/状态2层），后续所有卡都应补全 effect_id。
## 阵营/数值均不带【】；仅词条保留【…】(黑色)。
func _legacy_effect_text() -> String:
	return effects_text
