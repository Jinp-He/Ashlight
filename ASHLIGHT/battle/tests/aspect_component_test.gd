extends Node2D
## 测试场景专用：每次运行随机一次，颜色沿用卡牌主题蓝、绿、红。
@export var icon_palette: Array[Color] = [Color(0.0,0.631,1.0),Color(0.439,0.671,0.231),Color(0.890,0.231,0.231)]
func _ready() -> void:
	if not icon_palette.is_empty():$AspectGroup/Icon.self_modulate=icon_palette.pick_random()
