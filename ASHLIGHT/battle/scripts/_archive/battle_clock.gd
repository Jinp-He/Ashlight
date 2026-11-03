class_name BattleClock
extends RefCounted

## 纯规则状态：只有出牌/整备调用 tick。动画不推进时间。
var ticks: int = 0
var recoveries: Array[Dictionary] = []
var effects: Array[Dictionary] = []
var enemies: Dictionary = {}
var _serial: int = 0

func register_card(uid: String, cd: CardData, payload: Dictionary) -> void:
	_serial += 1
	if cd.cost_value > 0:
		recoveries.append({"key": "recovery_%d" % _serial, "uid": uid,
			"remaining": cd.cost_value, "cost": cd.cost_value, "color": 0})
	if cd.cost_color != 0:
		effects.append({"key": "effect_%d" % _serial, "uid": uid,
			"remaining": maxi(1, cd.wait_ticks), "color": cd.cost_color,
			"payload": payload})

func tick() -> Array[Dictionary]:
	ticks += 1
	for item in recoveries:
		item.remaining = maxi(0, item.remaining - 1)
	for uid in enemies:
		enemies[uid] = maxi(0, int(enemies[uid]) - 1)
	var pulses: Array[Dictionary] = []
	for item in effects.duplicate():
		item.remaining = maxi(0, item.remaining - 1)
		if item.color == 2:
			pulses.append({"kind": "effect", "uid": item.uid, "payload": item.payload})
			if item.remaining == 0:
				effects.erase(item)
	return pulses

func take_ready_allies() -> Array[Dictionary]:
	var ready: Array[Dictionary] = []
	for item in recoveries.duplicate():
		if item.remaining == 0:
			ready.append({"kind": "recovery", "uid": item.uid,
				"ep": ceili(float(item.cost) / 2.0)})
			recoveries.erase(item)
	for item in effects.duplicate():
		if item.color == 1 and item.remaining == 0:
			ready.append({"kind": "effect", "uid": item.uid, "payload": item.payload})
			effects.erase(item)
	return ready

func change_countdown(uid: String, delta: int) -> bool:
	var changed: bool = false
	if enemies.has(uid):
		var before: int = enemies[uid]
		enemies[uid] = maxi(0, before + delta)
		changed = before != int(enemies[uid])
	for collection in [recoveries, effects]:
		for item in collection:
			if item.uid == uid and item.remaining > 0:
				var before: int = item.remaining
				item.remaining = maxi(0, before + delta)
				changed = changed or before != int(item.remaining)
	# 加速可以提前结束引导时长，但不会凭空多触发一拍。
	effects = effects.filter(func(item: Dictionary) -> bool: return item.color != 2 or item.remaining > 0)
	return changed

func remove_unit(uid: String) -> void:
	enemies.erase(uid)
	recoveries = recoveries.filter(func(item: Dictionary) -> bool: return item.uid != uid)
	effects = effects.filter(func(item: Dictionary) -> bool: return item.uid != uid)

func remaining_for(uid: String) -> int:
	if enemies.has(uid):
		return enemies[uid]
	var remaining: int = 0
	for collection in [recoveries, effects]:
		for item in collection:
			if item.uid == uid and (remaining == 0 or item.remaining < remaining):
				remaining = item.remaining
	return remaining
