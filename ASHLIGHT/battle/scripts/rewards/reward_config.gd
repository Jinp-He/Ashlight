class_name RewardConfig
extends Resource
@export var rarity_weights: Array[Vector3] = [Vector3(90,10,0), Vector3(65,30,5), Vector3(45,45,10), Vector3(30,50,20)]
@export var candidate_positions: PackedVector2Array = PackedVector2Array([Vector2(430,480),Vector2(800,480),Vector2(1170,480)])
@export var card_scale: float = 0.72
@export var aspect_scale: float = 1.4
@export var card_entry_duration: float = 0.5
@export var aspect_entry_duration: float = 0.3
@export var hover_lift: float = 24.0
@export var hover_scale: float = 1.04
@export var perspective_degrees: float = 4.0
@export var float_amplitude: Vector2 = Vector2(4,7)
@export var button_y: float = 780.0
@export var title_y: float = 125.0
@export var xp_card_scale: float = 0.36
@export var xp_hand_y: float = 800.0
@export var xp_center_spacing: float = 80.0
@export var xp_fan_degrees: float = 3.0
@export var xp_hover_scale: float = 1.4
@export var xp_hover_bottom_margin: float = 1.0
@export var choice_outline_pixels: float = 5.0
@export var shade_fade_duration: float = 0.2
@export var shade_fade_in_duration: float = 0.2
@export var xp_expand_duration: float = 0.1
@export var xp_restore_duration: float = 0.2
@export var normal_xp: int = 1
@export var elite_xp: int = 2
@export var boss_xp: int = 3
func weights(level: int) -> Vector3:
	return rarity_weights[clampi(level-1,0,rarity_weights.size()-1)]
func sample_cards(uid: String, level: int, amount: int = 3) -> Array[CardData]:
	var pool: Array[CardData] = []
	for cd in CardDatabase.new().get_all():
		var suffix: int = cd.id.get_slice("_",1).to_int()
		if cd.belong_to == uid and suffix >= 11 and suffix <= 20: pool.append(cd)
	var result: Array[CardData] = []
	var w := weights(level)
	for slot in amount:
		var groups: Array = [[],[],[]]
		for cd in pool: groups[clampi(cd.rarity-1,0,2)].append(cd)
		var total := 0.0
		for i in 3:
			if not groups[i].is_empty(): total += w[i]
		if total <= 0: break
		var pick := randf()*total
		var index := 0
		for i in 3:
			if groups[i].is_empty() or w[i] <= 0: continue
			index=i
			pick -= w[i]
			if pick < 0: index=i; break
		var selected: CardData = groups[index].pick_random()
		result.append(selected)
		for other in pool.duplicate():
			if other.card_name == selected.card_name: pool.erase(other)
	return result
