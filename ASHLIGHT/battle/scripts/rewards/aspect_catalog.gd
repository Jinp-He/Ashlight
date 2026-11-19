class_name AspectCatalog
extends RefCounted
const ITEMS: Dictionary = {
	"comrades": {"name":"同袍同泽", "icon":1, "description":"周周切蓝被动触发时，获得精通×5点护盾。"},
	"borrow": {"name":"借势而动", "icon":2, "description":"敌方使战场颜色从其他颜色变为蓝色时，也能触发周周的切蓝被动。"},
	"flash_return": {"name":"闪返无耗", "icon":3, "description":"【闪回】攻击牌结算后，保留使用前【士气】的一半，向下取整。"},
	"sleeve": {"name":"袖里乾坤", "icon":4, "description":"战场颜色从其他颜色变为蓝色时，若手中没有蓝色牌，获得1张蓝色牌【闪回】。"},
	"echo": {"name":"士气回响", "icon":5, "description":"攻击实际消耗【士气】后，周周技能倒计时减少1。"},
	"irene_rain": {"name":"阴雨绵绵", "icon":3, "description":"对敌方施加减益时，每种减益额外增加2层。", "role":"Irene", "placeholder_icon":true},
	"irene_gospel": {"name":"福音", "icon":1, "description":"基础被动有效触发时，获得5点护盾。", "role":"Irene", "placeholder_icon":true},
	"irene_whip": {"name":"鞭笞", "icon":4, "description":"召唤物每次行动改为连续行动2次。", "role":"Irene", "placeholder_icon":true},
	"irene_chain": {"name":"连锁", "icon":2, "description":"艾琳主动技能改为抽取1张与战场颜色相同的牌。", "role":"Irene", "placeholder_icon":true},
	"irene_link": {"name":"心智联结", "icon":5, "description":"召唤物耗尽或被替换时，获得1张对应召唤牌的【闪回】。", "role":"Irene", "placeholder_icon":true},
	"rocket_preheat": {"name":"预热装填", "icon":3, "description":"充能牌加入手牌时，获得精通点充能。", "role":"Rocket", "placeholder_icon":true},
	"rocket_recycle": {"name":"余热回收", "icon":1, "description":"打出充能牌后，获得精通层共享【充能】。", "role":"Rocket", "placeholder_icon":true},
	"rocket_calibrate": {"name":"精准校准", "icon":4, "description":"消耗【调节】打出充能牌时，该牌获得等同于实际消耗层数的充能。", "role":"Rocket", "placeholder_icon":true},
	"rocket_sterilize": {"name":"高温杀菌", "icon":2, "description":"护盾清空时，使自己所有减益的剩余层数减半，向上取整。", "role":"Rocket", "placeholder_icon":true},
	"rocket_supply": {"name":"定向供能", "icon":5, "description":"主动技能成功改色时，若目标牌具有【充能】词条，使其获得2点充能。", "role":"Rocket", "placeholder_icon":true}
}
static func sample(amount: int = 3, role: String = "Zhouzhou") -> Array[String]:
	var pool: Array[String] = []
	for id in ITEMS:
		if str(ITEMS[id].get("role","Zhouzhou"))==role and not RunProgress.has_aspect(id): pool.append(id)
	pool.shuffle()
	pool.resize(mini(amount,pool.size()))
	return pool
