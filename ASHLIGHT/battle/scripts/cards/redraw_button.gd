class_name RedrawButton
extends Button

## 右上角「重新发牌」按钮：点击时回收当前手牌并重新按角色分组发牌。
## 通过 hand_manager 组查找 HandManager，避免硬编码 NodePath（HandManager 在 _ready 里 add_to_group）。

func _ready() -> void:
	text = "整备：抽2 / 时间+2"
	add_to_group("redraw_button")
	pressed.connect(_on_pressed)


func _on_pressed() -> void:
	var hm := get_tree().get_first_node_in_group("hand_manager") as HandManager
	if is_instance_valid(hm):
		hm.redraw()
