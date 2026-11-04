extends Node
var failures: int = 0
func check(ok: bool, message: String) -> void:
	print("PASS FLAME: " if ok else "FAIL FLAME: ", message)
	if not ok:
		failures += 1
func _ready() -> void:
	call_deferred("run")
func run() -> void:
	var br := (load("res://battle/Battle.tscn") as PackedScene).instantiate() as BattleRoot
	add_child(br)
	await get_tree().create_timer(0.8).timeout
	var flame := br._top_flame
	check(flame.size == Vector2(322, 90), "flame keeps original dimensions")
	check(flame.global_position.is_equal_approx(Vector2(639, 0)), "flame is flush top and horizontally centered")
	check(br.get_node_or_null("HUD/PileTop/Outline") == null and br.get_node_or_null("HUD/PileTop/Diamond") == null, "old diamond fill and border are removed")
	check(absf(br._top_number.position.x + 50.0) <= 3.01 and absf(br._top_number.position.y + 32.0) <= 3.01 and br._top_number.get_theme_font_size("font_size") == 44, "central number floats within its 3px anchor bounds and retains style")
	check(flame.front.texture == flame.back.texture, "both layers use synchronized animation frames")
	var previous_frame: int = flame._frame
	await get_tree().create_timer(0.11).timeout
	check(flame._frame != previous_frame, "GIF advances at 0.1 seconds per frame")
	for index in CardRoot.COST_COLORS.size():
		br.rules.place_player_card(index, 3, "", "测试")
		br._v2_refresh_hud()
		await get_tree().create_timer(0.4).timeout
		check(flame.current_tint.is_equal_approx(CardRoot.COST_COLORS[index]), "flame follows card palette index %d" % index)
		check(br._top_number.text == "3", "pile number still follows rules")
	var next: int = (CardRoot.COST_COLORS.size() - 2) % CardRoot.COST_COLORS.size()
	flame.set_color_index(next)
	await get_tree().create_timer(0.12).timeout
	var progress: float = flame._transition_time
	flame.set_color_index(next)
	check(flame._transition_active and flame.back.visible, "new-color layer appears below dissolving old layer")
	check(is_equal_approx(flame._transition_time, progress), "same-color refresh does not restart dissolve")
	check(flame.front.texture == flame.back.texture, "animation frames remain synchronized while dissolving")
	await get_tree().create_timer(0.23).timeout
	check(not flame._transition_active and not flame.back.visible, "0.3-second transition completes and cleans old layer")
	flame.set_color_index(0)
	flame.set_color_index(1)
	flame.set_color_index(2)
	await get_tree().create_timer(0.7).timeout
	check(flame.current_tint.is_equal_approx(CardRoot.COST_COLORS[2]), "rapid updates settle on latest palette color")
	br.queue_free()
	await get_tree().process_frame
	print("PILE_FLAME_TEST failures=", failures)
	get_tree().quit(0 if failures == 0 else 1)
