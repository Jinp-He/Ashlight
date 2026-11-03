class_name CutsceneGroup
extends Node2D

## 一次演出触发的「自包含演出组」容器。
## 调用 play() 时内部创建独立的 CutsceneController + CutsceneInsert 子节点并同步播放；
## 控制器 finished 后整组 queue_free，旧组不残留、不干扰新组。
## 用于「每次点击/出牌生成独立演出组」方案：连续触发时多个组叠加、各自有完整插入帧，
## 根治多组共用全局唯一 CutsceneInsert 导致的「后触发的组打断/缺失插入帧」问题。
## 注意：本组仅承载一次演出，不复用；shake 作用于本组自身（偏移本组 position），各组合独立震动、互不串扰。

const CONTROLLER_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_controller.gd")
const INSERT_SCRIPT := preload("res://battle/scripts/cutscene/cutscene_insert.gd")

signal finished
## 缓动区（慢速对位区）进入/离开：转发自控制器，供调用方在「命中瞬间」同步触发效果。
signal slow_zone_entered
signal slow_zone_exited

var _controller: CutsceneController
var _insert: CutsceneInsert
## 整组销毁须等「控制器滑入完成」与「插入帧特效播完」两者都结束，
## 避免插入帧动画被较早结束的控制器一起 queue_free 截断（演出调快后尤为明显）。
var _ctrl_finished: bool = false
var _insert_finished: bool = false

func _on_slow_zone_entered() -> void:
	slow_zone_entered.emit()

func _on_slow_zone_exited() -> void:
	slow_zone_exited.emit()

## 并标记控制器已结束，参与整组销毁判定（不直接 free，须等插入帧也播完）。
func _on_ctrl_finished() -> void:
	finished.emit()
	_ctrl_finished = true
	_try_free()

## 插入帧特效播完回调：标记插入帧已结束，参与整组销毁判定。
func _on_insert_finished() -> void:
	_insert_finished = true
	_try_free()

## 仅当「控制器滑入完成」且「插入帧特效播完」两者都到位才销毁整组，
## 保证插入帧动画能完整播放（不再被较早结束的控制器一起 queue_free 截断）。
func _try_free() -> void:
	if _ctrl_finished and _insert_finished:
		queue_free()

## 启动一次独立演出（参数同 CutsceneController.play）。内部实例化独立控制器+插入帧。
## 滑入形变固定为「形变A·整体横向缩放」，由控制器按整组锚点离缓动区距离统一驱动（已移除形变B）。
func play(caster: Dictionary, target: Dictionary, direction: String, cost: int) -> void:
	_controller = CONTROLLER_SCRIPT.new() as CutsceneController
	if _controller == null:
		push_error("CutsceneGroup: 无法实例化 CutsceneController")
		queue_free()
		return
	_controller.z_index = 46
	add_child(_controller)

	_insert = INSERT_SCRIPT.new() as CutsceneInsert
	if _insert == null:
		push_error("CutsceneGroup: 无法实例化 CutsceneInsert")
		queue_free()
		return
	_insert.z_index = 45
	add_child(_insert)

	_controller.finished.connect(_on_ctrl_finished)
	_controller.slow_zone_entered.connect(_on_slow_zone_entered)
	_controller.slow_zone_exited.connect(_on_slow_zone_exited)
	_insert.finished.connect(_on_insert_finished)
	_controller.play(caster, target, direction, cost)
	_insert.play(direction)

## 多单位入口：直接转发 units 数组给控制器（插入帧仍与触发同步播放）。
## 用于预览场景随机多单位组合（我方1+敌方3 等）测试。
## 滑入形变固定为「形变A·整体横向缩放」，由控制器按整组锚点离缓动区距离统一驱动（已移除形变B）。
func play_units(units: Array[Dictionary], direction: String, cost: int) -> void:
	_controller = CONTROLLER_SCRIPT.new() as CutsceneController
	if _controller == null:
		push_error("CutsceneGroup: 无法实例化 CutsceneController")
		queue_free()
		return
	_controller.z_index = 46
	add_child(_controller)

	_insert = INSERT_SCRIPT.new() as CutsceneInsert
	if _insert == null:
		push_error("CutsceneGroup: 无法实例化 CutsceneInsert")
		queue_free()
		return
	_insert.z_index = 45
	add_child(_insert)

	_controller.finished.connect(_on_ctrl_finished)
	_controller.slow_zone_entered.connect(_on_slow_zone_entered)
	_controller.slow_zone_exited.connect(_on_slow_zone_exited)
	_insert.finished.connect(_on_insert_finished)
	_controller.play_units(units, direction, cost)
	_insert.play(direction)
