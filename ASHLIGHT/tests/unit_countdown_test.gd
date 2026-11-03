extends Node
var failures: int = 0
func check(ok: bool, message: String) -> void:
	print("PASS UNIT HUD: " if ok else "FAIL UNIT HUD: ", message)
	if not ok:
		failures += 1
func _ready() -> void:
	call_deferred("run")
func run() -> void:
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().process_frame
	for unit in br._get_units():
		var old_bar_y: float = unit.visual_offset_y + unit.hp_offset_y
		var old_buff_y: float = old_bar_y + unit.BAR_HEIGHT + unit.buff_offset_y - 15
		check(is_equal_approx(unit._hp_component.position.y, old_bar_y + 18), "HP group includes independent 3px nudge")
		check(is_equal_approx(unit._buff_component.position.y, old_buff_y + 15), "buff box moves down 5px from previous layout")
		check(is_equal_approx(unit._buff_component.position.y + unit._buff_component._toast_root.position.y,
			old_buff_y + unit._buff_component.toast_offset_y), "buff toast stays at previous position")
		var countdown := unit._countdown_component
		check(is_zero_approx(countdown.position.x) and is_equal_approx(countdown.position.y, old_bar_y + 33), "countdown stays centered and moves down 5px independently")
		check(is_zero_approx(countdown.icon.position.x + countdown.text_label.position.x + countdown.text_label.size.x), "icon and text bounds center on unit anchor")
		check(countdown.icon.size == Vector2(24, 24), "icon keeps 24px size")
		check(countdown.text_label.get_theme_font("font").resource_path.ends_with("DouyinSansBold.otf"), "countdown uses project Douyin font")
		check(unit.get_node_or_null("OverheadCountdown") == null, "legacy overhead label is absent")
	var ally := br._find_unit("Rocket")
	check(ally._countdown_component.text_label.text == "就绪", "ready ally uses icon plus ready")
	check(not br._intent_target_labels.Rocket.text.contains("就绪"), "skill description no longer repeats ready")
	br.rules.skill_cooldowns.Rocket = 3
	br.rules.enemies.Remy = 2
	br._v2_refresh()
	check(ally._countdown_component.text_label.text == "3", "skill cooldown uses existing rule value")
	check(br._find_unit("Remy")._countdown_component.text_label.text == "2", "enemy countdown uses existing rule value")
	br.rules.skill_cooldowns.Rocket = 0
	br._v2_refresh()
	check(ally._countdown_component.text_label.text == "就绪", "cooldown zero restores ready display")
	var base_y: float = ally._hp_component.position.y - ally.unit_hud_shift_y
	var toast_y: float = ally._buff_component._toast_root.global_position.y
	ally.unit_hud_shift_y = 45
	check(is_equal_approx(ally._hp_component.position.y, base_y + 45), "HUD offset remains adjustable")
	check(is_equal_approx(ally._buff_component._toast_root.global_position.y, toast_y), "changing HUD offset still preserves toast position")
	br.queue_free()
	await get_tree().process_frame
	print("UNIT_COUNTDOWN_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)
