class_name ComboGlossary
extends Node2D

## 卡牌快照右侧「词条解释」面板（2026-10-07 新增）。
## 视觉上完全复用 UnitBuffTooltip 的「名字（金 #9c660a）+ 说明（白）+ 黑圆角底框」组合，
## 即玩家在战斗单位上悬停 buff 时看到的那个描述框——只是这里不靠悬停、而是常显在快照右侧。
## 关键词来源：CardData 的原子效果和状态 ID，去重保序。
## 仅在首张锚点卡进入托盘（= 生成快照）时构建一次；取消/打出时随 _combo_unpackage 整体销毁。

const UnitBuffTooltipScript: Script = preload("res://battle/scripts/units/unit_buff_tooltip.gd")

@export_group("Layout")
@export var panel_width: float = 200.0     # 单条词条底框宽（与 UnitBuffTooltip 默认一致）
@export var side_gap: float = 20.0         # 距快照右缘的水平间距
@export var top_gap: float = 0.0           # 距快照顶部的垂直偏移（0 = 与快照顶对齐）
@export var entry_gap: float = 12.0        # 相邻词条之间的垂直间距

var _entries: Array[Control] = []

## 根据卡牌原子效果构建词条面板。
## snap_w / snap_h 为快照显示层尺寸（COMBO_CARD_W/H * combo_snap_scale），用于把面板锚到快照旁；
## place_left=true 时改放快照左侧（右缘放不下时避免溢出/遮挡），默认放右侧。
func build_from_data(data: CardData, snap_w: float, snap_h: float, place_left: bool = false) -> void:
	_clear()
	var keywords := data.keyword_names()
	if keywords.is_empty():
		return
	# 快照在托盘内居中于托盘原点：右缘 = +snap_w/2，左缘 = -snap_w/2，顶部 = -snap_h/2（托盘原点即快照中心）
	var start_x: float
	if place_left:
		start_x = -(snap_w * 0.5 + side_gap + panel_width)   # 整块面板贴在快照左侧空白区
	else:
		start_x = snap_w * 0.5 + side_gap                      # 整块面板贴在快照右侧
	var y := -snap_h * 0.5 + top_gap
	for kw in keywords:
		var desc: String = KeywordGlossary.ENTRIES.get(kw, "")
		# 先用同公式估算高度，让堆叠位置确定（UnitBuffTooltip 内部 _relayout_to_fit 会算出相同高度，无错位）
		var lines := _estimate_lines(desc, panel_width - 32.0)
		var h := 80.0 + maxi(0, lines - 1) * 30.0
		var tip := UnitBuffTooltipScript.new() as UnitBuffTooltip
		tip.name = "Glossary_" + kw
		tip.box_size = Vector2(panel_width, h)
		tip.z_index = 5   # 高于托盘内快照显示层(z=2)，保证可读
		add_child(tip)
		# 常显（instant=true 跳过淡入）；左上角锚到本容器局部坐标 (start_x, y)
		tip.show_tooltip(kw, desc, Vector2(start_x, y), true)
		_entries.append(tip)
		y += h + entry_gap

## 销毁全部词条（取消/打出时调用）。
func _clear() -> void:
	for e in _entries:
		if is_instance_valid(e):
			e.queue_free()
	_entries.clear()

## 估算说明文字换行行数（与 UnitBuffTooltip._estimate_desc_lines 一致：CJK 字形≈1em 宽）。
func _estimate_lines(p_text: String, p_inner_w: float) -> int:
	var n := p_text.length()
	if n == 0:
		return 1
	var char_w := 16.0   # = UnitBuffTooltip.desc_font_size
	var per_line := maxi(1, int(floor(p_inner_w / char_w)))
	return maxi(1, ceili(float(n) / float(per_line)))
