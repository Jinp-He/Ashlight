# ASHLIGHT · Godot 战斗原型

使用 Godot 4.7.2 打开 `project.godot`，运行 `battle/Battle.tscn`。

画面按 1600×900 设计分辨率制作，窗口任意缩放时整体等比缩放（`canvas_items` + `keep`），非 16:9 窗口两侧或上下留黑边。

当前规则与临时约定以 [战斗设计](docs/战斗设计.md) 为准。卡牌和怪物意图仍是测试占位内容。

## 操作

- 左键选一张可出的牌（与牌堆顶同色，或点数相同），再点击合法目标打出。一次只打一张。
- 选中后点另一张牌 = 改选；右键取消。
- 点一张接不上的牌 → 再点技能就绪的友方：用该友方的技能改写这张牌（周周换色 / 艾琳点数 -1 / 火炮点数 +1）。不算动作，之后进入冷却。
- 右下角弃牌堆按钮 = 摸牌：抽 1 张，敌人倒计时 -1。手牌满 10 张时改为弃手重抽。
- 屏幕上方居中为牌堆顶（颜色 + 数字）；左上为 COMBO ×N（下一张牌效果触发 N 次，摸牌清空）、待命的反制与动作次数；敌人头顶为倒计时，身下为下一张牌和目标。
- HUD 摆放在 `battle/Battle.tscn` 的 `HUD` 节点下（`PileTop`、`InfoPanel`），可直接在编辑器里拖动调整。

## 代码入口

- `battle/scripts/v2/chain_rules.gd`：纯规则状态：接牌判定、Combo、牌堆顶、敌人倒计时、待命的反制。
- `battle/scripts/battle_root.gd`：出牌 / 摸牌 / 补牌流程、敌人当场出手与反制牌结算、目标、胜负与 HUD。
- `battle/scripts/cards/hand_manager.gd`：公共手牌、可出判定显示、卡面刷新、卡牌交互及动画。
- `battle/scripts/cards/deck_manager.gd`：公共抽牌堆与弃牌堆。
- `data/cards/<角色>/*.tres`：卡牌数据（CardData 资源，24 张占位卡）。新增或改卡直接在编辑器里改资源，`CardDatabase` 自动读取整个文件夹。
- `battle/scripts/monster_database.gd`：占位敌人的倒计时与出牌循环。
- `battle/scripts/v2/ally_skills.gd`：友方技能的名字、效果与冷却。
- `battle/scripts/cutscene/`：原有战斗演出组件。
- `battle/scripts/_archive/`：旧 EP / 费用倒计时代码存档（Godot 忽略）。

## 验证

```sh
Godot --headless --path . res://tests/battle_v2_test.tscn
Godot --headless --path . res://tests/battle_v2_animation_test.tscn
```

分别验证规则与实际动画衔接。退出时仍有原型资源引用清理警告，尚未专项处理。

Windows 导出：使用项目中的 `Windows Desktop` 预设。导出的 `.exe` 与 `.pck` 须放在同一目录。
