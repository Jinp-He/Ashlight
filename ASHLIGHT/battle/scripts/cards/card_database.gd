class_name CardDatabase
extends RefCounted

## 卡牌数据入口：读取 CARD_DIR 下的 CardData .tres（每角色一个子文件夹）。
## 新建资源即进入卡牌库；初始卡组由 data/starting_decks/*.tres 单独指定。
const CARD_DIR: String = "res://data/cards"

## 全部卡牌（按文件夹、文件名排序）。
func get_all() -> Array[CardData]:
	var out: Array[CardData] = []
	_collect(CARD_DIR, out)
	return out

## 按角色筛选完整卡牌库，供生成临时牌等效果使用，不决定初始卡组。
func get_by_role(role: String) -> Array[CardData]:
	var out: Array[CardData] = []
	for c in get_all():
		if c.belong_to == role:
			out.append(c)
	return out

## list_directory 在导出包里也能列出 .tres（自动处理 .remap），子文件夹以 "/" 结尾。
func _collect(dir: String, out: Array[CardData]) -> void:
	var entries: PackedStringArray = ResourceLoader.list_directory(dir)
	entries.sort()
	for entry in entries:
		var path: String = dir.path_join(entry.trim_suffix("/"))
		if entry.ends_with("/"):
			_collect(path, out)
		elif entry.get_extension() == "tres":
			var cd := load(path) as CardData
			if cd != null:
				var errors := cd.validation_errors()
				if errors.is_empty():
					out.append(cd)
				else:
					push_error("CardDatabase: %s：%s" % [path, "；".join(errors)])
			else:
				push_warning("CardDatabase: %s 不是 CardData" % path)
