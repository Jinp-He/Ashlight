class_name CutsceneTestController
extends CutsceneController

## 演出编排器「零件测试」专用控制器：不滑入、不播插入帧，仅把单位图静态放置于场中，
## 供直接点击按钮触发 buff 提示 / 伤害跳字 等零件效果、实时目测调整参数。
## 直接复用父类 _spawn_buff_prompt / _spawn_damage_number（用户后续调整这两个函数即对整个演出生效）。

const SLOT_BASELINE: float = 560.0                     # 单位脚线 y（底部对齐：脚线落此）

# 默认满编名册：3 我方 + 3 敌方（与 Battle.tscn 单位布局一致），填满编排器全部 6 槽位。
const DEFAULT_ROSTER: Array[Dictionary] = [
	{"id": "Zhouzhou",  "faction": "player", "state": 0},
	{"id": "Irene",     "faction": "player", "state": 0},
	{"id": "Rocket",    "faction": "player", "state": 0},
	{"id": "Banner",    "faction": "enemy",  "state": 0},
	{"id": "DogKnight", "faction": "enemy",  "state": 0},
	{"id": "Tusk",      "faction": "enemy",  "state": 0},
]

var _test_sprites: Array[Sprite2D] = []
var _test_stretch_materials: Array[ShaderMaterial] = []   # 与 _test_sprites 对齐：静态单位的横向拉伸材质（未启用则 null）

## 静态放置单位（不滑入、不触发父类 _process 运动）。units 缺省用 DEFAULT_ROSTER 填满 6 槽位。
## 排布复刻父类 _start_play 的槽位布局（阵营内 intra_spacing / 跨阵营 cross_gap / 居中于 design_width 中心），仅就地摆好、不滑入。
## 注意：本方法不走父类的 play_units/_start_play（后者会置 _active=true 启动滑入）。
func build_static_units(units: Array[Dictionary] = []) -> void:
	_clear_units()
	for s in _test_sprites:
		if is_instance_valid(s):
			s.queue_free()
	_test_sprites.clear()
	_test_stretch_materials.clear()
	if units.is_empty():
		units = DEFAULT_ROSTER
	# 阵营排序：我方恒左、敌方恒右（与父类一致）。
	var sorted_u: Array[Dictionary] = []
	var enemy_u: Array[Dictionary] = []
	for u in units:
		if u.get("faction", "player") == "enemy":
			enemy_u.append(u)
		else:
			sorted_u.append(u)
	sorted_u.append_array(enemy_u)
	units = sorted_u
	# 复刻父类槽位布局数学：阵营内 intra_spacing、跨阵营 cross_gap，整组居中于 design_width 中心。
	var intra_spacing: float = 170.0
	var cross_gap: float = 266.7
	var design_w: float = 1600.0
	var rel_x: Array[float] = []
	var cum: float = 0.0
	for i in units.size():
		if i > 0:
			var is_cross: bool = units[i - 1].get("faction", "player") != units[i].get("faction", "player")
			cum += cross_gap if is_cross else intra_spacing
		rel_x.append(cum)
	var mid_off: float = design_w * 0.5 - cum * 0.5
	for i in units.size():
		var u: Dictionary = units[i]
		var x: float = rel_x[i] + mid_off
		var spr := Sprite2D.new()
		spr.centered = true
		spr.texture = _load_unit_texture(u.get("id", ""), u.get("faction", "player"), int(u.get("state", 0)))
		spr.scale = Vector2.ONE * unit_scale
		var h: float = spr.texture.get_height() if spr.texture != null else 0.0
		spr.position = Vector2(x, SLOT_BASELINE - h * unit_scale * 0.5)
		spr.z_index = 1
		spr.set_meta("faction", u.get("faction", "player"))   # 伤害数字水平区间按阵营取位
		add_child(spr)
		_test_sprites.append(spr)
		_test_stretch_materials.append(_attach_stretch(spr))   # 挂载滑入形变着色器（未启用则为 null）

## 对随机一个单位挂载 buff/debuff 提示（复用父类 _spawn_buff_prompt_static：持久挂载、无获取动画）。
## name：提示条名称；tone：1=有益(绿)/2=有害(红)/其他=中性；icon：图标文件名(不含.png)，留空则按 tone 兜底 buff001/buff002。
func test_buff_prompt(name: String, tone: int, icon: String = "") -> void:
	if _test_sprites.is_empty():
		return
	clear_fx()
	var spr := _test_sprites[randi() % _test_sprites.size()]
	_spawn_buff_prompt_static(spr, name, tone, icon)

## 对指定单位（缺省随机）触发一次伤害跳字（复用父类 _spawn_damage_number，委托 DamageNumber 组件）。
## 本函数不再清场；连续多段由调用方先 clear_fx() 一次、再多次调用，使多段同时显示且各自随机定位。
## target：传入则固定命中该单位（用于演示「同角色同次多段」间距）；为空则随机取一个单位。
## payload：见 FX/DamageNumber/README_DamageNumber.md §9 数据契约（type/value/is_crit/source/absorbed_amount/blocked）。
func test_damage_number(payload: Dictionary, target: Sprite2D = null) -> void:
	if _test_sprites.is_empty():
		return
	var spr: Sprite2D = target if is_instance_valid(target) else _test_sprites[randi() % _test_sprites.size()]
	_spawn_damage_number(spr, payload)

## 随机返回一个测试单位（供调用方在同一次跳字组里锁定同一单位、演示多段间距）。
func pick_random_unit() -> Sprite2D:
	if _test_sprites.is_empty():
		return null
	return _test_sprites[randi() % _test_sprites.size()]

## 静态预览滑入形变（形变A·整体横向缩放）：将所有测试单位美术强制拉伸到最大倍数(slide_stretch_max)。
func preview_stretch() -> void:
	if _test_sprites.is_empty():
		return
	for i in _test_sprites.size():
		var spr := _test_sprites[i] as Sprite2D
		if not is_instance_valid(spr):
			continue
		var mat: ShaderMaterial = _test_stretch_materials[i] if i < _test_stretch_materials.size() else null
		if not is_instance_valid(mat):
			mat = _attach_stretch(spr)
			if i < _test_stretch_materials.size():
				_test_stretch_materials[i] = mat
			else:
				_test_stretch_materials.append(mat)
		if mat == null:
			continue
		mat.set_shader_parameter("u_stretch", slide_stretch_max)

## 复位静态预览形变：恢复原比例(u_stretch=1.0)。
func preview_stretch_reset() -> void:
	for i in _test_stretch_materials.size():
		var mat: ShaderMaterial = _test_stretch_materials[i]
		if not is_instance_valid(mat):
			continue
		mat.set_shader_parameter("u_stretch", 1.0)

## 清除当前所有已生成的零件效果（伤害数字 / 静态 buff 提示 / 闪白层），避免反复点击堆叠。
func clear_fx() -> void:
	for n in _fx_nodes:
		if is_instance_valid(n):
			n.queue_free()
	_fx_nodes.clear()
	for p in _static_prompts:
		if is_instance_valid(p):
			p.queue_free()
	_static_prompts.clear()
	for f in _flash_sprites:
		if is_instance_valid(f):
			f.queue_free()
	_flash_sprites.clear()
