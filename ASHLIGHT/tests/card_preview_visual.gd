extends Control

func _ready() -> void:
	get_tree().root.content_scale_size = Vector2i(400, 620)
	get_tree().root.size = Vector2i(400, 620)
	var preview := preload("res://addons/card_inspector/preview.gd").new()
	preview.data = load("res://tests/fixtures/composite_card.tres") as CardData
	preview.set_anchors_and_offsets_preset(Control.PRESET_FULL_RECT)
	add_child(preview)
	for frame in 12: await get_tree().process_frame
	await RenderingServer.frame_post_draw
	var image := get_viewport().get_texture().get_image()
	var args := OS.get_cmdline_user_args()
	var output: String = args[0] if not args.is_empty() else "user://card-preview.png"
	image.save_png(output)
	print("CARD_PREVIEW_VISUAL saved " + output)
	get_tree().quit()
