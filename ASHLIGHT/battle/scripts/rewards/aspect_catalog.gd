class_name AspectCatalog
extends RefCounted
const ITEMS: Dictionary = {
	"comrades": {"name":"同袍同泽", "icon":1, "description":"周周切蓝被动触发时，获得精通×5点护盾。"},
	"borrow": {"name":"借势而动", "icon":2, "description":"敌方使战场颜色从其他颜色变为蓝色时，也能触发周周的切蓝被动。"},
	"flash_return": {"name":"闪返无耗", "icon":3, "description":"【闪回】攻击牌结算后，保留使用前【士气】的一半，向下取整。"},
	"sleeve": {"name":"袖里乾坤", "icon":4, "description":"战场颜色从其他颜色变为蓝色时，若手中没有蓝色牌，获得1张蓝色牌【闪回】。"},
	"echo": {"name":"士气回响", "icon":5, "description":"攻击实际消耗【士气】后，周周技能倒计时减少1。"}
}
static func sample(amount: int = 3) -> Array[String]:
	var pool: Array[String] = []
	for id in ITEMS:
		if not RunProgress.has_aspect(id): pool.append(id)
	pool.shuffle()
	pool.resize(mini(amount,pool.size()))
	return pool
