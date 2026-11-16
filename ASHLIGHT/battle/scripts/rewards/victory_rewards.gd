class_name VictoryRewards
extends CanvasLayer
@export var config: RewardConfig = preload("res://data/rewards/default_rewards.tres")
var battle_root
var hand: HandManager
var root: Node2D
var shade: ColorRect
var title: Label
var buttons: Array[Button] = []
var candidates: Array[RewardCandidate] = []
var xp_cards: Array[Node2D] = []
var _refresh_badge: Label
var selected_index := -1
var busy := true
var phase := "waiting"
var active_role := ""
var _xp_hovered: Node2D
var _masked_units: Array[UnitRoot] = []
var _xp_selected: Node2D
var _pointer_down := false
var _dragging := false
var _press_position := Vector2.ZERO
var _tip: UnitBuffTooltip
var _hover_index := -1
var _started := false
var _exiting := false
var _fade_timer: Timer
var _xp_entry_timer: Timer
var _choice_entry_timer: Timer
var _xp_entry_pending := true
var _shade_tween: Tween
func _ready() -> void:
	layer=120
	shade=ColorRect.new();shade.color=Color(0,0,0,0.6);shade.mouse_filter=Control.MOUSE_FILTER_STOP;shade.visible=false;add_child(shade)
	root=Node2D.new();add_child(root)
	title=Label.new();title.add_theme_font_override("font",preload("res://assets/fonts/Biantao.ttf"));title.add_theme_font_size_override("font_size",34);title.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER;title.position=Vector2(300,config.title_y);title.size=Vector2(1000,60);title.mouse_filter=Control.MOUSE_FILTER_IGNORE;root.add_child(title)
	for i in 3:
		var button:=Button.new();button.text=["确认","刷新","结束"][i];button.position=Vector2(525+i*200,config.button_y);button.size=Vector2(150,55);button.add_theme_font_size_override("font_size",24);root.add_child(button);buttons.append(button)
	_refresh_badge=Label.new();_refresh_badge.position=Vector2(123,-14);_refresh_badge.size=Vector2(28,25);_refresh_badge.horizontal_alignment=HORIZONTAL_ALIGNMENT_CENTER;_refresh_badge.add_theme_font_size_override("font_size",20);_refresh_badge.mouse_filter=Control.MOUSE_FILTER_IGNORE;buttons[1].add_child(_refresh_badge)
	buttons[0].pressed.connect(confirm);buttons[1].pressed.connect(refresh);buttons[2].pressed.connect(skip)
	_tip=UnitBuffTooltip.new();_tip.z_index=50;root.add_child(_tip)
	_show_buttons(false)
	_resize()
func _resize() -> void:
	var size:=get_viewport().get_visible_rect().size
	shade.size=size
	var factor:=minf(size.x/1600.0,size.y/900.0)
	root.scale=Vector2.ONE*factor;root.position=(size-Vector2(1600,900)*factor)*0.5
	if is_instance_valid(_tip):
		_tip.scene_height=size.y
		_tip.bottom_margin=50.0*factor
func begin() -> void:
	if _started:return
	_started=true
	hand=get_tree().get_first_node_in_group("hand_manager") as HandManager
	while is_inside_tree() and (battle_root.clock_busy or hand._visual_return_busy or battle_root._get_units().any(func(u:UnitRoot)->bool:return u.unit_faction=="enemy" and u.get_current_hp()<=0)):
		await get_tree().process_frame
	if not is_inside_tree():return
	if not RunProgress.battle.claimed:
		RunProgress.battle.claimed=true
		RunProgress.battle.remaining=battle_root._defeated_xp
		RunProgress.battle.phase="xp"
		RunProgress.save_deck(hand)
	await hand.collect_for_rewards()
	if not is_inside_tree():return
	battle_root.get_node("HUD/TeamHUD").hide()
	battle_root._team._buff_component.hide()
	for unit in battle_root._get_units():
		if unit.unit_faction=="player":unit.toggle_bar_display(true)
		else:unit.hide()
	if RunProgress.battle.phase=="done":await _finish();return
	_xp_entry_pending=RunProgress.battle.phase=="xp"
	_rebuild_xp()
	await _next()
func _show_buttons(value:bool) -> void:
	for button in buttons:button.visible=value
func _rebuild_xp() -> void:
	for card in xp_cards:
		if is_instance_valid(card):card.queue_free()
	xp_cards.clear();_xp_hovered=null
	var amount:int=RunProgress.battle.remaining
	for i in amount:
		var card:=Node2D.new();card.name="ExperienceCard";root.add_child(card)
		var art:=Sprite2D.new();art.texture=preload("res://assets/rewards/experience_card.png");art.scale=Vector2.ONE*config.xp_card_scale;card.add_child(art)
		card.position=Vector2(800+(i-(amount-1)*0.5)*config.xp_center_spacing,config.xp_hand_y)
		card.rotation=deg_to_rad(config.xp_fan_degrees*(float(i)/maxi(amount-1,1)*2.0-1.0)) if amount>1 else 0.0
		card.z_index=i
		card.set_meta("home_rotation",card.rotation);card.set_meta("base_z",i)
		card.set_meta("home",card.position);xp_cards.append(card)
func _next() -> void:
	if not is_inside_tree():return
	if not RunProgress.battle.queue.is_empty():
		var stage:Dictionary=RunProgress.battle.queue[0]
		active_role=str(stage.role);phase=str(stage.phase)
		RunProgress.battle.role=active_role;RunProgress.battle.phase=phase
		await _open_choice()
	elif int(RunProgress.battle.remaining)>0:
		busy=true
		phase="xp";RunProgress.battle.phase=phase;
		for xp_card in xp_cards:xp_card.show()
		_show_buttons(false);title.text="分配经验：选择经验牌，再点击角色"
		await _fade_shade_out()
		if _exiting:return
		if _xp_entry_pending:await _play_xp_entry()
		if _exiting:return
		busy=false
	else:await _finish()
func _finish() -> void:
	_clear_candidates();phase="done";RunProgress.battle.phase="done";busy=true;_show_buttons(false);title.text="经验分配完成";_tip.hide_tooltip()
	await _fade_shade_out()
	if _exiting:return
	RunProgress.save_deck(hand)
func _generate() -> Array:
	if phase=="cards":return config.sample_cards(active_role,int(RunProgress.roles[active_role].level))
	if active_role=="Zhouzhou":return AspectCatalog.sample()
	return []
func _open_choice() -> void:
	busy=true;selected_index=-1;_hover_index=-1;_tip.hide_tooltip();
	for xp_card in xp_cards:xp_card.hide()
	_set_character_outlines_blocked(true)
	_show_buttons(true)
	for button in buttons:button.disabled=true
	title.text="选择1张牌加入牌库" if phase=="cards" else "选择1种百相"
	var choices:Array=[]
	if not RunProgress.battle.candidates.is_empty():
		for item in RunProgress.battle.candidates:
			choices.append(load(str(item)) if phase=="cards" else str(item))
	else:choices=_generate()
	if choices.is_empty():
		RunProgress.battle.queue.pop_front();RunProgress.battle.candidates=[]
		await _next();return
	RunProgress.battle.candidates=[]
	for item in choices:RunProgress.battle.candidates.append(item.resource_path if phase=="cards" else item)
	_clear_candidates()
	_fade_shade_in()
	for i in choices.size():
		var candidate:=RewardCandidate.new();candidate.config=config
		if phase=="cards":candidate.card_data=choices[i]
		else:candidate.aspect_id=choices[i]
		root.add_child(candidate);candidates.append(candidate)
		var slot:int=i if choices.size()==3 else (i+1 if choices.size()==1 else i)
		candidate.base_position=config.candidate_positions[slot]
		if choices.size()==2:candidate.base_position=Vector2(600+i*400,480)
		candidate.scale=Vector2.ONE*(config.card_scale if phase=="cards" else config.aspect_scale)
		candidate.position=Vector2(-350,480) if phase=="cards" else candidate.base_position
		candidate.modulate.a=1 if phase=="cards" else 0
		var tween:=candidate.create_tween()
		if phase=="cards":tween.tween_property(candidate,"position",candidate.base_position,config.card_entry_duration).set_trans(Tween.TRANS_CUBIC).set_ease(Tween.EASE_OUT)
		else:tween.tween_property(candidate,"modulate:a",1.0,config.aspect_entry_duration)
	var entry_timer:=Timer.new();entry_timer.one_shot=true;add_child(entry_timer)
	_choice_entry_timer=entry_timer;entry_timer.start(maxf(0.001,maxf(config.shade_fade_in_duration,config.card_entry_duration if phase=="cards" else config.aspect_entry_duration)))
	await entry_timer.timeout
	_choice_entry_timer=null;entry_timer.queue_free()
	if _exiting or not is_inside_tree():return
	for candidate in candidates:candidate.ready_input=true
	busy=false;_update_buttons()
func _update_buttons() -> void:
	buttons[0].disabled=busy or selected_index<0
	buttons[1].disabled=busy or RunProgress.refresh_count()<=0
	buttons[1].text="刷新"
	_refresh_badge.text=str(RunProgress.refresh_count())
	buttons[2].disabled=busy
func _clear_candidates() -> void:
	_tip.hide_tooltip();_hover_index=-1
	for candidate in candidates:
		if is_instance_valid(candidate):candidate.queue_free()
	candidates.clear()
func _mouse() -> Vector2:return root.get_global_transform_with_canvas().affine_inverse()*get_viewport().get_mouse_position()
func _process(_delta:float) -> void:
	if not is_instance_valid(root):return
	_resize()
	if is_instance_valid(_xp_selected):_xp_selected.position=_mouse()+Vector2(35,45)
	if phase=="xp" and not busy:_update_xp_hover(_delta)
	if busy or phase not in ["cards","aspects"]:return
	var hit:=-1
	var point:=root.to_global(_mouse())
	for i in candidates.size():
		if candidates[i].ready_input and candidates[i].contains(point):hit=i
	if hit==_hover_index:return
	_hover_index=hit;_tip.hide_tooltip()
	for i in candidates.size():
		candidates[i].set_hover(i==hit and i!=selected_index)
		candidates[i].set_hover_dimming(i!=hit)
	if hit>=0 and hit!=selected_index:
		var candidate:=candidates[hit]
		var sections:Array[Dictionary]=[]
		if phase=="cards":sections=hand._card_keyword_sections(candidate.card_data)
		else:
			var pattern:=RegEx.new();pattern.compile("【([^】]+)】")
			for matched in pattern.search_all(AspectCatalog.ITEMS[candidate.aspect_id].description):
				var term:String=matched.get_string(1)
				if KeywordGlossary.ENTRIES.has(term):sections.append({"title":term,"description":KeywordGlossary.ENTRIES[term]})
		if not sections.is_empty():
			var width:float=(400 if phase=="cards" else 238)*candidate.scale.x
			var x:float=candidate.base_position.x+width*0.5+15
			if x+250>1600:x=candidate.base_position.x-width*0.5-265
			_tip.show_sections(sections,Vector2(x,250))
func _input(event:InputEvent) -> void:
	if phase in ["waiting","done"]:return
	if busy:
		if event is InputEventMouseButton:get_viewport().set_input_as_handled()
		return
	if not event is InputEventMouseButton and not event is InputEventMouseMotion:return
	if event is InputEventMouseMotion:
		if _pointer_down and event.position.distance_to(_press_position)>12:_dragging=true
		return
	var mouse:=event as InputEventMouseButton
	if phase in ["cards","aspects"]:
		if mouse.pressed and mouse.button_index==MOUSE_BUTTON_LEFT:
			for i in candidates.size():
				if candidates[i].contains(root.to_global(_mouse())):
					selected_index=i;_tip.hide_tooltip()
					for j in candidates.size():candidates[j].select(i==j)
					_update_buttons();get_viewport().set_input_as_handled();return
		return
	if phase!="xp":return
	if mouse.pressed and mouse.button_index==MOUSE_BUTTON_RIGHT:_cancel_xp();get_viewport().set_input_as_handled();return
	if mouse.button_index!=MOUSE_BUTTON_LEFT:return
	if mouse.pressed:
		if is_instance_valid(_xp_selected):
			_try_assign(mouse.position);get_viewport().set_input_as_handled();return
		var card:=_xp_card_at(_mouse())
		if is_instance_valid(card):
			_xp_selected=card;_xp_hovered=null;card.rotation=0.0;card.scale=Vector2.ONE*0.5;card.z_index=20;_pointer_down=true;_dragging=false;_press_position=mouse.position;get_viewport().set_input_as_handled();return
	elif _pointer_down:
		_pointer_down=false
		if _dragging:_try_assign(mouse.position)
func _cancel_xp() -> void:
	if is_instance_valid(_xp_selected):
		_xp_selected.position=_xp_selected.get_meta("home");_xp_selected.scale=Vector2.ONE;_xp_selected.z_index=int(_xp_selected.get_meta("base_z"));_xp_selected.rotation=float(_xp_selected.get_meta("home_rotation"))
	_xp_selected=null;_pointer_down=false;_dragging=false
func _try_assign(canvas_point:Vector2) -> void:
	var point: Vector2 = battle_root.get_canvas_transform().affine_inverse()*canvas_point
	for unit in battle_root._get_units():
		if unit.unit_faction=="player" and unit.is_point_inside(point):
			assign_experience(unit.unit_id);return
	_cancel_xp()
func assign_experience(uid:String) -> bool:
	if busy or phase!="xp" or int(RunProgress.battle.remaining)<=0:return false
	var unit: UnitRoot = battle_root._find_unit(uid)
	if not is_instance_valid(unit) or unit.unit_faction!="player":return false
	busy=true
	_xp_selected=null;_pointer_down=false;_dragging=false
	RunProgress.battle.remaining-=1
	var old_level:int=unit.current_level
	unit.add_xp(1)
	RunProgress.sync_role(unit)
	_rebuild_xp()
	if not RunProgress.battle.used_roles.has(uid):
		RunProgress.battle.used_roles.append(uid)
		RunProgress.battle.queue.append({"role":uid,"phase":"cards"})
	for level in range(old_level+1,unit.current_level+1):
		if uid=="Zhouzhou":RunProgress.battle.queue.append({"role":uid,"phase":"aspects"})
	if unit.current_level>old_level and is_instance_valid(unit._upgrade_anim) and unit._upgrade_anim.is_playing():
		await unit._upgrade_anim.animation_finished
	if not is_inside_tree():return true
	await _next()
	return true
func refresh() -> void:
	if busy or RunProgress.refresh_count()<=0:return
	var next:=_generate()
	if next.is_empty():return
	busy=true
	if not RunProgress.spend_refresh():busy=false;return
	RunProgress.battle.candidates=[]
	for item in next:RunProgress.battle.candidates.append(item.resource_path if phase=="cards" else item)
	await _open_choice()
func confirm() -> void:
	if busy or selected_index<0 or selected_index>=candidates.size():return
	busy=true;_update_buttons();_tip.hide_tooltip()
	var selected:=candidates[selected_index]
	if phase=="cards":
		hand.deck.draw_pile.append(selected.card_data)
		RunProgress.save_deck(hand)
	else:RunProgress.grant_aspect(selected.aspect_id)
	_commit_choice()
	_depart(selected_index)
	var departure_timer:=Timer.new();departure_timer.one_shot=true;add_child(departure_timer);departure_timer.start(0.85)
	await departure_timer.timeout
	departure_timer.queue_free()
	if is_inside_tree():await _complete_choice()
func skip() -> void:
	if busy:return
	busy=true;_update_buttons();_tip.hide_tooltip()
	_commit_choice()
	_depart(-1)
	var departure_timer:=Timer.new();departure_timer.one_shot=true;add_child(departure_timer);departure_timer.start(0.85)
	await departure_timer.timeout
	departure_timer.queue_free()
	if is_inside_tree():await _complete_choice()
func _depart(selected:int) -> void:
	var pile:=get_tree().get_first_node_in_group("deck_pile") as Control
	var destination:=Vector2(130,820)
	if is_instance_valid(pile):destination=root.to_local(pile.get_global_rect().get_center())
	for i in candidates.size():candidates[i].depart(i==selected,destination)
func _commit_choice() -> void:
	# 先提交阶段，再播放退场，避免退场中重进重复领取。
	RunProgress.battle.candidates=[]
	RunProgress.battle.queue.pop_front()
func _complete_choice() -> void:
	_clear_candidates()
	await _next()

func _xp_contains(card: Node2D, point: Vector2) -> bool:
	var size:Vector2=Vector2(276,680)*config.xp_card_scale
	return Rect2(-size*0.5,size).has_point(card.to_local(root.to_global(point)))

func _xp_card_at(point: Vector2) -> Node2D:
	if is_instance_valid(_xp_hovered) and _xp_contains(_xp_hovered,point):return _xp_hovered
	for i in range(xp_cards.size()-1,-1,-1):
		if _xp_contains(xp_cards[i],point):return xp_cards[i]
	return null

func _update_xp_hover(delta: float) -> void:
	if is_instance_valid(_xp_selected):return
	var hit:=_xp_card_at(_mouse())
	_xp_hovered=hit
	var blend:=1.0-exp(-18.0*delta)
	for card in xp_cards:
		var hovered:bool=card==hit
		var target_scale:float=config.xp_hover_scale if hovered else 1.0
		card.scale=card.scale.lerp(Vector2.ONE*target_scale,blend)
		card.rotation=lerp_angle(card.rotation,0.0 if hovered else float(card.get_meta("home_rotation")),blend)
		var target:Vector2=card.get_meta("home")
		if hovered:
			target.y=900.0-config.xp_hover_bottom_margin-680.0*config.xp_card_scale*target_scale*0.5
		card.position=card.position.lerp(target,blend)
		card.z_index=20 if hovered else int(card.get_meta("base_z"))
		if hovered:
			var extent:float=config.xp_card_scale*card.scale.y*(absf(sin(card.rotation))*276.0+absf(cos(card.rotation))*680.0)*0.5
			card.position.y=minf(card.position.y,900.0-config.xp_hover_bottom_margin-extent)

func _set_character_outlines_blocked(blocked: bool) -> void:
	if blocked:
		_masked_units.clear()
		for unit in battle_root._get_units():
			if unit.unit_faction=="player":_masked_units.append(unit);unit.set_reward_outline_blocked(true)
	else:
		for unit in _masked_units:
			if is_instance_valid(unit):unit.set_reward_outline_blocked(false)
		_masked_units.clear()

func _fade_shade_out() -> void:
	if shade.visible:
		if is_instance_valid(_shade_tween):_shade_tween.kill()
		_shade_tween=create_tween()
		_shade_tween.tween_property(shade,"modulate:a",0.0,config.shade_fade_duration)
		var fade_timer:=Timer.new();fade_timer.one_shot=true;add_child(fade_timer);fade_timer.start(maxf(config.shade_fade_duration,0.001))
		_fade_timer=fade_timer
		await fade_timer.timeout
		_fade_timer=null
		fade_timer.queue_free()
		if _exiting or not is_inside_tree():return
		shade.hide();shade.modulate.a=1.0
	_set_character_outlines_blocked(false)

func _exit_tree() -> void:
	_exiting=true
	# 退出前结束淡出等待，使上层阶段协程有机会收尾。
	if is_instance_valid(_fade_timer):_fade_timer.timeout.emit()
	if is_instance_valid(_xp_entry_timer):_xp_entry_timer.timeout.emit()
	if is_instance_valid(_choice_entry_timer):_choice_entry_timer.timeout.emit()
	_set_character_outlines_blocked(false)

func _fade_shade_in() -> void:
	# 连续选牌、百相与刷新沿用现有遮罩，不重复闪回透明。
	if shade.visible:return
	if is_instance_valid(_shade_tween):_shade_tween.kill()
	shade.modulate.a=0.0;shade.show()
	_shade_tween=create_tween()
	_shade_tween.tween_property(shade,"modulate:a",1.0,config.shade_fade_in_duration)

func _play_xp_entry() -> void:
	_xp_entry_pending=false
	if xp_cards.is_empty():return
	var white:=ShaderMaterial.new();white.shader=preload("res://battle/scripts/rewards/xp_reveal.gdshader")
	white.set_shader_parameter("white_amount",1.0)
	var tween:=create_tween().set_parallel(true)
	for card in xp_cards:
		var art:=card.get_child(0) as Sprite2D
		art.material=white;art.scale=Vector2(config.xp_card_scale,0.0)
		tween.tween_property(art,"scale:y",config.xp_card_scale,config.xp_expand_duration)
	tween.chain().tween_method(func(value:float):white.set_shader_parameter("white_amount",value),1.0,0.0,config.xp_restore_duration)
	var timer:=Timer.new();timer.one_shot=true;add_child(timer)
	_xp_entry_timer=timer;timer.start(maxf(config.xp_expand_duration+config.xp_restore_duration,0.001))
	await timer.timeout
	_xp_entry_timer=null;timer.queue_free()
	if _exiting:return
	for card in xp_cards:
		var art:=card.get_child(0) as Sprite2D
		art.scale=Vector2.ONE*config.xp_card_scale;art.material=null
