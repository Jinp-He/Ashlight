extends Node2D

## 演出编排器「随机演出」测试场景：点击按钮生成一次独立演出组(CutsceneGroup)，
## 内部含真实滑入演出 + 滑入形变(形变A·整体横向缩放，以整组锚点离缓动区距离统一驱动) +
## 持久 buff 提示(士气/出血) + 持久伤害数字，三者整合在一次滑入→滑出中呈现，便于一次目测效果。
## 伤害数字组 / buff 提示组 自滑入起持续存在、无动画（演出控制器内「滑入即带、随单位销毁」）。

const CUTSCENE_GROUP_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_group.gd")

@onready var _btn_a: Button = $BtnA
@onready var _btn_clear: Button = $BtnClear

## 可用单位池：{"id":String, "faction":String}
var _unit_pool: Array[Dictionary] = []

## 演示用 buff/debuff 名称与益害标记：仅含已实现的真实 buff/debuff（士气=增益 / 出血·中毒=减益）。
## 注：护盾(shield)不计入提示器（走独立护盾叠层），故池内不含「护盾」。
const DEMO_BUFF_NAMES: Array[String] = ["士气", "出血", "中毒", "赃物", "上毒"]
const DEMO_BUFF_TONES: Array[int] = [1, 2, 2, 2, 1]

func _ready() -> void:
	_build_unit_pool()
	_btn_a.pressed.connect(_on_random_a)
	_btn_clear.pressed.connect(_on_clear)

## 扫描 res://assets/units/{player,enemy}/{id}/ 收集所有可用单位 id。
func _build_unit_pool() -> void:
	_unit_pool.clear()
	for faction in ["player", "enemy"]:
		var dir := DirAccess.open("res://assets/units/%s" % faction)
		if dir == null:
			continue
		dir.list_dir_begin()
		var sub: String = dir.get_next()
		while sub != "":
			if dir.current_is_dir() and not sub.begins_with("."):
				_unit_pool.append({"id": sub, "faction": faction})
			sub = dir.get_next()
		dir.list_dir_end()

## 随机取一个单位的 id/faction 副本（faction_filter 非空时仅从同阵营取，避免改到池内引用）。
func _random_unit(faction_filter: String = "") -> Dictionary:
	if _unit_pool.is_empty():
		return {}
	var candidates: Array[Dictionary] = _unit_pool
	if faction_filter != "":
		candidates = []
		for u in _unit_pool:
			if u["faction"] == faction_filter:
				candidates.append(u)
	if candidates.is_empty():
		return {}
	var u: Dictionary = candidates[randi() % candidates.size()]
	return {"id": u["id"], "faction": u["faction"]}

## 随机出一种伤害数字 payload（覆盖全部 7 种类型），供随机演出单位的「伤害数字」字段。
## 字段见 FX/DamageNumber/README_DamageNumber.md §9（type/value/is_crit/source/absorbed_amount/blocked）。
func _random_payload() -> Dictionary:
	var types := ["normal", "crit", "tick", "absorb", "miss", "heal", "shield"]
	var t: String = types[randi() % types.size()]
	var p: Dictionary = {}
	match t:
		"normal": p = {"type": "normal", "value": -randi_range(50, 320)}
		"crit":   p = {"type": "crit", "value": -randi_range(200, 600), "is_crit": true}
		"tick":   p = {"type": "tick", "value": -randi_range(20, 80), "source": "灼烧"}
		"absorb": p = {"type": "absorb", "value": -randi_range(2, 20), "absorbed_amount": randi_range(5, 40)}
		"miss":   p = {"type": "miss", "value": 0}
		"heal":   p = {"type": "heal", "value": randi_range(40, 160)}
		"shield": p = {"type": "shield", "value": randi_range(30, 120)}
	return p

## 形变A·整体横向缩放（以整组锚点离缓动区距离统一驱动形变）：随机生成一次演出组。
func _on_random_a() -> void:
	_play_random()

## 播放一次随机多单位组合演出。
## 组合 = 我方 np 个 + 敌方 ne 个（各 0..3，至少 1 个），顺序随机交织；整体入场方向随机 player/enemy。
## 为每个单位随机挂上 真实 buff 提示(士气/出血) + 伤害数字 payload（dmg_payload）。
## 伤害数字/buff 提示在演出控制器内「滑入即带、持久无动画」，整段滑入→滑出持续可见。
func _play_random() -> void:
	var units: Array[Dictionary] = []
	var np: int = randi_range(0, 3)
	var ne: int = randi_range(0, 3)
	if np == 0 and ne == 0:
		np = 1   # 不允许空组合
	for i in np:
		var u := _random_unit("player")
		u["state"] = randi() % 5   # 0 Idle / 1 Attack / 2 Cast / 3 Hit / 4 Charge（缺态自动回退 Idle）
		units.append(u)
	for i in ne:
		var u := _random_unit("enemy")
		u["state"] = randi() % 5
		units.append(u)
	units.shuffle()   # 随机交织我方/敌方顺序，避免总我方在前
	for u in units:
		var k: int = randi() % DEMO_BUFF_NAMES.size()
		u["buff_name"] = DEMO_BUFF_NAMES[k]
		u["buff_tone"] = DEMO_BUFF_TONES[k]
		u["buff_icon"] = ""   # 留空 → 提示模块按 tone 兜底 buff001(中性)/buff002(有害)；正式战斗填真实图标名
		u["dmg_payload"] = _random_payload()
	var direction: String = "player" if randf() < 0.5 else "enemy"
	# 每次点击生成一个独立演出组（内含自己的 CutsceneController + CutsceneInsert），
	# 叠在旧组上方各自独立播放、播完自动 queue_free。
	var group := CUTSCENE_GROUP_SCRIPT.new() as CutsceneGroup
	add_child(group)
	group.play_units(units, direction, 3)

## 清除当前所有未播完的演出组（避免反复点击堆叠过多）。
func _on_clear() -> void:
	for g in get_children():
		if g is CutsceneGroup:
			g.queue_free()
