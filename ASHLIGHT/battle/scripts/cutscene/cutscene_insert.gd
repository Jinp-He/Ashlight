class_name CutsceneInsert
extends Node2D

## 战斗「插入演出」帧动画播放器。
## 选中目标打出卡牌后，由 HandManager 触发 play(direction) 全屏播放 14 帧序列；
## 帧来源：res://assets/battle/insert/insert_001.png ... insert_014.png（原生尺寸、全宽）。
## 美术铁规则：保持原尺寸，不缩放；敌方方向水平翻转（flip_h）。
## 设计：节点自身在 _ready 内建 Sprite2D 子节点，无需依赖 .tscn 预设子节点，便于独立实例化（F6 直跑不崩）。

signal finished

## 帧路径模板（%d 替换为 1..frame_count，零填充见 frame_dir 中 %03d）。
@export var frame_dir: String = "res://assets/battle/insert/insert_%03d.png"
## 帧总数（与 insert 文件夹内 png 数量一致）。
@export var frame_count: int = 14
## 每帧停留时长（秒）。frame_interval=0.1 → 每 0.1 秒一帧（10fps）；完整 14 帧 ≈ 1.4s（原 0.125 时 1.75s，调快更紧凑，替代抽帧解决拖沓）。
@export var frame_interval: float = 0.1
## 跳过的帧（1-based 文件名序号，0=不跳，默认）。填 13 = insert_013 = 14 帧序列的倒数第二帧可抽掉收尾；当前不抽，靠 frame_interval 调快解决拖沓。
@export var skip_frame: int = 0
## 敌方进攻时水平翻转（我方不翻转）。
@export var flip_for_enemy: bool = true

var _sprite: Sprite2D
var _frames: Array[Texture2D] = []
var _playing: bool = false
var _pending_direction: String = "player"

func _ready() -> void:
	add_to_group("cutscene_insert")   # 供 HandManager 经 group 取得本节点，避免硬编码路径
	_sprite = Sprite2D.new()
	_sprite.visible = false
	_sprite.centered = true
	add_child(_sprite)
	# 全屏宽帧：水平居中于设计宽 800；垂直基准 y=360（相对演出预览 effectY=400 上移 40px，略偏上非正中心 450）。
	_sprite.position = Vector2(800.0, 360.0)
	_preload_frames()

func _preload_frames() -> void:
	_frames.clear()
	for i in range(1, frame_count + 1):
		if i == skip_frame:
			continue
		var tex := load(frame_dir % i) as Texture2D
		if is_instance_valid(tex):
			_frames.append(tex)
	if _frames.is_empty():
		push_error("CutsceneInsert: 未加载到任何帧，请检查 frame_dir=%s（当前 res://assets/battle/insert/ 是否存在）" % frame_dir)

## 播放插入演出。direction ∈ {"player","enemy"}；敌方时水平翻转。
## 用 Tween.tween_method 驱动帧索引（线性），无 _process 轮询；播完 _stop → emit finished。
func play(direction: String = "player") -> void:
	if _frames.is_empty():
		finished.emit()
		return
	if _playing:
		return
	_playing = true
	SfxManager.play_insert()   # 演出插入帧开始即播音效，带随机 key 升降
	_pending_direction = direction
	_sprite.visible = true
	_sprite.flip_h = flip_for_enemy and direction == "enemy"
	# 连续播放：立即显示首帧，随后线性推进至末帧，无任何前导停顿/空白间隔。
	# 时长 = frame_interval × _frames.size()（skip_frame=0 时不抽帧，实播 = frame_count）；整段 tween 从 0 → size-1，每帧占满一整段、首尾等长。
	_set_frame(0)
	var total := frame_interval * float(_frames.size())
	var tw := create_tween()
	tw.tween_method(_set_frame, 0.0, float(_frames.size() - 1), total).set_trans(Tween.TRANS_LINEAR)
	tw.tween_callback(_stop)

## Tween 回调：将浮点帧索引四舍五入映射到纹理数组。
func _set_frame(f: float) -> void:
	var i := clampi(int(roundf(f)), 0, _frames.size() - 1)
	_sprite.texture = _frames[i]
	# 每帧素材左边贴屏幕左边（x=0）：centered 精灵中心 x = 纹理宽度/2。
	# 首帧 insert_001 为 226 宽小条 → 落在屏幕最左；其余 1600 宽全屏帧 → 左缘贴左、铺满屏宽。
	_sprite.position = Vector2(_sprite.texture.get_width() * 0.5, _sprite.position.y)

func _stop() -> void:
	_playing = false
	_sprite.visible = false
	_sprite.texture = null
	finished.emit()
