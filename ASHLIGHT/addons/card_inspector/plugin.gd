@tool
extends EditorPlugin

var inspector: EditorInspectorPlugin

func _enter_tree() -> void:
	inspector = preload("res://addons/card_inspector/inspector.gd").new()
	add_inspector_plugin(inspector)

func _exit_tree() -> void:
	remove_inspector_plugin(inspector)
	inspector = null
