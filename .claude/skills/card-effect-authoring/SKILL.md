---
name: card-effect-authoring
description: >
  Ashlight 卡牌/敌人技能 effect 的录入与校验流程。当用户想「给某张卡加一个效果」「按描述改
  effects」「检查数据表 effects 是否合法」「改完表重新导表/跑 gen.bat」，或提到 CardInfo /
  EnemySkillInfo / DataTables / Luban 导表时，都用这个 skill。它保证：只改 Excel 源不改生成的
  json、effect 的单元格编码格式正确、引用的 Buff/卡牌 Id 存在、以及新增 effect 类型时不漏掉战斗
  侧的 Command 映射。涉及 Ashlight 卡牌数值/效果的表操作时优先使用，即使用户没明说「导表」。
---

# Ashlight Card Effect Authoring

真源是 Excel（`DataTables/Datas/`），导表编译出 `Assets/Resources/Config/*.json` 和 `Assets/Gen/*.cs`。**只改 Excel，绝不手改这两处产物**。effect 是多态的：基类 `Effect` + 子类，Excel 单元格首段 = 子类名（json 里是 `$type`）。字段权威 schema 见 [defines.xml](../../../DataTables/Defines/defines.xml)，各类型速查见 [references/effect-catalog.md](references/effect-catalog.md)。

## 工具箱（都从仓库根目录跑，依赖 openpyxl；`$S` = `.claude/skills/card-effect-authoring/scripts`）

| 干什么 | 命令 |
|---|---|
| 看表（Id/名字/描述/Effects，`OFF`=被##注释未导入） | `python $S/dump_tables.py [-t card\|enemy\|buff] [--full]` |
| 改单元格（按 Id 定位+旧值断言，不匹配即中止） | `python $S/edit_cell.py <xlsx> <Id> <列名> <旧值> <新值>` |
| 校验（格式/字段数/引用/占位符 + 战斗映射 WARN） | `python $S/validate_tables.py` |
| 导表 | `cd DataTables && dotnet ../Tools/Luban/Luban.dll -t all -d json -c cs-simple-json --conf ./luban.conf -x outputCodeDir=../Assets/Gen -x outputDataDir=../Assets/Resources/Config` |

别直接跑 `gen.bat`（结尾 `pause` 会挂住）。**别现写 openpyxl 代码**——脚本已固化所有读表坑；确实要写新表操作代码时，先读 [references/excel-gotchas.md](references/excel-gotchas.md)。

## 标准流程

1. **选类型**：把描述拆成一或多个 effect，查 [effect-catalog.md](references/effect-catalog.md)。没有现成类型能表达 → 读 [references/new-effect-type.md](references/new-effect-type.md)（要改代码，先告知用户等确认）。
2. **看样板**：`dump_tables.py` 看邻近行——现有数据永远是最可靠的编码参考。编码规则：多 effect 用 `;` 分隔，effect 内字段用 `,`：`类型名,EffectType缩写,字段...`（缩写 `A`=Attack `D`=Defense `T`=TimeSlot `H`=Heal `N`=Null `B`=Buff `F`=Fill；字段按 defines.xml 该子类 `<var>` 声明顺序；子列表用 `|`）。
3. **写入**：`edit_cell.py`（按 Id，绝不按行号）。目标文件开在 Excel 里会 `PermissionError`，先关。
4. **校验**：`validate_tables.py` 查格式/引用/占位符可解析性，并对**表里用到但战斗侧未映射**的 effect 类型发 WARN（= 哑效果，向用户点明）。它查不了**语义**——「描述写抽牌、effect 却加护甲」这种要人工逐条比对：描述文字 ↔ effect 类型 ↔ 数值 ↔ 占位符。
   占位符规则：`{X}`/`{X:i}` 的 X = 效果的 EffectType 缩写。卡牌解析器认 `A/D/T/H/B/N/F/V`（`{V}`=`{F}` 别名，buff 数值几乎都是 F 编码，**别凭直觉写 `{N}`**）；敌技解析器**只认 `A/D/T/H/B/N`**（敌技 buff 编码是 B，描述用 `{B}`）。
5. **导表 + 验产物**：跑 dotnet 命令，开对应 json 确认目标行 `$type`/字段值符合预期。Luban 报错会指到表/行/列。

## 红线

- 只改 Excel 源；按 Id 定位行；导表前必跑校验；语义一致性人工比对。
- 战斗映射以 `validate_tables.py` 输出为准（它直接解析 `CardPlayResolver.cs` / `EnemySkillToTimelineConverter.cs`），catalog 的映射列仅供速查、可能滞后。
- 新增 effect 类型：按 [new-effect-type.md](references/new-effect-type.md) 走，先征得用户同意，补齐战斗映射并登记 catalog。
