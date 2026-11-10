class_name RewardCandidate
extends Node2D
var card_data: CardData
var aspect_id: String = ""
var config: RewardConfig
var base_position: Vector2
var chosen := false
var hovered := false
var ready_input := false
var _visual: Node2D
var _card: CardRoot
var _flip: Node2D
var _ghost: Sprite2D
var _outline: Line2D
var _target_pos := Vector2.ZERO
var _target_scale := 1.0
var _clock := 0.0
var _phase := randf()*TAU
var _white_material: ShaderMaterial
var _hover_outline: ShaderMaterial
var _aspect_flag: Sprite2D
func _ready() -> void:
	_visual = Node2D.new(); add_child(_visual)
	_hover_outline=ShaderMaterial.new();_hover_outline.shader=preload("res://battle/scripts/units/outline.gdshader");_hover_outline.set_shader_parameter("outline_color",Color.WHITE);_hover_outline.set_shader_parameter("outline_thickness",config.choice_outline_pixels)
	if card_data != null:
		_card = CardRoot.new(); _card.apply_data(card_data,card_data.base_number)
		_visual.add_child(_card)
		_flip = preload("res://battle/scripts/cards/draw_flip_visual.gd").new()
		_visual.add_child(_flip); _flip.setup(_card); _flip.set_flip(0)
		for side in [_flip._front,_flip._back]:
			side.material.set_shader_parameter("x_rot",0.0)
			side.material.set_shader_parameter("rect_size",Vector2(400,576))
		_ghost = Sprite2D.new(); _ghost.texture = _flip._front.texture
		var mat := ShaderMaterial.new(); mat.shader=preload("res://battle/scripts/cutscene2/silhouette.gdshader")
		mat.set_shader_parameter("silhouette_color",Color.BLACK)
		_ghost.material=mat; _ghost.modulate=Color(0,0,0,0.3); _ghost.visible=false; _ghost.z_index=-1; add_child(_ghost)
	else:
		_build_aspect()
	_outline=Line2D.new(); _outline.width=3; _outline.default_color=Color.WHITE
	var size := Vector2(400,576) if card_data!=null else Vector2(238,341)
	_outline.points=PackedVector2Array([-size/2,Vector2(size.x/2,-size.y/2),size/2,Vector2(-size.x/2,size.y/2),-size/2])
	_outline.visible=false; _visual.add_child(_outline)
func _build_aspect() -> void:
	var data: Dictionary = AspectCatalog.ITEMS[aspect_id]
	for file in ["aspect_base.png", "flag_%02d.png" % randi_range(1,3)]:
		var art := Sprite2D.new(); art.texture=load("res://assets/rewards/"+file); art.centered=false; art.position=-Vector2(238,341)/2; _visual.add_child(art)
		if file.begins_with("flag_"):_aspect_flag=art
	var icon:=TextureRect.new(); icon.texture=load("res://assets/rewards/aspect_%03d.png" % data.icon)
	icon.expand_mode=TextureRect.EXPAND_IGNORE_SIZE; icon.stretch_mode=TextureRect.STRETCH_KEEP_ASPECT_CENTERED
	icon.position=Vector2(100,0)-Vector2(238,341)/2; icon.size=Vector2(38,38); icon.mouse_filter=Control.MOUSE_FILTER_IGNORE; _visual.add_child(icon)
	var heading:=Label.new();heading.text=str(data.name);heading.position=Vector2(20,81)-Vector2(238,341)/2;heading.size=Vector2(198,42)
	heading.add_theme_font_override("font",preload("res://assets/fonts/Biantao.ttf"));heading.add_theme_font_size_override("font_size",27);heading.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER;heading.mouse_filter=Control.MOUSE_FILTER_IGNORE;_visual.add_child(heading)
	var text:=RichTextLabel.new();text.bbcode_enabled=true;text.scroll_active=false;text.fit_content=false
	text.autowrap_mode=TextServer.AUTOWRAP_WORD_SMART;text.position=Vector2(40,155)-Vector2(238,341)/2;text.size=Vector2(158,98)
	text.add_theme_font_override("normal_font",preload("res://assets/fonts/DouyinSansBold.otf"));text.add_theme_font_size_override("normal_font_size",14)
	text.text="[center]"+str(data.description)+"[/center]";text.mouse_filter=Control.MOUSE_FILTER_IGNORE;_visual.add_child(text)

func set_hover(value: bool) -> void:
	if chosen: value=false
	hovered=value; _outline.visible=false
	if is_instance_valid(_flip):_flip._front.material.next_pass=_hover_outline if chosen or hovered else null
	elif is_instance_valid(_aspect_flag):_aspect_flag.material=_hover_outline if chosen else null
	_target_pos=Vector2.UP*config.hover_lift if value and card_data!=null else Vector2.ZERO
	_target_scale=config.hover_scale if value and card_data!=null else 1.0
	if is_instance_valid(_ghost): _ghost.visible=value
func select(value: bool) -> void:
	chosen=value; set_hover(false); _outline.visible=false
	_visual.position=Vector2.ZERO;_visual.scale=Vector2.ONE
	if is_instance_valid(_flip):
		_flip._front.material.set_shader_parameter("x_rot",0.0);_flip._front.material.set_shader_parameter("y_rot",0.0)
	if value and is_instance_valid(_card): _card.play_card_flash(0,0.4)
func _process(delta: float) -> void:
	_clock+=delta
	if not is_instance_valid(_visual): return
	_visual.position=_visual.position.lerp(_target_pos,1-exp(-14*delta))
	_visual.scale=_visual.scale.lerp(Vector2.ONE*_target_scale,1-exp(-14*delta))
	if card_data==null and ready_input and not chosen:
		_visual.position=Vector2(sin(_clock*0.8+_phase)*config.float_amplitude.x, sin(_clock*0.65+_phase*1.7)*config.float_amplitude.y)
	if card_data!=null and is_instance_valid(_flip) and not chosen:
		var factor:=1.0 if hovered else 0.0
		var mat: ShaderMaterial=_flip._front.material
		for axis in ["x_rot","y_rot"]:
			var target:=sin(_clock*(0.8 if axis=="x_rot" else 0.65)+_phase)*config.perspective_degrees*factor
			mat.set_shader_parameter(axis,lerpf(float(mat.get_shader_parameter(axis)),target,1-exp(-8*delta)))
func contains(point: Vector2) -> bool:
	var size:=Vector2(400,576) if card_data!=null else Vector2(238,341)
	return Rect2(-size/2,size).has_point(to_local(point))
func depart(selected: bool, destination: Vector2) -> void:
	ready_input=false;set_hover(false)
	# Tween 绑定候选节点，退出场景时随节点释放，不保留等待 finished 的协程。
	var tween:=create_tween()
	if card_data!=null:
		if selected:
			tween.tween_method(_flip.set_flip,0.0,180.0,0.3).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
			tween.tween_property(self,"position",destination,0.45).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_IN_OUT)
			tween.parallel().tween_property(self,"scale",Vector2.ONE*0.08,0.45)
		else:
			tween.tween_method(_flip.set_dissolve,0.0,1.0,0.8)
	else:
		var shader:=Shader.new()
		shader.code="shader_type canvas_item; uniform float p=0.0; uniform bool white=false; void fragment(){vec4 t=texture(TEXTURE,UV)*COLOR;float n=fract(sin(dot(floor(UV*200.0),vec2(12.9898,78.233)))*43758.5453);COLOR=vec4(white?vec3(1.0):t.rgb,t.a*(white?1.0-p:step(p,n)));}"
		var mat:=ShaderMaterial.new();mat.shader=shader;mat.set_shader_parameter("white",selected)
		_apply_aspect_material(_visual,mat)
		tween.tween_method(func(v:float):mat.set_shader_parameter("p",v),0.0,1.0,0.2 if selected else 0.8)
	tween.tween_callback(queue_free)
func _apply_aspect_material(node: Node, mat: ShaderMaterial) -> void:
	for child in node.get_children():
		if child is CanvasItem: child.material=mat
		_apply_aspect_material(child,mat)
