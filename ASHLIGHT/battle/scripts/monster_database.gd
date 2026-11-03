class_name MonsterDatabase
extends RefCounted

## 原型怪物数据。countdown 为每次出牌后的倒计时；cards 按顺序循环打出。
## 卡牌：color(0红 1蓝 2黄 3绿) / number / stat(damage|shield) / value / delay(本次之后倒计时额外 +N)。
const MONSTERS: Dictionary = {
	"Remy": {"max_hp": 30.0, "countdown": 3, "cards": [
		{"name": "斩击", "color": 0, "number": 3, "stat": "damage", "value": 5},
		{"name": "重斩", "color": 0, "number": 5, "stat": "damage", "value": 8},
	]},
	"Wangg": {"max_hp": 30.0, "countdown": 4, "cards": [
		{"name": "冰锥", "color": 1, "number": 4, "stat": "damage", "value": 6},
		{"name": "雷击", "color": 2, "number": 6, "stat": "damage", "value": 9},
		{"name": "护体", "color": 3, "number": 2, "stat": "shield", "value": 5},
	]},
	"Grey": {"max_hp": 40.0, "countdown": 5, "cards": [
		{"name": "重锤", "color": 0, "number": 4, "stat": "damage", "value": 8},
		{"name": "粉碎", "color": 0, "number": 7, "stat": "damage", "value": 16, "delay": 1},
		{"name": "装甲", "color": 1, "number": 3, "stat": "shield", "value": 10},
	]},
	"Mafe": {"max_hp": 99.0, "countdown": 3, "cards": [
		{"name": "重锤", "color": 0, "number": 4, "stat": "damage", "value": 8},
		{"name": "粉碎", "color": 0, "number": 7, "stat": "damage", "value": 16, "delay": 1},
		{"name": "装甲", "color": 1, "number": 3, "stat": "shield", "value": 10},
	]},
}

static func get_data(id: String) -> Dictionary:
	return MONSTERS.get(id, {})
