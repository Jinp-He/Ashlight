# Codex 协作指南

本文件是给 Codex 在 Ashlight 项目中工作的入口说明。开始修改前先读本文件，再按任务需要阅读相关模块文档与代码。

## 项目定位

Ashlight 是一个以 Unity 为核心的独立游戏项目。当前优先级是核心系统搭建与可玩闭环，而不是内容量、特效 polish 或过度优化。

核心方向：

- 架构优先于功能堆砌。
- 玩法与数值尽量数据驱动。
- 保持配置、运行时状态、系统规则、UI 表现的职责边界清晰。
- 支持后续叠加 Roguelite、卡牌、数值成长等玩法，不把逻辑一次性写死。

## 工作原则

1. 先理解归属系统，再写代码。
2. 不把状态逻辑、存档逻辑、UI 展示、配置读取混在同一个类里。
3. 可调数值应来自 Luban / Config，不硬编码到业务逻辑中。
4. 配置数据只读且不可变；运行时状态可存档、可恢复。
5. 系统之间优先通过接口、事件或 Manager 协作。
6. 避免滥用 `GetComponent` / `FindObjectOfType` 形成隐式耦合。
7. UI 只能读状态或发送命令，不直接修改业务规则或核心数值。
8. 修改范围保持小而准，避免顺手重构无关代码。

## 推荐分层

- 数据层（Data / Config）：读取 Luban 生成数据，提供只读访问。
- 状态层（Runtime State / Save）：记录真实游戏状态，支持 Save / Load，优先用纯 C# 数据结构。
- 系统层（Manager / System）：驱动规则、更新状态、处理系统级逻辑；一个 Manager 只负责一类规则。
- 表现层（View / UI）：展示状态、转发玩家输入、播放动画或特效。

## 代码风格

- 命名清晰，少用缩写。
- 方法短小，一个方法只做一件事。
- 明确区分 `Init` / `Tick` / `Update`，以及 `Command` / `Query`。
- 优先沿用已有目录、命名、事件和 Manager 模式。
- 不随意改生成代码；若生成代码与数据表不一致，先确认生成链路。

## Luban 数据表读取顺序

读取或修改数据表时，必须先建立类型上下文，避免只看业务表误判。

1. `DataTables/Datas/__enums__.xlsx`：枚举定义，例如 `CharacterEnum`、`TargetTypeEnum`、`RarityEnum`、`EnemyIntentionEnum`、`CardTypeEnum`。
2. `DataTables/Datas/__beans__.xlsx`：Bean / 多态结构，例如 `Effect` 派生、`Strategy`、`EnemyIntention`。
3. `DataTables/Datas/__tables__.xlsx`：表声明，包括 `full_name`、`value_type`、`input`、`index`、`mode`、`output`。
4. `DataTables/Datas/**/#*.xlsx`：具体业务数据。

常用映射以 `Assets/Gen/Tables.cs` 与 `Assets/Resources/Config/*.json` 为准：

- `Character.TbCardInfo` -> `Assets/Resources/Config/character_tbcardinfo.json`
- `Character.TbCharaterInfo` -> `Assets/Resources/Config/character_tbcharaterinfo.json`
- `Enemy.TbEnemyInfo` -> `Assets/Resources/Config/enemy_tbenemyinfo.json`
- `Enemy.TbEnemySkillInfo` -> `Assets/Resources/Config/enemy_tbenemyskillinfo.json`
- `Enemy.TbEncounter` -> `Assets/Resources/Config/enemy_tbencounter.json`
- `TbCustomColor` -> `Assets/Resources/Config/tbcustomcolor.json`
- `TbNounDictionary` -> `Assets/Resources/Config/tbnoundictionary.json`

运行时加载链路：

1. `Assets/Scripts/Config/ConfigLoader.cs` 加载 `Resources/Config/*.json`。
2. `cfg.Tables` 读取固定文件名。
3. `Tables.ResolveRef()` 解析跨表引用。
4. 游戏系统消费解析后的只读配置，不直接依赖 Excel 原始文本。

## 战斗系统要点

主推战斗方案见 `Doc/BattleSystem.md`。

- 战斗由 ATB 行动条驱动。
- 玩家侧是类杀戮尖塔的能量 + 多张手牌机制。
- 敌人侧使用意图轴 / 执行轴提供可见压力窗口。
- 玩家角色到达行动点时全场暂停，直到玩家结束回合。
- 能量来自角色配置，核心规则为“能量 = Speed”。
- 卡牌分为即时牌 `Normal` 与执行牌 `Execution`。

实现相关改动应优先检查 `Assets/Scripts/Battle` 下已有结构与 README。

## 卡牌数值平衡

Claude 侧已有 `/balance-cards` 命令说明，Codex 遇到同类任务时按以下规则执行。

数据源：

- `Assets/Resources/Config/character_tbcardinfo.json`

分析项：

- 总格数：`Channeling + Duration + Recoil`
- 当前伤害值
- 推荐伤害值
- 差异百分比

基础公式：

```text
基础伤害 = 格数 * 5
格数 = Channeling + Duration + Recoil
```

调整参考：

- AOE：`* 0.8`
- 迅捷：`-3`
- 控制：`-2` 到 `-3`，视效果强度而定
- 条件伤害：基础约 `* 0.8`，触发收益约 `* 1.5` 到 `* 2`
- 护甲：`1` 护甲约等于 `0.8` 伤害
- 穿甲：约 `-5` 伤害

若用户要求应用修改，先生成清晰报告，再修改 JSON 或源数据表；修改后说明是否需要重新跑 Luban 生成。

## 文件与环境注意事项

- 项目根目录：`F:\Ashlight`
- 这是 Unity 项目，避免无意义改动 `Library/`、`Temp/`、缓存或第三方包。
- `.claude/worktrees/` 是 Claude 工作树副本，默认不要在其中修改主项目逻辑。
- 若终端显示中文乱码，先判断是否为编码显示问题，不要直接改业务数据。
- 读取中文 Markdown 时使用 UTF-8。
- 修改前检查工作区已有改动，不覆盖用户未提交内容。

## 开始任务前的最小检查

1. 看清任务属于哪个层级或模块。
2. 读取相关 README / Doc / 现有代码。
3. 若涉及数据表，按 Luban 顺序建立类型上下文。
4. 若涉及 UI，确认 UI 只发命令不改业务状态。
5. 完成后做最小可行验证，并明确说明验证结果。
