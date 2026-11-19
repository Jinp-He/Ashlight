class_name RunProgress
extends RefCounted
## 仅内存：跨场景保留，退出程序即清除。新局由 reset_run 显式重置。
static var roles: Dictionary = {}
static var cards: Array[CardData] = []
static var aspects: Array[String] = []
static var refreshes: int = 3
static var battle: Dictionary = {}
static var _serial: int = 0
static var _cleanup_connected: bool = false
static func ensure() -> void:
	if not _cleanup_connected and Engine.get_main_loop() is SceneTree:
		var tree := Engine.get_main_loop() as SceneTree
		tree.root.tree_exiting.connect(_release_resources,CONNECT_ONE_SHOT)
		_cleanup_connected=true
	for uid in ["Zhouzhou", "Irene", "Rocket"]:
		if not roles.has(uid): roles[uid] = {"level": 1, "xp": 0.0}
	if battle.is_empty(): begin_battle()
static func begin_battle() -> void:
	_serial += 1
	battle = {"id": _serial, "claimed": false, "remaining": 0, "used_roles": [], "queue": [], "phase": "combat", "role": "", "candidates": []}
static func reset_run() -> void:
	roles.clear(); cards.clear(); aspects.clear(); refreshes = 3; battle.clear(); ensure()
static func refresh_count() -> int: return refreshes
static func add_refreshes(amount: int) -> void: refreshes = maxi(0, refreshes + amount)
static func spend_refresh() -> bool:
	if refreshes <= 0: return false
	refreshes -= 1
	return true
static func reset_refreshes(amount: int = 3) -> void: refreshes = maxi(0, amount)
static func has_aspect(id: String) -> bool: return aspects.has(id)
static func grant_aspect(id: String) -> bool:
	if aspects.has(id): return false
	aspects.append(id)
	return true
static func sync_role(unit: UnitRoot) -> void:
	var info := unit.get_xp_info()
	roles[unit.unit_id] = {"level": int(info.level), "xp": float(info.current)}
static func save_deck(hand) -> void:
	cards.clear()
	cards.append_array(hand.deck.draw_pile)
	cards.append_array(hand.deck.discard_pile)
	for hc in hand._cards:
		if is_instance_valid(hc) and not hc.is_flashback: cards.append(hc.data)

static func _release_resources() -> void:
	cards.clear()
