class_name PileFlame
extends Control
## 牌堆顶美术模板：同一套透明火焰帧读取卡牌色表，双层同步播放。
const DISSOLVE := preload("res://battle/scripts/ui/pile_flame_dissolve.gdshader")
@export var frame_interval: float = 0.1
@export var dissolve_duration: float = 0.3
var back: Sprite2D
var front: Sprite2D
var _back_material: ShaderMaterial
var _front_material: ShaderMaterial
var _frames: Array[Texture2D] = []
var _frame_time: float = 0.0
var _frame: int = 0
var _transition_time: float = 0.0
var _transition_active: bool = false
var _initialized: bool = false
var current_tint := Color.BLACK
var target_tint := Color.BLACK
var _pending_tint := Color.BLACK

func _ready() -> void:
	mouse_filter = Control.MOUSE_FILTER_IGNORE
	for i in 6:
		_frames.append(load("res://assets/ui/pile_flame/flame_%02d.png" % i) as Texture2D)
	back = _make_layer("NewColor")
	front = _make_layer("OldColor")
	_back_material = back.material as ShaderMaterial
	_front_material = front.material as ShaderMaterial
	back.visible = false
	_apply_frame()
	_apply_tint(current_tint)

func _make_layer(layer_name: String) -> Sprite2D:
	var sprite := Sprite2D.new()
	sprite.name = layer_name
	sprite.centered = false
	var shader_material := ShaderMaterial.new()
	shader_material.shader = DISSOLVE
	sprite.material = shader_material
	add_child(sprite)
	return sprite

func set_color_index(index: int) -> void:
	# 不维护第二份颜色表；卡牌修改/增加颜色后随该表自动更新。
	if index < 0 or CardRoot.COST_COLORS.is_empty():
		set_tint(Color.BLACK)
	else:
		set_tint(CardRoot.COST_COLORS[clampi(index, 0, CardRoot.COST_COLORS.size() - 1)])

func set_tint(tint: Color) -> void:
	_pending_tint = tint
	if not _initialized:
		_initialized = true
		_apply_tint(tint)
		return
	if _transition_active or current_tint.is_equal_approx(tint):
		return
	target_tint = tint
	_transition_time = 0.0
	_transition_active = true
	_back_material.set_shader_parameter("tint", tint)
	_front_material.set_shader_parameter("progress", 0.0)
	back.visible = true

func _apply_tint(tint: Color) -> void:
	current_tint = tint
	target_tint = tint
	_front_material.set_shader_parameter("tint", tint)
	_front_material.set_shader_parameter("progress", 0.0)
	_back_material.set_shader_parameter("tint", tint)
	back.visible = false

func _apply_frame() -> void:
	front.texture = _frames[_frame]
	back.texture = _frames[_frame]

func _process(delta: float) -> void:
	_frame_time += delta
	while _frame_time >= maxf(frame_interval, 0.001):
		_frame_time -= maxf(frame_interval, 0.001)
		_frame = (_frame + 1) % _frames.size()
		_apply_frame()
	if not _transition_active:
		return
	_transition_time += delta
	var progress: float = clampf(_transition_time / maxf(dissolve_duration, 0.001), 0.0, 1.0)
	_front_material.set_shader_parameter("progress", progress)
	if progress >= 1.0:
		_transition_active = false
		_apply_tint(target_tint)
		if not _pending_tint.is_equal_approx(current_tint):
			set_tint(_pending_tint)
