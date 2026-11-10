class_name UnitHp
extends Node2D

## 单位血条组件（Step 1 · 6b）。三层 TextureProgressBar（底部 / 纯白填充 / 彩色填充+边框）+ 居中数值 + 护盾叠层。
## 分层（由下到上）：HpBarBg(底部) → HpBarWhite(纯白填充，位于彩色填充下) → HpBar(彩色填充 + 边框texture_over，最上层)。
## 掉血闪白：伤害时彩色层立即扣减、白色层暂留旧值露出[新值,旧值]白段，随后白色层缩回到新值对齐、露出底槽，目视本次掉量。
## 居中铁律：本组件内部所有零件以父节点(UnitRoot)原点为基准，整体 x 居中于 0。护盾为叠层，出现/消失不推血条。

const HP_FILL_PATH: String = "res://assets/Unit UI/unit_hud/hp_bar_fill.png"
const HP_BG_PATH: String = "res://assets/Unit UI/unit_hud/hp_bar_bg.png"
const HP_FRAME_PATH: String = "res://assets/Unit UI/unit_hud/hp_bar_frame.png"
const HP_FILL_WHITE_PATH: String = "res://assets/Unit UI/unit_hud/hp_bar_fill_white.png"
const HP_SHIELD_PATH: String = "res://assets/Unit UI/unit_hud/hp_shield.png"
const FONT_PATH: String = "res://assets/fonts/DouyinSansBold.otf"

signal hp_changed(new_current: float, new_max: float)
signal shield_changed(new_value: float)

## 护盾图标相对血条右端的偏移（叠层，不推血条）
@export var shield_icon_offset_x: float = 6.0 :
	set(value):
		shield_icon_offset_x = value
		_refresh_shield_position()

## HP 数值与护盾数值的字体大小（原始基础 16，可按观感微调）
@export var hp_font_size: int = 20 :
	set(value):
		hp_font_size = value
		_refresh_label_font()

## HP 数值与护盾数值的描边粗细（原始基础 2，可按观感微调）
@export var hp_outline_size: int = 6 :
	set(value):
		hp_outline_size = value
		_refresh_label_font()

## 九宫格固定边缘像素，顺序为左、上、右、下。
@export var bar_stretch_margins: Vector4i = Vector4i(20, 14, 20, 14)

var _bar_bg: TextureProgressBar      # 底部（HP底部）：始终满铺，作为损失后露出的暗轨
var _bar_white: TextureProgressBar   # 纯白填充：位于 HP 彩色填充之下，掉血闪白残影层
var _bar: TextureProgressBar         # 彩色填充 + 边框（texture_over 边框置于最上层）
var _hp_text: Label
var _shield: Node2D
var _shield_icon: TextureRect
var _shield_text: Label
var _bar_size: Vector2 = Vector2.ZERO
## 掉血闪白白层缩回时长(s)：伤害结算后纯白填充从「旧血量」缓缓缩到「新血量」、露出底槽，使玩家看清本次掉量。
var hp_trail_time: float = 0.45
var _white_tween: Tween = null

var _max_hp: float = 100.0
var _cur_hp: float = 100.0
var _shield_val: float = 0.0

func _ready() -> void:
	_build()

func _build() -> void:
	var fill_tex := load(HP_FILL_PATH) as Texture2D
	var bg_tex := load(HP_BG_PATH) as Texture2D
	var frame_tex: Texture2D = null
	if ResourceLoader.exists(HP_FRAME_PATH):
		frame_tex = load(HP_FRAME_PATH) as Texture2D
	var white_tex: Texture2D = null
	if ResourceLoader.exists(HP_FILL_WHITE_PATH):
		white_tex = load(HP_FILL_WHITE_PATH) as Texture2D
	else:
		push_warning("UnitHp: 纯白填充缺失 %s（掉血闪白残影将不可用）" % HP_FILL_WHITE_PATH)
	var shield_tex := load(HP_SHIELD_PATH) as Texture2D

	if fill_tex == null or bg_tex == null:
		push_warning("UnitHp: 血条美术缺失 %s / %s" % [HP_FILL_PATH, HP_BG_PATH])
		return

	_bar_size = fill_tex.get_size()

	# 底层：HP底部（始终满铺，作为损失后露出的暗轨）
	_bar_bg = TextureProgressBar.new()
	_bar_bg.name = "HpBarBg"
	_bar_bg.fill_mode = TextureProgressBar.FILL_LEFT_TO_RIGHT
	_bar_bg.texture_progress = bg_tex
	_bar_bg.min_value = 0.0
	_bar_bg.max_value = _max_hp
	_bar_bg.value = _max_hp
	_bar_bg.size = _bar_size
	_bar_bg.position = Vector2(-_bar_size.x * 0.5, 0.0)
	add_child(_bar_bg)

	# 中层：纯白填充（位于 HP 彩色填充之下）。掉血时彩色层立即扣减、白层暂留旧值，随后白层缩回对齐 → 露出底槽、目视本次掉量。
	if white_tex != null:
		_bar_white = TextureProgressBar.new()
		_bar_white.name = "HpBarWhite"
		_bar_white.fill_mode = TextureProgressBar.FILL_LEFT_TO_RIGHT
		_bar_white.texture_progress = white_tex
		_bar_white.min_value = 0.0
		_bar_white.max_value = _max_hp
		_bar_white.value = _cur_hp
		_bar_white.size = _bar_size
		_bar_white.position = Vector2(-_bar_size.x * 0.5, 0.0)
		add_child(_bar_white)

	# 顶层：彩色填充 + 边框（texture_over 边框置于最上层）
	_bar = TextureProgressBar.new()
	_bar.name = "HpBar"
	_bar.fill_mode = TextureProgressBar.FILL_LEFT_TO_RIGHT
	_bar.texture_progress = fill_tex
	if frame_tex != null:
		_bar.texture_over = frame_tex
	_bar.min_value = 0.0
	_bar.max_value = _max_hp
	_bar.value = _cur_hp
	_bar.size = _bar_size
	_bar.position = Vector2(-_bar_size.x * 0.5, 0.0)
	add_child(_bar)

	_hp_text = Label.new()
	_hp_text.name = "HpText"
	_hp_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
	_hp_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
	_hp_text.text = _hp_label()
	var font := load(FONT_PATH) as Font
	if font != null:
		var ls := LabelSettings.new()
		ls.font = font
		ls.font_size = hp_font_size
		ls.outline_size = hp_outline_size
		ls.outline_color = Color.BLACK
		_hp_text.label_settings = ls
	_hp_text.size = _bar_size
	# 仅本次：HP 文字整体下移 2px（独立调整，不与护盾文字捆绑）
	_hp_text.position = Vector2(-_bar_size.x * 0.5, 2.0)
	add_child(_hp_text)

	# 护盾叠层（靠右，不占血条布局；护盾=0 时隐藏）
	_shield = Node2D.new()
	_shield.name = "ShieldOverlay"
	_shield.visible = _shield_val > 0.0
	add_child(_shield)

	if shield_tex != null:
		var icon_size := shield_tex.get_size()
		_shield_icon = TextureRect.new()
		_shield_icon.name = "ShieldIcon"
		_shield_icon.texture = shield_tex
		_shield_icon.size = icon_size
		_shield_icon.position = Vector2(0.0, -icon_size.y * 0.5)
		_shield.add_child(_shield_icon)
		_shield_text = Label.new()
		_shield_text.name = "ShieldText"
		_shield_text.horizontal_alignment = HORIZONTAL_ALIGNMENT_CENTER
		_shield_text.vertical_alignment = VERTICAL_ALIGNMENT_CENTER
		_shield_text.text = str(int(_shield_val))
		if font != null:
			var sls := LabelSettings.new()
			sls.font = font
			sls.font_size = hp_font_size
			sls.outline_size = hp_outline_size
			sls.outline_color = Color.BLACK
			_shield_text.label_settings = sls
		_shield_text.size = Vector2(60.0, icon_size.y)
		# 仅本次对齐：护盾文字上移半个自身框高，使其字形中心落在 bar 垂直中心（与 HP 文字同 Y）。
		# 注意——这是一次性位置对齐，不引入"护盾/HP 共用高度"参数，二者高度后续可独立调整。
		# 仅本次：护盾文字相对图标额外左移 30px（只动文字、不动图标；一次性调整，不进导出参数）
		# 仅本次：护盾文字整体下移 2px（独立调整，不与 HP 文字捆绑）
		_shield_text.position = Vector2(icon_size.x + 2.0 - 45.0, -icon_size.y * 0.5 + 2.0)
		_shield.add_child(_shield_text)
	else:
		push_warning("UnitHp: 护盾图标缺失 %s" % HP_SHIELD_PATH)

	set_width_multiplier(1.0)

## 只加宽血条图形与文字布局框，字体和护盾叠层保持原始比例。
func set_width_multiplier(multiplier: float) -> void:
	if not is_instance_valid(_bar) or _bar.texture_progress == null:
		return
	var factor := maxf(multiplier, 0.1)
	_bar_size.x = _bar.texture_progress.get_width() * factor
	for bar: TextureProgressBar in [_bar_bg, _bar_white, _bar]:
		if is_instance_valid(bar):
			bar.scale = Vector2.ONE
			bar.nine_patch_stretch = true
			bar.stretch_margin_left = bar_stretch_margins.x
			bar.stretch_margin_top = bar_stretch_margins.y
			bar.stretch_margin_right = bar_stretch_margins.z
			bar.stretch_margin_bottom = bar_stretch_margins.w
			bar.size = _bar_size
			bar.position.x = -_bar_size.x * 0.5
	if is_instance_valid(_hp_text):
		_hp_text.size.x = _bar_size.x
		_hp_text.position.x = -_bar_size.x * 0.5
	_refresh_shield_position()

func setup_stats(max_v: float, cur_v: float, shield_v: float) -> void:
	_max_hp = maxf(max_v, 1.0)
	_cur_hp = clampf(cur_v, 0.0, _max_hp)
	_shield_val = maxf(shield_v, 0.0)
	if is_instance_valid(_bar_bg):
		_bar_bg.max_value = _max_hp
		_bar_bg.value = _max_hp
	if is_instance_valid(_bar_white):
		_bar_white.max_value = _max_hp
		_bar_white.value = _cur_hp
	if is_instance_valid(_bar):
		_bar.max_value = _max_hp
		_bar.value = _cur_hp
	if is_instance_valid(_hp_text):
		_hp_text.text = _hp_label()
	if is_instance_valid(_shield):
		_shield.visible = _shield_val > 0.0
	if is_instance_valid(_shield_text):
		_shield_text.text = str(int(_shield_val))
	hp_changed.emit(_cur_hp, _max_hp)
	shield_changed.emit(_shield_val)

func set_hp(v: float) -> void:
	_cur_hp = clampf(v, 0.0, _max_hp)
	if is_instance_valid(_bar_white):
		_bar_white.value = _cur_hp
	if is_instance_valid(_bar):
		_bar.value = _cur_hp
	if is_instance_valid(_hp_text):
		_hp_text.text = _hp_label()
	hp_changed.emit(_cur_hp, _max_hp)

func set_max_hp(v: float) -> void:
	_max_hp = maxf(v, 1.0)
	if is_instance_valid(_bar_bg):
		_bar_bg.max_value = _max_hp
		_bar_bg.value = _max_hp
	if is_instance_valid(_bar_white):
		_bar_white.max_value = _max_hp
		_bar_white.value = _cur_hp
	if is_instance_valid(_bar):
		_bar.max_value = _max_hp
		_bar.value = _cur_hp
	if is_instance_valid(_hp_text):
		_hp_text.text = _hp_label()
	hp_changed.emit(_cur_hp, _max_hp)

func set_shield(v: float) -> void:
	_shield_val = maxf(v, 0.0)
	if is_instance_valid(_shield):
		_shield.visible = _shield_val > 0.0
	if is_instance_valid(_shield_text):
		_shield_text.text = str(int(_shield_val))
	shield_changed.emit(_shield_val)

## 受到伤害：【护盾优先吸收】—— 先以护盾抵消伤害（护盾不足才扣血），杜绝"伤害直接打到血条、护盾形同虚设"。
## 返回本次【实际扣除的血量】(float)，供上层（中毒/出血飘字）显示真实掉血；护盾吸收的部分不计入。
## 掉血闪白残影：仅「真实扣血」那部分产生（护盾吸收时不闪白）；护盾被扣减时实时刷新护盾叠层。
func apply_damage(amount: float) -> float:
	var remainder: float = maxf(amount, 0.0)
	# 护盾优先吸收：扣减护盾，剩余再扣血（护盾按 1:1 抵消，不为负）。
	if _shield_val > 0.0:
		var absorbed: float = mini(remainder, _shield_val)
		_shield_val = maxf(_shield_val - absorbed, 0.0)
		remainder -= absorbed
		if is_instance_valid(_shield):
			_shield.visible = _shield_val > 0.0
		if is_instance_valid(_shield_text):
			_shield_text.text = str(int(_shield_val))
		shield_changed.emit(_shield_val)
	# 剩余伤害扣血
	var old_hp: float = _cur_hp
	_cur_hp = clampf(_cur_hp - remainder, 0.0, _max_hp)
	var new_hp: float = _cur_hp
	if is_instance_valid(_bar):
		_bar.value = new_hp                       # 彩色填充立即扣减
	if is_instance_valid(_bar_white):
		_bar_white.value = old_hp                 # 白层暂留旧值，露出 [new, old] 白段
	if is_instance_valid(_hp_text):
		_hp_text.text = _hp_label()
	hp_changed.emit(new_hp, _max_hp)
	var hp_lost: float = maxf(old_hp - new_hp, 0.0)
	if hp_lost > 0.0 and is_instance_valid(_bar_white):
		_trail_white_to(new_hp)
	return hp_lost

## 纯白填充层从当前值缩回到 target（= 伤害后新血量），露出底槽，形成掉血闪白残影。多次掉血重启动画（先杀旧 tween）。
func _trail_white_to(target: float) -> void:
	if is_instance_valid(_white_tween):
		_white_tween.kill()
	_white_tween = create_tween()
	_white_tween.tween_property(_bar_white, "value", target, hp_trail_time).set_ease(Tween.EASE_IN)

## 获得护盾：在现有护盾值上叠加 amount（不为负）。
func apply_shield(amount: float) -> void:
	_shield_val = maxf(_shield_val + amount, 0.0)
	if is_instance_valid(_shield):
		_shield.visible = _shield_val > 0.0
	if is_instance_valid(_shield_text):
		_shield_text.text = str(int(_shield_val))
	shield_changed.emit(_shield_val)

## 当前护盾值（供 UnitRoot 包装层读取，避免暴露私有字段）。
func get_shield() -> float:
	return _shield_val

## 消耗护盾：在现有护盾值上扣减 amount（不为负）。供【中毒】护盾 1:1 抵消层数结算使用。
func reduce_shield(amount: float) -> void:
	_shield_val = maxf(_shield_val - amount, 0.0)
	if is_instance_valid(_shield):
		_shield.visible = _shield_val > 0.0
	if is_instance_valid(_shield_text):
		_shield_text.text = str(int(_shield_val))
	shield_changed.emit(_shield_val)

## 回复血量：在现有血量上叠加 amount（不超 max_hp）。供「回复血量」卡牌款式(原型 heal)使用。
func apply_heal(amount: float) -> void:
	_cur_hp = clampf(_cur_hp + amount, 0.0, _max_hp)
	if is_instance_valid(_bar_white):
		_bar_white.value = _cur_hp
	if is_instance_valid(_bar):
		_bar.value = _cur_hp
	if is_instance_valid(_hp_text):
		_hp_text.text = _hp_label()
	hp_changed.emit(_cur_hp, _max_hp)

## 当前血量（供 UnitRoot 包装层读取，避免暴露私有字段）。
func get_current_hp() -> float:
	return _cur_hp

func _hp_label() -> String:
	return "%d/%d" % [int(_cur_hp), int(_max_hp)]

func _refresh_shield_position() -> void:
	if not is_instance_valid(_shield):
		return
	var right_edge := _bar_size.x * 0.5
	# 护盾整体(图标+文字)相对原位置左移 30px（一次性视觉调整，不进导出参数；图标文字作为整体一起移）
	_shield.position = Vector2(right_edge + shield_icon_offset_x - 30.0, _bar_size.y * 0.5)

func _refresh_label_font() -> void:
	for lbl: Label in [_hp_text, _shield_text]:
		if not is_instance_valid(lbl) or lbl.label_settings == null:
			continue
		lbl.label_settings.font_size = hp_font_size
		lbl.label_settings.outline_size = hp_outline_size
