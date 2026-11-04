# ASHLIGHT · Godot 战斗原型

使用 Godot 4.7.2 打开 `project.godot`，运行 `battle/Battle.tscn`。

画面按 1600×900 设计分辨率制作，窗口任意缩放时整体等比缩放（`canvas_items` + `keep`），非 16:9 窗口两侧或上下留黑边。

当前规则与临时约定以 [战斗设计](docs/战斗设计.md) 为准。卡牌和怪物意图仍是测试占位内容。

## 操作

- 左键选一张可出的牌（与牌堆顶同色，或点数相同），再点击合法目标打出。一次只打一张。
- 选中后点另一张牌 = 改选；右键取消。
- 点一张接不上的牌 → 再点技能就绪的友方：用该友方的技能改写这张牌（周周换色 / 艾琳点数 -1 / 火炮点数 +1）。不算动作，之后进入冷却。
- 右下角弃牌堆按钮 = 摸牌：抽 1 张，敌人倒计时 -1。手牌允许超过 10 张，不触发弃手重抽，也不清护盾。
- 敌方每次攻击造成实际掉血时，Combo −1，最低 ×1；护盾完全吸收不扣，部分吸收但仍掉血则扣 1。
- 屏幕上方居中为牌堆顶（颜色 + 数字）；左上为 COMBO ×N（下一张牌效果触发 N 次，摸牌清空）、待命的反制与动作次数；敌人头顶为倒计时，身下为下一张牌和目标。
- HUD 摆放在 `battle/Battle.tscn` 的 `HUD` 节点下（`PileTop`、`InfoPanel`），可直接在编辑器里拖动调整。

## 代码入口

- `battle/scripts/v2/chain_rules.gd`：纯规则状态：接牌判定、Combo、牌堆顶、敌人倒计时、待命的反制。
- `battle/scripts/battle_root.gd`：出牌 / 摸牌 / 补牌流程、敌人当场出手与反制牌结算、目标、胜负与 HUD。
- `battle/scripts/cards/hand_manager.gd`：公共手牌、可出判定显示、卡面刷新、卡牌交互及动画。
- `battle/scripts/cards/deck_manager.gd`：公共抽牌堆与弃牌堆。
- `data/cards/<角色>/*.tres`：完整卡牌库（CardData 资源），新建或改卡直接在编辑器里操作。
- `data/starting_decks/*.tres`：角色初始卡组（StartingDeckData 资源），显式引用开局要加入公共牌库的卡牌。
- `battle/scripts/monster_database.gd`：按场景单位 ID 加载、校验怪物资源。
- `data/monsters/*.tres`：怪物生命、基础倒计时和有序意图循环。
- `data/enemy_intents/*.tres`：可共享的敌方意图配置（颜色、点数、伤害/护盾、额外恢复时间）。
- `battle/scripts/v2/ally_skills.gd`：友方技能的名字、效果与冷却。
- `battle/scripts/cutscene2/`：当前战斗原位演出；组合卡牌逐项播放，每项只播放一次并带入 Combo 倍率。
- `battle/scripts/cutscene/`：保留的旧演出和预览组件。
- `battle/scripts/_archive/`：旧 EP / 费用倒计时代码存档（Godot 忽略）。

## 怪物与意图

在 Inspector 编辑 `data/monsters/remy.tres`、`wangg.tres`、`grey.tres`、`mafe.tres`：`max_hp` 决定初始生命，`countdown` 决定基础倒计时，`intents` 是按顺序循环的意图清单。场景敌人的 `unit_id` 对应资源 `id`，文件名为 ID 的 snake_case。

意图本身保存在 `data/enemy_intents/`，可直接拖入怪物的 `intents` 数组。`intent_name / color / number` 控制显示和牌堆顶，`stat / value` 控制伤害或护盾，`delay` 控制这次出手后额外的恢复格数。伤害预告时随机锁定存活对手，护盾作用于自身；被反制也会推进循环及应用 delay。

Grey 与 Mafe 共享重锤、粉碎、装甲三份意图；修改共享资源会同时生效。只改某种怪物时，复制意图 `.tres` 后替换其引用。怪物静态配置不保存当前生命、剩余倒计时或锁定目标，运行时不会写回资源。详细字段与数值见战斗设计文档第 4 节。

## 角色初始卡组

在 Inspector 编辑 `data/starting_decks/zhouzhou.tres`、`irene.tres`、`rocket.tres`：

- `role_id`：对应场景单位 ID（Zhouzhou / Irene / Rocket）。
- `deck_name`：卡组名称，便于识别。
- `cards`：初始卡牌清单，拖入 CardData `.tres`。每个元素是一张实体牌，同一资源列两次就带两张。删除元素即不再携带这张牌。

开局按场景中实际出场的角色加载同名小写卡组文件，合并成公共牌库后洗牌。当前每角色 5 张，共 15 张；翻 1 张作为牌堆顶，再抽 5 张起手，流程保持不变。初始牌库张数随清单变化，不在代码中固定。

**新建卡牌只会加入卡牌库，不会自动进入初始卡组**，还需要将它拖入对应 `cards` 数组。初始卡组中的卡必须属于相同角色，空槽、无效效果和角色不匹配会报告配置错误。运行时洗牌、抽牌、弃牌不修改配置资源。

自定义战斗也可直接调用 `DeckManager.init_from_starting_decks()`，传入一组 StartingDeckData 资源。初始卡组表示整副牌库的组成，不保证某张牌一定进入随机起手。

## 编辑卡牌与预览

选中 `data/cards/<角色>/*.tres`，Inspector 顶部显示完整卡面。`Card Inspector` 插件已默认启用；若旧编辑器窗口未加载，在「项目设置 → 插件」启用它。预览点数默认取 `base_number`，可手动切换为 1–9；不会掷点或修改卡牌数据。名字、插画、颜色、嵌套效果等修改会实时刷新。

卡牌基础信息留在 `CardData`，玩法配置在 `effects: Array[CardEffect]`。在 Inspector 的效果数组中新建 `CardEffect`，上下拖动决定执行顺序：

| 字段 | 用途 |
|---|---|
| `kind` | damage / shield / heal / draw / apply_status / counter / haste / slow |
| `target` | 选中敌人 / 选中友方 / 选中单位 / 自身 / 全体敌人 / 全体友方 / 全场 |
| `amount`、`scales_with_number` | 点数 × amount，或固定 amount |
| `hits` | 重复次数；多段伤害逐段结算 |
| `status_id` | apply_status 的状态：morale / bleed / poison |
| `counter_color` | counter 的反制颜色，-1 表示任意 |
| `exclude_self` | 从效果目标中排除施法者 |

一次出牌选中一个单位，所有手选效果共用该目标；自身、群体、抽牌、反制独立结算。不支持同时要求选中敌人和友方，配置校验会提示错误。`haste / slow` 只改变敌人的倒计时；当前没有位移规则，因此没有开放 move。抽牌作用于公共牌库，反制登记给施法者，二者忽略 target。

Combo ×N 重复整个列表 N 次；每项效果的演出仅播放一次。已倒下的目标只跳过对应效果，后续自身/群体效果继续；战斗结束或施法者倒下则停止。沿用原规则，出牌时的士气加成取快照，在本牌的所有直接伤害及 Combo 重复中使用，首次直接伤害结算时消耗；本牌新获得的士气供后续出牌使用。

卡面效果文案和关键词均从原子效果生成，词条解释仍集中于 `keyword_glossary.gd`。`description` 只填额外说明；留空时显示各效果的数值公式。原有费用字段、单 effect_id、手填效果文案/keywords 已移除。24 张现有卡已迁移，保留各自数值、插画引用以及本地更改的「闪避」名称。

组合示例在 `tests/fixtures/composite_card.tres`：伤害敌人 → 自身护盾 → 敌人中毒 → 抽牌。它不加入正式牌库，可直接选中查看预览，或复制到 `data/cards/` 后改 ID 和角色，再加入对应的初始卡组清单使用。

## 验证

```sh
Godot --headless --path . res://tests/battle_v2_test.tscn
Godot --headless --path . res://tests/battle_v2_animation_test.tscn
Godot --headless --path . res://tests/card_effect_test.tscn
```

分别验证战斗规则、实际动画衔接、原子效果顺序与预览绑定。

可选视觉截图（需图形环境）：`Godot --path . --rendering-method gl_compatibility --resolution 400x620 res://tests/card_preview_visual.tscn -- <输出PNG绝对路径>`。退出时仍有原型资源引用清理警告，尚未专项处理。

Windows 导出：使用项目中的 `Windows Desktop` 预设。导出的 `.exe` 与 `.pck` 须放在同一目录。
