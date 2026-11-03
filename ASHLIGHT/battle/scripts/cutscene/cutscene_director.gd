class_name CutsceneDirector
extends Node

## 演出组串行调度器（方案甲）：同一时刻仅一个 CutsceneGroup 在播，其余入 FIFO 队；
## 当前组 finished → 执行其自带结算回调 on_finished → 自动播放队首下一个。
## 杜绝「同帧 spawn 多个演出组叠放」的观感 bug。
##
## 关键语义：本调度器只管「演出组播放不重叠」，不干涉各生成点的结算/推进逻辑——
## 每个生成点把「如何构建 group」(build_callable) 与「播完后做什么」(on_finished) 一并交来，
## 调度器保证它们按入队顺序一场接一场执行，on_finished 也严格在该组真正播完后才回调。

var _active: CutsceneGroup = null
var _active_cb: Callable = Callable()
## 队列项：{"build": Callable -> CutsceneGroup, "on_finished": Callable}
var _queue: Array = []

func _ready() -> void:
	process_mode = Node.PROCESS_MODE_ALWAYS   # 即便父节点暂停也照常推进，避免卡死

## 入队一个演出组请求。
## build_callable：无参 Callable，内部自行为 group 调用 new()/add_child()/play_units()，返回 group。
##   注意：build 内【不要】自行 connect group.finished（交由本调度器统一接管）。
func enqueue(build_callable: Callable, on_finished_callable: Callable = Callable(), owner_uid: String = "") -> void:
	_queue.append({"build": build_callable, "on_finished": on_finished_callable, "owner_uid": owner_uid})
	_pump()

func _pump() -> void:
	if _active != null:
		return
	if _queue.is_empty():
		return
	var spec: Dictionary = _queue.pop_front()
	var group: CutsceneGroup = spec["build"].call() as CutsceneGroup
	if not is_instance_valid(group):
		_spec_done(null, spec["on_finished"])
		return
	group.set_meta("owner_uid", spec.get("owner_uid", ""))
	_active = group
	_active_cb = _on_group_finished.bind(group, spec["on_finished"])
	group.finished.connect(_active_cb)

## 丢弃属于 owner_uid 的全部演出组（含正在播放的 _active 与排队中的），且【不触发】其 on_finished。
##   重框下一位，绝不能让该单位的演出结算回调(execute_end) 再去误动当前已框定的下一位（否则队列错位 / 卡死）。
## 仅精准命中 owner_uid，不影响其它单位的演出组。
func cancel_for_uid(uid: String) -> void:
	for i in range(_queue.size() - 1, -1, -1):
		if _queue[i].get("owner_uid", "") == uid:
			_queue.remove_at(i)
	if _active != null and _active.get_meta("owner_uid", "") == uid:
		var g := _active
		if is_instance_valid(g) and g.finished.is_connected(_active_cb):
			g.finished.disconnect(_active_cb)
		_active = null
		_active_cb = Callable()
		if is_instance_valid(g):
			g.queue_free()
		_pump()

func _on_group_finished(group: CutsceneGroup, on_finished: Callable) -> void:
	if _active == group:
		if is_instance_valid(group) and group.finished.is_connected(_active_cb):
			group.finished.disconnect(_active_cb)
		_active = null
		_active_cb = Callable()
	if on_finished.is_valid():
		on_finished.call()
	_pump()

func _spec_done(_g: CutsceneGroup, on_finished: Callable) -> void:
	if on_finished.is_valid():
		on_finished.call()
	_pump()
