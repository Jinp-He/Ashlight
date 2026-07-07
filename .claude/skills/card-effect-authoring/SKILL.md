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

给 Ashlight 的卡牌（`CardInfo`）和敌人技能（`EnemySkillInfo`）录入或修改 effect，并在导表前做合法性检查。核心目标：**把一句自然语言描述，翻译成一条合法的 effect 单元格编码，写进 Excel 源表，做完全部校验，再重新生成表代码。**

## 这条流水线为什么是这样的

数据表走 Luban：**真源是 Excel（`DataTables/Datas/`）**，`gen.bat` 把它编译成 `Assets/Resources/Config/*.json`（运行时数据）和 `Assets/Gen/*.cs`（强类型读取代码）。所以：

- **只改 Excel，绝不手改 `Assets/Resources/Config/*.json` 或 `Assets/Gen/*.cs`** —— 那些是产物，下次 gen 会被覆盖，手改等于白改还会造成源/产物不一致。
- effect 是**多态**的：一个基类 `Effect` + 十几个子类（`AttackEffect`、`DefenseEffect`…）。子类名在运行时当判别符用（json 里是 `$type`，Excel 里是单元格第一段）。

完整字段定义见 [defines.xml](../../../DataTables/Defines/defines.xml)（effect 的权威 schema）。每种 effect 的字段/编码/战斗映射速查见 [references/effect-catalog.md](references/effect-catalog.md)。

## 标准流程

### 1. 理解需求，选定 effect 类型

把描述拆成一个或多个 effect。查 [references/effect-catalog.md](references/effect-catalog.md) 找匹配的类型。
- 「造成 X 点伤害」→ `AttackEffect`；「获得 X 点护甲」→ `DefenseEffect`/`InterceptEffect`；「推迟 X 格」→ `PushCollisionEffect`/`TimeShiftAllEffect`；「贴 buff」→ `BuffEffect`；「加牌进手」→ `AddToHandEffect`……
- 一张卡多个效果就是多个 effect（列表）。
- **如果没有现成类型匹配**，就是「新增 effect 类型」，转到下面的《新增 effect 类型》小节——这比录一行数据重得多，别硬套。

### 2. 定位目标表和目标行

- 卡牌：`DataTables/Datas/Character/#CardInfo.xlsx`
- 敌人技能：`DataTables/Datas/Enemy/#EnemySkillInfo.xlsx`

**永远按 Id 定位行，绝不按固定行号**。用户在 Excel 里增删/重排后行号会整体漂移（实测同一张卡从 r14 挪到过 r70），写死行号会改错行。先读表、用 openpyxl 按 `Id` 列匹配到目标行，写入前再断言该行 `Id` 和旧 `Effects` 值符合预期（护栏），不符就中止。

**动手前先看该表里已有的行**，把邻近几行的 `Effects` 单元格当作格式样板——现有数据永远是最可靠的编码参考，胜过任何文档。先读懂表结构再动，尤其见下方《数据表结构须知》。

### 3. 写 effect 单元格编码

单元格编码规则（以 `#CardInfo.xlsx` 的 `Effects` 列为准，表头 `##type` = `(list#sep=;),(Effect#sep=,)`）：

- **多个 effect 用 `;` 分隔**，**单个 effect 内部字段用 `,` 分隔**。
- 单个 effect 格式：`类型名,EffectType,字段1,字段2,...`
  - 第 1 段：子类名（如 `AttackEffect`），大小写要和 defines.xml 完全一致。
  - 第 2 段：`EffectType`（EffectEnum 缩写）：`A`=Attack `D`=Defense `T`=TimeSlot `H`=Heal `N`=Null `B`=Buff `F`=Fill。
  - 之后：按 defines.xml 里该子类 `<var>` 的**声明顺序**依次填值。
- **子字段内部的列表用 `|`**（避开 `,`/`;`），如 `AttackExtraEffect` 的 conditions：`Channeling|Recoil`。

实例（均取自现有表）：
```
AttackEffect,A,12,false                                  单体 12 伤害
AttackEffect,A,13,true;DefenseEffect,D,3,true            群体 13 伤害 + 每次命中 +3 护甲
AttackExtraEffect,A,9,Channeling|Recoil,2.0              9 伤害，目标处于引导/后摇则 ×2
AttackEffect,A,16,false;PushCollisionEffect,T,1,Stun     16 伤害并推迟 1 格（撞击致晕）
PushCollisionEffect,T,1,Stun
```

### 4. 合法性检查（导表前必做）

每加/改一条 effect，逐项核对——这是这个 skill 存在的主要价值，别跳过：

1. **类型名合法**：第 1 段必须是 defines.xml 里定义、且在 [Effect.cs](../../../Assets/Gen/Effect.cs) 的 `DeserializeEffect` switch 里注册的子类。拼错 = 导表直接 `SerializationException`。
2. **字段数量/顺序/类型对齐**：字段个数和 defines.xml 里该子类的 `<var>` 数一致，顺序一致，类型对（int/float/bool/string）。bool 写 `true`/`false`。
3. **EffectType 缩写有效**：见上表 7 个值。
4. **引用 Id 存在**：
   - `BuffEffect` 的 `buff_id` → 必须存在于 `DataTables/Datas/#BuffInfo.xlsx`。
   - `AddToHandEffect` 的 `card_id` → 必须存在于 `#CardInfo.xlsx`。
5. **战斗侧有映射**：确认该 effect 类型在 `ConvertEffectToCommand` 里有对应 Command，否则**能导表但战斗中是哑效果**。已映射清单见 [references/effect-catalog.md](references/effect-catalog.md) 的「战斗映射」列；未映射的类型要向用户点明「这个 effect 目前战斗里不生效」。
6. **描述占位符同步**：`Description` 列里的 `{X}` 是给玩家看的数值占位，运行时按**占位符字母 = 效果的 EffectType 缩写**去匹配替换。字母映射（对齐 `CardDescriptionParser`）：`A`=Attack `D`=Defense `T`=TimeSlot `H`=Heal `B`=Buff `N`=Null `F`/`V`=Fill。多个同类效果用索引 `{A:1}` 取第 2 个。**常见坑**：buff 数值（如抽牌数、层数）几乎都编码成 `BuffEffect,F,...`（EffectType=F），描述要用 `{V}` 或 `{F}`，**别凭直觉写 `{N}`**（`{N}`=Null，匹配不到就会原样显示）。加了带数值的 effect 后，检查描述文案与数值是否一致——这一层校验脚本已能自动查。

跑校验脚本一次性扫全表（**依赖 openpyxl**：`pip install openpyxl`）：
```
python .claude/skills/card-effect-authoring/scripts/validate_cardinfo.py
```
它对照 defines.xml 校验每个 `Effects` 单元格的类型名、字段数、EffectType、Buff/卡牌 Id 引用，**以及描述里的 `{X}`/`{X:i}` 占位符能否匹配到卡上对应 EffectType 的效果**（例如 `抽{N}张牌` 配 `BuffEffect,F,...`——`{N}` 是 Null 类型、卡上没有 Null 效果，会被标出，因为运行时 `CardDescriptionParser` 也替换不了、会原样显示 `{N}`），列出所有问题。
**它查得了什么、查不了什么**：做**格式、引用、占位符可解析性**校验；但它**判断不了 effect 的语义是否真的符合描述文字**（比如「描述写抽牌、effect 却是加护甲」——只要占位符字母碰巧能匹配到某个同类效果，脚本就放行）——这种语义比对仍得人来做。它也**不会误报被 `##` 注释掉的行**（见下方《数据表结构须知》）。

### 5. 重新导表（gen.bat）

改完 Excel 后重新生成。**注意 `gen.bat` 结尾有 `pause`，直接跑会挂住等按键**——用下面任一方式：

```bash
# 方式 A：喂一个回车让 pause 通过
cd F:/Ashlight/DataTables && echo. | cmd //c gen.bat

# 方式 B：直接跑底层 dotnet 命令（等价，不会 pause）
cd F:/Ashlight/DataTables && dotnet ../Tools/Luban/Luban.dll -t all -d json -c cs-simple-json \
  --conf ./luban.conf -x outputCodeDir=../Assets/Gen -x outputDataDir=../Assets/Resources/Config
```

### 6. 验证产物

- 打开对应的 `Assets/Resources/Config/*.json`（如 `character_tbcardinfo.json`），确认目标行的 `Effects` 数组里新 effect 的 `$type` 和字段值符合预期。
- Luban 报错通常指到具体表/行/列，按提示回 Excel 修。
- 若涉及新增 effect 类型，回 Unity 确认 `Assets/Gen/` 已更新且能编译。

## 新增 effect 类型（重活，别和录数据混淆）

当没有现成类型能表达需求时，要动 schema 和代码。这比录一行数据重得多，**别默默开工**——先按下面第 0 步告知用户。

0. **先向用户明确输出**：告诉用户「这个需求现有 effect 类型覆盖不了，需要**新增一个 effect 类型 `XxxEffect`**」，并列出：它的字段、拟映射到哪个 Command、要改动哪些文件（defines.xml + 两处 `ConvertEffectToCommand` + 可能的新 Command）。让用户确认后再动手——因为这一步会改代码，影响面比录数据大得多。
1. 在 [defines.xml](../../../DataTables/Defines/defines.xml) 加一个 `<bean name="XxxEffect" parent="Effect">`，按需加 `<var>`。
2. 跑 gen.bat —— Luban 会自动在 `Effect.cs` 的 `DeserializeEffect` switch 里加上 `case "XxxEffect"`，并生成 `Assets/Gen/XxxEffect.cs`。
3. **加战斗映射（关键，容易漏）**：在 `ConvertEffectToCommand` 里加 `if (effect is XxxEffect e) return new XxxCommand(...)`。这段逻辑**有两份**，视用到哪条路径决定是否都改：
   - [CardPlayResolver.cs](../../../Assets/Scripts/Battle/Core/Engine/CardPlayResolver.cs)（当前 ATB 离散回合制在用）
   - [CardToTimelineConverter.cs](../../../Assets/Scripts/Battle/Core/Engine/CardToTimelineConverter.cs)（旧时间轴路径）
   ⚠️ 两处的 `ActionBarShiftCommand` 传参符号相反（一处取负），加/改位移类 effect 时注意。
4. 若需要新 Command，在 `Assets/Scripts/Battle/Core/Commands/` 下新建（参考同目录现有 Command）。
5. **登记进 catalog（别漏）**：在 [references/effect-catalog.md](references/effect-catalog.md) 的「Effect 子类」表加一行，填：类型名、字段（顺序）、单元格示例、战斗映射（→哪个 Command，没映射填 `—`）。catalog 是这个 skill 的权威速查表，漏登记会导致下次录数据/校验时找不到新类型。同时脚本校验器（对照 defines.xml 动态解析，无需改）会自动认得新类型。
6. 到这一步才回第 3 节，把新类型录进 Excel 数据行。

## 数据表结构须知（Luban xlsx 的坑，实测踩过）

改表/读表/写校验脚本前必须懂这几条，否则会读错数据还以为一切正常：

1. **数据行的 A 列是空的**。A 列只放 `##var`/`##type`/`##group`/`##` 这些元标记，真正的 `Id` 在 B 列、`Effects` 在 E 列。判断「是不是数据行」**绝不能用 A 列非空**——那会把每一条数据行都当空行跳过。正确判据：整行非空 且 A 列不以 `##` 开头。
2. **A 列填 `##` = 该行被 Luban 注释掉、跳过导入**。所以表里可能有一堆「看着有数据但其实没进游戏」的行（实测有 25 张卡被 `##` 注释，生成的 json 里没有）。校验/统计时要连同 Luban 一起把这些行视为不存在。发现某张卡「配了却不生效」，先看它 A 列是不是 `##`。
3. **一个 workbook 里多个 `##var` sheet 会被合并成一张逻辑表**。例如 `#CardInfo.xlsx` 的飞刀 token `Extra001` 单独放在 `Sheet2`。只读第一张 sheet 会漏掉其它 sheet 的行，导致 `card_id`/`buff_id` 引用误报「不存在」。读表要遍历所有 `##var` sheet。
4. **逻辑 sheet 名 ↔ 物理文件名（sheet1.xml…）不保证同序**，走 rId 关系表映射。别用 zipfile 硬读 `sheet1.xml`——会读串 sheet。**统一用 openpyxl 读**（它正确解析映射），并按「A1 == `##var`」认数据 sheet。
5. **行号会漂移**：用户在 Excel 里增删/重排后，同一张卡的行号会变。**一律按 Id 定位，写死行号迟早改错行**（见第 2 步）。
6. **写文件会被 Excel 独占锁挡住**：目标 xlsx 开在 Excel 里时，openpyxl 保存/gen.bat 导表都会 `PermissionError`（目录里会有 `~$` 开头的锁文件）。写前确保文件已关闭。

以上第 1、3、4 条正是 `scripts/validate_cardinfo.py` 已经处理好的——改脚本时别把这些修复改回去。

## 关键红线

- 改 Excel，不改 `Assets/Gen/*.cs` 和 `Assets/Resources/Config/*.json`。
- 录数据前先看现有行当样板；**按 Id 定位行，不按行号**。
- 读表/校验遵守《数据表结构须知》：数据行 A 列为空、`##` 行是注释、合并所有 `##var` sheet、用 openpyxl。
- 导表前跑完第 4 节的合法性检查（脚本只查格式与引用，**描述↔effect 的语义一致性要人工比对**）。
- 新增 effect 类型：**先向用户输出说明并等确认**（会改代码，非小改动）；补上战斗侧 `ConvertEffectToCommand` 映射，否则是哑效果；改完把新类型**登记进 [effect-catalog.md](references/effect-catalog.md)**。
