## 全局音效服务（Autoload，服务定位器）。
## 仅负责音效播放，不持有任何玩法逻辑：抽牌随机音 + 鼠标点击音。
## 注册于 project.godot [autoload] 段，名称 SfxManager。
extends Node

## 4 种抽牌音（zapsplat 发牌声），随机取一播放。
const DRAW_STREAMS: Array[AudioStream] = [
	preload("res://assets/audio/sfx/draw_001.mp3"),
	preload("res://assets/audio/sfx/draw_002.mp3"),
	preload("res://assets/audio/sfx/draw_003.mp3"),
	preload("res://assets/audio/sfx/draw_004.mp3"),
]
## 鼠标点击音。
const CLICK_STREAM: AudioStream = preload("res://assets/audio/sfx/ui_click.mp3")
## 连锁音效（触发连锁「加链环」瞬间播放）：单文件 + 高连锁升 key。
## 音源为用户提供的 djartmusic-magical-sparkle-whoosh，字节复制为 chain_sfx.mp3（英文命名约定）。
const CHAIN_STREAM: AudioStream = preload("res://assets/audio/sfx/chain_sfx.mp3")
## 演出「插入帧」音效：用户提供的「演出插入.wav」，字节复制为 insert_whoosh.wav（英文命名约定）。
## 每次插入演出（卡牌打出 / 敌人攻击的插帧动画）开始时播放，带随机 key 升降变化避免每次完全一样。
const INSERT_STREAM: AudioStream = preload("res://assets/audio/sfx/insert_whoosh.wav")
## 连锁三档音量（线性，相对原始 100）：1连锁=50 / 2连锁=80 / 3连锁=100。
const CHAIN_VOL: Array[float] = [0.0, 0.50, 0.80, 1.00]
## 连锁音高步进：每多一连锁升几个半音（原设计 2 半音 ≈ +2 key）。
## 改为公式化——音高随连锁次数无限递升（pitch = 2^(半音数/12)），不再有"3连锁封顶"的档位上限。
@export var chain_semitone_step: float = 2.0
## 点击音音量(dB)：默认 -16，比抽牌/演出音轻，存在感不抢戏。
## 0 = 原始音量；负值越小越轻。若仍嫌吵可调到 -20 左右。
@export var click_volume_db: float = -16.0
## 连锁音效随机变调幅度（pitch_scale ± 该值）：叠极小抖动避免每次完全一样、听感不死板。设 0 即完全还原设计音。
@export var chain_pitch_jitter: float = 0.02
## 演出插入音效随机变调幅度（pitch_scale ± 该值）：每次插帧播放时随机升/降一点 key，听感不死板。0 = 固定原版音高。
@export var insert_pitch_jitter: float = 0.06

var _click_player: AudioStreamPlayer

func _ready() -> void:
	_click_player = AudioStreamPlayer.new()
	_click_player.volume_db = click_volume_db
	add_child(_click_player)


## 抽牌音：每次调用独立生成一个 AudioStreamPlayer 播放随机一种，播完自动回收。
## 因此可多重叠加——同一时刻多张抽牌音同时响，不会被前一张截断（修正之前 4 池轮询互切的问题）。
func play_random_draw() -> void:
	if DRAW_STREAMS.is_empty():
		return
	var player := AudioStreamPlayer.new()
	add_child(player)
	player.stream = DRAW_STREAMS[randi() % DRAW_STREAMS.size()]
	player.finished.connect(player.queue_free)
	player.play()


## 鼠标点击音。
func play_click() -> void:
	if is_instance_valid(_click_player):
		_click_player.stream = CLICK_STREAM
		_click_player.play()


## 播放连锁音效。ring_count = 链环数(锚点不计)；<1 视为尚未形成连锁，不播放。
## 单文件 chain_sfx.mp3 高连锁通过 pitch_scale 升 key + 音量递增（CHAIN_VOL）体现"越来越强"；
## 音高现已公式化：pitch = 2^(半音数/12)，半音数 = chain_semitone_step × (ring_count-1)，随连锁次数无限递升、不再有档位上限。
## 叠加 ±chain_pitch_jitter 极小随机变调避免每次完全一样、听感不死板。
## 每次调用独立生成 AudioStreamPlayer，自然播完即回收。
func play_chain(ring_count: int) -> void:
	if ring_count < 1:
		return
	# 音高：随连锁次数无限递升——每多一链环升 chain_semitone_step 个半音，pitch = 2^(半音数/12)。
	var semis: float = chain_semitone_step * float(ring_count - 1)
	var pitch: float = pow(2.0, semis / 12.0) + randf_range(-chain_pitch_jitter, chain_pitch_jitter)
	# 音量：沿用原三档手感（1连锁=0.5 / 2连锁=0.8 / 3连锁=1.0），更高连锁维持满音量(1.0=0dB)防爆音。
	# 注意：这是音量自然封顶，并非连锁档位上限——升 key 已无上限。
	var lvl: int = clampi(ring_count, 1, CHAIN_VOL.size() - 1)
	var player := AudioStreamPlayer.new()
	add_child(player)
	player.stream = CHAIN_STREAM
	player.volume_db = linear_to_db(CHAIN_VOL[lvl])   # 线性(0~1) → dB；1.0=0dB，0.8≈-1.9dB，0.5≈-6.0dB
	player.pitch_scale = pitch
	player.finished.connect(player.queue_free)   # 自然播完回收
	player.play()


## 播放演出「插入帧」音效（卡牌打出 / 敌人攻击的插帧动画开始时，见 CutsceneInsert.play）。
## 带随机 key 升降：pitch_scale = 1.0 ± insert_pitch_jitter（正负随机，每次插帧升或降一点，听感不死板）。
## 每次调用独立生成 AudioStreamPlayer，自然播完即回收，可多组演出音叠加互不截断。
func play_insert() -> void:
	var pitch: float = 1.0 + randf_range(-insert_pitch_jitter, insert_pitch_jitter)
	var player := AudioStreamPlayer.new()
	add_child(player)
	player.stream = INSERT_STREAM
	player.pitch_scale = pitch
	player.finished.connect(player.queue_free)   # 自然播完回收
	player.play()


## 全局左键按下即播点击音——覆盖按钮、卡牌选靶、单位点选等一切鼠标点击动作。
func _input(event: InputEvent) -> void:
	if event is InputEventMouseButton:
		var mb := event as InputEventMouseButton
		if mb.pressed and mb.button_index == MOUSE_BUTTON_LEFT:
			play_click()
