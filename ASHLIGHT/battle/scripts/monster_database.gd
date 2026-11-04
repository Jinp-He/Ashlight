class_name MonsterDatabase
extends RefCounted

## 文件名为场景 unit_id 的 snake_case；新增怪物无需注册到代码字典。
const MONSTER_DIR: String = "res://data/monsters"

static func get_data(id: String) -> MonsterData:
	var path: String = MONSTER_DIR.path_join(id.to_snake_case() + ".tres")
	if not ResourceLoader.exists(path):
		push_error("MonsterDatabase: 缺少怪物资源：" + path)
		return null
	var data := load(path) as MonsterData
	if data == null or data.id != id:
		push_error("MonsterDatabase: 资源类型或怪物ID不匹配：" + path)
		return null
	var errors := data.validation_errors()
	if not errors.is_empty():
		push_error("MonsterDatabase: %s：%s" % [path, "；".join(errors)])
		return null
	return data
