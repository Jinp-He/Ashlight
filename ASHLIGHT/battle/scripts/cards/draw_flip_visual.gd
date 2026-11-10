extends Node2D
var _card: CardRoot
var _front: Sprite2D
var _back: Sprite2D

func setup(card: CardRoot) -> void:
	_card = card
	var viewport := SubViewport.new()
	viewport.size = Vector2i(400, 576)
	viewport.transparent_bg = true
	viewport.disable_3d = true
	viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(viewport)
	card.reparent(viewport)
	card.position = Vector2(200, 288)
	card.scale = Vector2.ONE
	card.rotation = 0
	card.visible = true
	_front = Sprite2D.new()
	_front.texture = viewport.get_texture()
	add_child(_front)
	# 正反面均投影400x576画布。卡背裁掉透明边距后，对齐正面底图可见轮廓。
	var back_viewport := SubViewport.new()
	back_viewport.size = viewport.size
	back_viewport.transparent_bg = true
	back_viewport.disable_3d = true
	back_viewport.render_target_update_mode = SubViewport.UPDATE_ALWAYS
	add_child(back_viewport)
	var back_art := TextureRect.new()
	var cropped := AtlasTexture.new()
	cropped.atlas = preload("res://assets/cards/card_back.png")
	var back_image := cropped.atlas.get_image()
	if back_image.is_compressed():
		back_image.decompress()
	cropped.region = Rect2(back_image.get_used_rect())
	back_art.texture = cropped
	back_art.expand_mode = TextureRect.EXPAND_IGNORE_SIZE
	back_art.position = Vector2(25, 14)
	back_art.size = Vector2(350, 555)
	back_viewport.add_child(back_art)
	_back = Sprite2D.new()
	_back.texture = back_viewport.get_texture()
	add_child(_back)
	for sprite in [_front, _back]:
		var material := ShaderMaterial.new()
		material.shader = preload("res://battle/scripts/cards/fake3d.gdshader")
		material.set_shader_parameter("fov", 70.0)
		material.set_shader_parameter("cull_back", true)
		sprite.material = material
	set_flip(180)

func set_flip(angle: float) -> void:
	_front.material.set_shader_parameter("y_rot", angle)
	_back.material.set_shader_parameter("y_rot", angle - 180)

func restore() -> void:
	if is_instance_valid(_card):
		_card.reparent(get_parent())
		_card.position = Vector2.ZERO
		_card.scale = Vector2.ONE
		_card.rotation = 0
		_card.visible = true
	queue_free()
func set_dissolve(progress: float) -> void:
	_front.material.set_shader_parameter("dissolve_progress", progress)
	_back.material.set_shader_parameter("dissolve_progress", progress)