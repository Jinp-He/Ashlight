@tool
extends EditorInspectorPlugin

func _can_handle(object: Object) -> bool:
	return object is CardData

func _parse_begin(object: Object) -> void:
	var preview := preload("res://addons/card_inspector/preview.gd").new()
	preview.data = object as CardData
	add_custom_control(preview)
