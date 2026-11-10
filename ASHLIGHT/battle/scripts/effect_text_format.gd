@tool
class_name EffectTextFormat
extends RefCounted

## 战斗效果文字统一格式（单一真相来源）。
## 意图展开组(intent_root) 与 卡牌效果文字(card_root) 共用本规格，确保排版一致、防止漂移。
##
## 格式规格（2026-10-09 固化）：
##   · 正文(描述)：抖音体(DouyinSansBold.otf)，颜色由调用方背景决定（黑面板=白字 / 卡面=黑字），
##     经 RichTextLabel 的 default_font 主题覆盖承载，本模块不干预。
##   · 关键数值：扁桃体(Biantao.ttf) + 放大字号 + 与正文同色（不上金、不加【】）。
##   · 升降箭头：▲=增益/上升→红(#e22600) / ▼=削减/下降→绿(#5cc600)，取自伤害/治疗数字色库；
##     用抖音体兜底字形（保证 ▲/▼ 可见），字号=关键数值字号。
##   · 关键数值左右留空位由调用方在套用处加空格（意图靠模板、卡牌靠 _format_plain），本函数仅负责字体/字号/箭头着色。

## 字体路径（关键数值 + 箭头/正文，两处共用同一文件，杜绝路径漂移）。
const BIANTAO_PATH: String = "res://assets/fonts/Biantao.ttf"        # 扁桃体（关键数值）
const DOUYIN_PATH: String = "res://assets/fonts/DouyinSansBold.otf"  # 抖音体（正文 + 箭头兜底字形）

## 升降箭头配色（取自伤害数字色库：普通伤害暖红 / 治疗翠绿）。
const ARROW_UP_COLOR: String = "#e22600"    # ▲ 上升 / 增益
const ARROW_DOWN_COLOR: String = "#5cc600"  # ▼ 下降 / 削减

## 关键数值 + 可选升降箭头 → bbcode 字符串。
##   num    = 纯数字串（可含 × 等多段符号，如 "3×2"）；空串调用方应自行兜底。
##   arrow  = 空 / "▲" / "▼"；非空时追加同字号着色箭头。
##   font_size = 关键数值放大字号（意图/卡牌各自传入：意图≈24、卡牌≈30）。
## 数值恒用扁桃体放大；箭头用抖音体同字号、按方向染红/绿。两处调用产出的 bbcode 结构完全一致。
static func key_number(num: String, arrow: String, font_size: int) -> String:
	var num_bb: String = "[font=%s][font_size=%d]%s[/font_size][/font]" % [BIANTAO_PATH, font_size, num]
	if arrow == "":
		return num_bb
	var col: String = ARROW_UP_COLOR if arrow == "▲" else ARROW_DOWN_COLOR
	var arrow_bb: String = "[font=%s][font_size=%d][color=%s]%s[/color][/font_size][/font]" % [DOUYIN_PATH, font_size, col, arrow]
	return num_bb + arrow_bb

## 纯文本数值标记，由卡面格式化器为三角形染色。
static func changed_value(value: int, baseline: int) -> String:
	return str(value) + ("▲" if value > baseline else ("▼" if value < baseline else ""))