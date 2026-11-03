extends Node2D
class_name BeamBurst

## 白色光束扩散特效（像素风）。
## 移植自网页预览《光束扩散特效_预览.html》，参数与数学一一对应：
##   扩散曲线  expand = 1 - (1-p)^burst          （极快首爆 → 缓速尾段）
##   主透明度  a      = (1 - p/0.95)^1.3 * alpha   （扩散同时整体淡出直到消失）
##   光束      = 轴向渐变(中心亮→两端硬边收口) × 垂直遮罩(中间厚→上下收口)
##   热核      = 更窄更高的中心亮线（权重 = core）
##   粒子      = 开场一次性迸发（最多），之后阻尼外飞、尺寸(1-p*0.92)缩小、透明淡出
##   像素化    = 所有坐标按 pixel_size 量化到网格（光束纹理分块、粒子方块 snap）
##
## 用法（程序员集成）：
##   var b := preload("res://FX/BeamBurst/BeamBurst.gd").new()
##   add_child(b)
##   b.position = Vector2(640, 360)   # 特效中心点
##   b.angle_deg = 30.0               # 可选：旋转
##   b.play()
## 也可直接把 BeamBurst.tscn（本脚本挂在一个 Node2D 上）拖进场景，勾 autoplay。

# ===================== 可调参数（对应网页 12 项） =====================
@export_group("Beam 光束")
@export var pixel_size : int = 5            # 像素块大小（量化网格，越大越像素风）
@export var angle_deg : float = 0.0         # 旋转角度（度）
@export var length : float = 100.0          # 最大长度（全长，px）
@export var thickness : float = 25.0        # 垂直厚度（全厚，px）
@export var burst : float = 10.0            # 扩散缓动：越大首爆越快
@export var duration_ms : float = 800.0     # 持续时间（ms）
@export var alpha : float = 1.0             # 整体亮度
@export var core : float = 0.29             # 核心加亮（0~1）

@export_group("Particles 粒子")
@export var particle_count : int = 10       # 粒子数量（开场一次性迸发）
@export var particle_speed : float = 0.60   # 粒子速度倍率
@export var particle_bias : float = 0.0     # 0=全向点缀，1=强轴向(向两边)扩散

@export_group("Playback 播放")
@export var loop : bool = true              # 循环播放
@export var autoplay : bool = false         # 进场景自动播放

# 纹理缓冲上限（按网页可调范围取余量：长度≤100、厚度≤60 → 缓冲 240×140）
const MAX_LEN : float = 120.0
const MAX_THICK : float = 70.0

# 内部粒子数据
class Particle:
	var x: float = 0.0
	var y: float = 0.0
	var vx: float = 0.0
	var vy: float = 0.0
	var size: float = 0.0
	var a0: float = 0.0

var _beam_sprite : Sprite2D
var _beam_img : Image
var _beam_tex : ImageTexture
var _t : float = 0.0            # 已过时间（秒）
var _playing : bool = false
var _particles : Array[Particle] = []


func _ready() -> void:
	var iw := int(MAX_LEN * 2.0)
	var ih := int(MAX_THICK * 2.0)
	_beam_img = Image.create(iw, ih, false, Image.FORMAT_RGBA8)
	_beam_tex = ImageTexture.create_from_image(_beam_img)
	_beam_sprite = Sprite2D.new()
	_beam_sprite.texture = _beam_tex
	_beam_sprite.centered = true
	_beam_sprite.visible = false
	var mat := CanvasItemMaterial.new()
	mat.blend_mode = CanvasItemMaterial.BLEND_MODE_ADD   # 发光叠力
	_beam_sprite.material = mat
	add_child(_beam_sprite)
	# 自身 _draw（粒子）也走加法混合：给本节点挂同样 ADD 材质的 CanvasItemMaterial
	material = mat
	if autoplay:
		play()


## 开始（或重播）特效
func play() -> void:
	_t = 0.0
	_playing = true
	_beam_sprite.visible = true
	_spawn_particles()


## 停止并清空显示
func stop() -> void:
	_playing = false
	_t = 0.0
	if _beam_img != null:
		_beam_img.fill(Color(0, 0, 0, 0))
		if _beam_tex != null:
			_beam_tex.update(_beam_img)
	_beam_sprite.visible = false
	queue_redraw()


func _spawn_particles() -> void:
	_particles.clear()
	var axis := deg_to_rad(angle_deg)
	for i in particle_count:
		var dir : float
		if randf() < particle_bias:
			var side := -1.0 if randf() < 0.5 else 1.0
			dir = axis + side * randf() * 0.6      # 沿光束轴向向两边（带少量散布）
		else:
			dir = randf() * TAU                      # 少量全向点缀
		var sp := (40.0 + randf() * 150.0) * particle_speed
		var r0 := randf() * 10.0
		var pt := Particle.new()
		pt.x = cos(dir) * r0
		pt.y = sin(dir) * r0
		pt.vx = cos(dir) * sp
		pt.vy = sin(dir) * sp
		pt.size = 3.0 + randf() * 7.0
		pt.a0 = 0.5 + randf() * 0.5
		_particles.append(pt)


func _process(delta: float) -> void:
	if not _playing:
		return
	_t += delta
	var dur := maxf(duration_ms / 1000.0, 0.001)
	var p := _t / dur
	if p >= 1.0:
		if loop:
			_t = 0.0
			p = 0.0
			_spawn_particles()
		else:
			p = 1.0
			_playing = false
	_update_beam(p)
	_update_particles(p, delta)
	queue_redraw()


func _update_beam(p: float) -> void:
	var expand := 1.0 - pow(1.0 - p, burst)
	var hx := float(length) * expand
	var hy := float(thickness)
	var a := pow(maxf(0.0, 1.0 - p / 0.95), 1.3) * alpha
	var w := _beam_img.get_width()
	var h := _beam_img.get_height()
	_beam_img.fill(Color(0, 0, 0, 0))
	_beam_sprite.rotation_degrees = angle_deg
	if hx < 0.5:
		_beam_tex.update(_beam_img)
		return

	# 像素块大小 = 量化网格步长
	var px := maxi(1, int(pixel_size))
	var ch := hy * 0.5   # 热核半厚（网页 gy2 的 ch）
	for yy in range(0, h, px):
		var bh := mini(px, h - yy)
		var cy := float(yy) - h * 0.5 + px * 0.5
		# 垂直遮罩（网页 gy，线性渐变量程 [-hy,+hy]）：
		#   |y| ≤ 0.16·hy → 实心；0.16·hy ~ 0.40·hy 线性收口到 0；再外为 0
		var vy := absf(cy) / maxf(hy, 0.001)
		var vy_mask := clampf((0.40 - vy) / 0.24, 0.0, 1.0)
		# 热核垂直遮罩（网页 gy2，量程 [-ch,+ch]）：|y| ≤ 0.30·ch 实心 → 1.0·ch 收口
		var vy_core := 0.0
		if core > 0.0:
			var wv := absf(cy) / maxf(ch, 0.001)
			vy_core = clampf((1.0 - wv) / 0.70, 0.0, 1.0)
		if vy_mask <= 0.0 and vy_core <= 0.0:
			continue
		for xx in range(0, w, px):
			var bw := mini(px, w - xx)
			var cx := float(xx) - w * 0.5 + px * 0.5
			# 轴向遮罩（网页 gx，线性渐变量程 [-hx,+hx]）：
			#   |x| ≤ 0.48·hx → 实心；0.48·hx ~ 0.68·hx 线性收口到 0；再外为 0
			var ux := absf(cx) / maxf(hx, 0.001)
			var ax_mask := clampf((0.68 - ux) / 0.20, 0.0, 1.0) * a
			# 热核轴向遮罩（网页 gx2）：|x| ≤ 0.40·hx 实心 → 0.60·hx 收口
			var ax_core := 0.0
			if core > 0.0:
				ax_core = clampf((0.60 - ux) / 0.20, 0.0, 1.0) * a * core
			var fa := clampf(ax_mask * vy_mask + ax_core * vy_core, 0.0, 1.0)
			if fa > 0.003:
				_beam_img.fill_rect(Rect2i(xx, yy, bw, bh), Color(1, 1, 1, fa))
	_beam_tex.update(_beam_img)


func _update_particles(p: float, delta: float) -> void:
	var damp := pow(0.985, delta * 60.0)   # 帧率无关阻尼（对应网页每帧 *0.985）
	for pt in _particles:
		pt.x += pt.vx * delta
		pt.y += pt.vy * delta
		pt.vx *= damp
		pt.vy *= damp


func _draw() -> void:
	if not _playing:
		return
	var prog := clampf(_t / maxf(duration_ms / 1000.0, 0.001), 0.0, 1.0)
	# 注意：本节点 material 已设为 ADD，故以下 _draw 内容天然走加法混合
	var px := maxf(1.0, float(pixel_size))
	for pt in _particles:
		var s := pt.size * (1.0 - prog * 0.92)     # 持续缩小至消失
		if s <= 0.3:
			continue
		var al := (1.0 - prog) * pt.a0
		if al <= 0.0:
			continue
		# 像素化：位置与尺寸都对齐到网格 → 方块（1..N 个像素块），与网页低分辨率缓冲一致
		var blocks := maxi(1, int(round(s / px)))
		var ds := float(blocks) * px
		var sx := floorf(pt.x / px) * px
		var sy := floorf(pt.y / px) * px
		var half := ds * 0.5
		draw_rect(Rect2(sx - half, sy - half, ds, ds), Color(1, 1, 1, al))
