class_name CutscenePreview
extends Node2D

## 演出预览独立场景：点击按钮播放一次 CutsceneController + CutsceneInsert。
## 美术来源随机（res://assets/units/...），用于调整编排器位置/速度等参数时快速目测。
## 不作为正式战斗逻辑；正式接入仍在 battle/Battle.tscn 的 HandManager 路径。

@onready var _button: Button = $PlayButton
@onready var _fx_button: Button = $FxButton

## 一次演出 = 一个独立 CutsceneGroup 实例（自带控制器+插入帧），连续点击可叠加、各自播完自毁。
const CUTSCENE_GROUP_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_group.gd")
## 孤立预览用：直接实例化粒子迸发 + 扩散圆环于屏幕中心，便于脱离整段演出单独调参。
const BURST_SCENE := preload("res://battle/scripts/cutscene/cutscene_burst.tscn")
const RING_SCENE := preload("res://battle/scripts/cutscene/cutscene_ring.tscn")

## 可用单位池：{"id":String, "faction":String}
var _unit_pool: Array[Dictionary] = []

## 演示用 buff/debuff 名称与益害标记（预览场景无真实 buff 数据，用它们驱动缓动区提示动画）。
## 注：护盾(shield)不计入 buff 提示器（走独立护盾叠层），故演示池不含「护盾」；仅含真实的 士气(增益)/出血·中毒(减益)。
const DEMO_BUFF_NAMES: Array[String] = ["士气", "出血", "中毒", "赃物", "上毒"]
const DEMO_BUFF_TONES: Array[int] = [1, 2, 2, 2, 1]

func _ready() -> void:
	_build_unit_pool()
	_button.pressed.connect(_on_play_pressed)
	_fx_button.pressed.connect(_on_fx_pressed)

## 孤立预览「粒子迸发 + 扩散圆环」：直接于屏幕中心实例化并播放，
## 不依赖整段滑入演出，方便单独调参（参数在 cutscene_burst.tscn / cutscene_ring.tscn 中调整）。
func _on_fx_pressed() -> void:
	var anchor := Vector2(800.0, 430.0)
	var b := BURST_SCENE.instantiate() as Node2D
	if b != null:
		add_child(b)
		b.position = anchor
		if b.has_method("play"):
			b.play()
	var r := RING_SCENE.instantiate() as Node2D
	if r != null:
		add_child(r)
		r.position = anchor
		if r.has_method("play"):
			r.play()

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

## 点击「播放演出」：播放一次随机多单位组合（滑入形变=形变A·整体横向缩放，以整组锚点离缓动区距离统一驱动）。
func _on_play_pressed() -> void:
	_play()

## 播放一次随机多单位组合演出。
## 组合 = 我方 np 个 + 敌方 ne 个（各 0..3，至少 1 个），顺序随机交织；整体入场方向随机 player/enemy。
## 例如 np=1,ne=3 → 「我方1敌方3」；np=3,ne=2 → 「我方3敌方2」。覆盖用户要看的混合比例。
func _play() -> void:
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
	# 演示缓动区效果：为每个单位随机挂上 buff/debuff 提示 + 伤害数字字段。
	# 正式战斗中这些字段由调用方按真实结算数据填入（缺省则不触发该类效果）。
	for u in units:
		var k: int = randi() % DEMO_BUFF_NAMES.size()
		u["buff_name"] = DEMO_BUFF_NAMES[k]
		u["buff_tone"] = DEMO_BUFF_TONES[k]
		u["buff_icon"] = ""   # 留空 → 提示模块按 tone 兜底 buff001(中性)/buff002(有害)；正式战斗填真实图标名
		u["damage"] = randi_range(3, 20) * 10
	var direction: String = "player" if randf() < 0.5 else "enemy"
	# 每次点击生成一个独立演出组（内含自己的 CutsceneController + CutsceneInsert），
	# 叠在旧组上方各自独立播放、播完自动 queue_free —— 重叠时每个组都有完整插入帧
	# （根治共用全局唯一插入帧导致的新组无特效问题）。
	var group := CUTSCENE_GROUP_SCRIPT.new() as CutsceneGroup
	add_child(group)
	group.play_units(units, direction, 3)
