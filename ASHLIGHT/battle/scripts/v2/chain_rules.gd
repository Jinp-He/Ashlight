class_name ChainRules
extends RefCounted

## 纯规则状态：UNO 式接牌、Combo 倍数、牌堆顶、敌人倒计时与待命的反制。
## 只有玩家动作（出牌 / 摸牌 / 补牌）调用 tick。动画不推进时间。
const COLOR_NAMES: Array[String] = ["红", "蓝", "黄", "绿"]
const ANY_COLOR: int = -1
const MAX_COMBO: int = 4
const HAND_START: int = 5
const HAND_CAP: int = 10
const DRAW_TICKS: int = 1
const REFILL_TICKS: int = 1

## Combo ×N：下一张牌的效果触发 N 次。每接上一张 +1，断链回到 ×1。
var combo: int = 1
var ticks: int = 0
## 牌堆顶：{color, number, source("player"/"enemy"), uid, name}。空字典表示任意牌可接。
var top: Dictionary = {}
## 敌人 uid → 剩余倒计时。归零时当场出牌。
var enemies: Dictionary = {}
## 友方 uid → 技能剩余冷却（0 = 就绪）。
var skill_cooldowns: Dictionary = {}
## 待命的反制：{uid, color(ANY_COLOR=任意), value, name}。敌人的下一个动作到来时全部结算并消耗。
var counters: Array[Dictionary] = []

func can_play(color: int, base_number: int) -> bool:
	if top.is_empty():
		return true
	return color == int(top.color) or base_number == int(top.number)

func place_player_card(color: int, number: int, uid: String, card_name: String) -> void:
	top = {"color": color, "number": number, "source": "player", "uid": uid, "name": card_name}

func place_enemy_card(card: Dictionary, uid: String) -> void:
	top = {"color": card.color, "number": card.number, "source": "enemy", "uid": uid, "name": card.name}

func combo_up() -> void:
	combo = mini(combo + 1, MAX_COMBO)

func reset_combo() -> void:
	combo = 1

func tick() -> void:
	ticks += 1
	for uid in enemies:
		enemies[uid] = maxi(0, int(enemies[uid]) - 1)
	for uid in skill_cooldowns:
		skill_cooldowns[uid] = maxi(0, int(skill_cooldowns[uid]) - 1)

## 已归零的敌人（调用方按从左到右排序后依次出牌）。
func ready_enemies() -> Array[String]:
	var out: Array[String] = []
	for uid in enemies:
		if int(enemies[uid]) == 0:
			out.append(uid)
	return out

## 敌人的下一个动作到来：取出全部待命反制，返回其中能反制这张牌的。
## 只有攻击牌能被反制；不匹配的反制同样被消耗。
func take_counters(card: Dictionary) -> Array[Dictionary]:
	var hits: Array[Dictionary] = []
	for counter in counters:
		if card.stat == "damage" and (int(counter.color) == ANY_COLOR or int(counter.color) == int(card.color)):
			hits.append(counter)
	counters.clear()
	return hits

func remove_unit(uid: String) -> void:
	enemies.erase(uid)

static func color_name(color: int) -> String:
	if color == ANY_COLOR:
		return "任意"
	return COLOR_NAMES[clampi(color, 0, COLOR_NAMES.size() - 1)]
