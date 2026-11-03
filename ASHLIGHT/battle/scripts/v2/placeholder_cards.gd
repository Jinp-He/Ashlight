class_name PlaceholderCards
extends RefCounted

## 临时测试数据（24 张，每角色 8 张）。正式卡牌表到位后只替换 CardDatabase 的数据入口。
## 颜色：0红 1蓝 2黄 3绿。key_value 为每点倍率；scales_with_number=false 时为固定值。
## 反制牌第 8 列为要反制的攻击颜色（-1 = 任意），key_value 为反制伤害。
## 定位：Rocket 直接伤害 / Irene 出血 / Zhouzhou 护盾与抽牌。
const SPECS: Dictionary = {
	"Rocket": [
		["重拳", 0, 3, "damage_single", 2, "敌方", "单体"],
		["速射", 1, 1, "damage_single", 2, "敌方", "单体"],
		["横扫", 2, 4, "damage_all", 1, "敌方", "群体"],
		["突刺", 3, 2, "damage_single", 2, "敌方", "单体"],
		["爆破", 0, 5, "damage_single", 2, "敌方", "单体"],
		["连打", 1, 4, "damage_multi", 1, "敌方", "单体"],
		["冲锋", 2, 2, "damage_single", 2, "敌方", "单体"],
		["震地", 3, 5, "damage_all", 1, "敌方", "群体"],
	],
	"Irene": [
		["截击", 0, 2, "counter", 5, "我方", "单体", 0],
		["血刃", 1, 3, "bleed", 1, "敌方", "单体"],
		["放血", 2, 1, "bleed", 2, "敌方", "单体"],
		["灼伤", 3, 4, "bleed", 1, "敌方", "单体"],
		["血雾", 0, 4, "bleed", 1, "敌方", "群体"],
		["刺击", 1, 5, "damage_single", 1, "敌方", "单体"],
		["冥想", 2, 3, "draw", 1, "我方", "单体"],
		["拆招", 3, 1, "counter", 3, "我方", "单体", -1],
	],
	"Zhouzhou": [
		["格挡", 0, 1, "shield", 3, "我方", "单体"],
		["守护", 1, 2, "shield", 2, "我方", "单体"],
		["坚壁", 2, 5, "shield", 2, "我方", "单体"],
		["整理", 3, 3, "draw", 2, "我方", "单体"],
		["侦察", 0, 2, "draw", 1, "我方", "单体"],
		["盾反", 1, 3, "counter", 5, "我方", "单体", 1],
		["屏障", 2, 2, "shield", 1, "我方", "群体"],
		["回旋", 3, 4, "shield", 2, "我方", "单体"],
	],
}

static func all_cards() -> Array[CardData]:
	var result: Array[CardData] = []
	for role in SPECS:
		var specs: Array = SPECS[role]
		for index in specs.size():
			var spec: Array = specs[index]
			var cd := CardData.new()
			cd.id = "placeholder_%s_%d" % [role, index]
			cd.card_name = spec[0]
			cd.belong_to = role
			cd.uno_color = spec[1]
			cd.base_number = spec[2]
			cd.effect_id = spec[3]
			cd.key_value = spec[4]
			cd.target_faction = spec[5]
			cd.scope = spec[6]
			cd.scales_with_number = cd.effect_id != "draw" and cd.effect_id != "counter"
			if spec.size() > 7:
				cd.counter_color = spec[7]
			cd.hits = 2 if cd.effect_id == "damage_multi" else 1
			cd.target_self = cd.effect_id == "draw" or cd.effect_id == "counter"
			cd.target_type = "Self" if cd.target_self else "SingleEnemy"
			cd.card_type = 0 if cd.effect_id.begins_with("damage") else (2 if cd.effect_id == "shield" else 1)
			cd.cost_value = cd.base_number
			cd.rarity = 1
			cd.art_id = "%s001.png" % role
			if cd.scales_with_number:
				cd.description = "效果值 = 点数 × %d" % cd.key_value
			else:
				cd.description = "效果值固定"
			result.append(cd)
	return result
