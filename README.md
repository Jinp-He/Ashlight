# DataTables Luban 结构阅读指引（给 AI）

本文件用于指导后续 AI 在本项目中正确阅读和理解 Luban 结构表，避免只看单表导致误判。

## 1. 阅读顺序（必须按顺序）

1. `DataTables/Datas/__enums__.xlsx`  
   先确定所有枚举定义（例如 `CharacterEnum`、`TargetTypeEnum`、`RarityEnum`、`EnemyIntentionEnum`、`CardTypeEnum`）。
2. `DataTables/Datas/__beans__.xlsx`  
   再确定所有 Bean/多态结构（例如 `Effect` 及其派生、`Strategy`、`EnemyIntention`、`EnemyIntentions`）。
3. `DataTables/Datas/__tables__.xlsx`  
   再看表声明：`full_name`、`value_type`、`input`、`index`、`mode`、`output`。
4. 业务数据表（`DataTables/Datas/**/#*.xlsx`）  
   最后读取具体数据，且必须结合前 3 步的类型信息解释。

## 2. Luban 表头规则（本项目适用）

- `##var`：字段名行（可有多级 `##var`）。
- `##type`：字段类型行。
- `##group`：导出分组行（本项目多数为空，视为全组导出）。
- `##...`：注释行（例如 `##`、`##comment`）。
- 当某列字段名为空或以 `#` 开头时，该列应视为注释列忽略。

官方参考：
- [Luban 文档首页](https://www.datable.cn/)
- [Excel 格式（高级，4.x）](https://www.datable.cn/docs/manual/exceladvanced)
- [Excel 紧凑格式（4.x）](https://www.datable.cn/docs/manual/excelcompactformat)
- [Quick Start（4.x）](https://www.datable.cn/en/docs/beginner/quickstart)

## 3. 先建“类型字典”，再读数据行

AI 必须先建立以下字典，再解析业务表单元格：

- **枚举字典**：来自 `__enums__.xlsx`。
- **Bean 字典**：来自 `__beans__.xlsx` 的 `full_name/parent/*fields`。
- **表字典**：来自 `__tables__.xlsx` 的 `full_name -> input/value_type/index/output`。

若未建字典，禁止直接解释复合列（如 `Effects`、`StrategySet`、`IntentionSet`）。

## 4. 紧凑格式与分隔符规则（重点）

本项目大量使用 Luban 紧凑/流式格式：

- `list` 常见写法：`(list#sep=;),T` 或 `(list#sep=,),T`
- Bean 常见写法：`(Effect#sep=,)`
- 组合类型示例：`(list#sep=;),(Effect#sep=,)`

解释原则：

1. 先按外层 `list` 的 `sep` 切分元素。
2. 每个元素再按 Bean 的 `sep` 解释字段顺序。
3. 若 Bean 为多态，先读取类型标识（例如 `AttackEffect`），再按对应派生结构解析。

## 5. 项目当前表与生成类映射（已落地）

以 `Assets/Gen/Tables.cs` 和 `Assets/Resources/Config/*.json` 为准：

- `Character.TbCardInfo`  
  - 输入：`DataTables/Datas/Character/#CardInfo.xlsx`
  - 输出：`Assets/Resources/Config/character_tbcardinfo.json`
  - 记录类：`cfg.Character.CardInfo`
- `Character.TbCardInfo_backup`  
  - 输入：`DataTables/Datas/Character/#CardInfo_backup.xlsx`
  - 输出：`Assets/Resources/Config/character_tbcardinfo_backup.json`
  - 记录类：`cfg.Character.CardInfo_backup`
- `Character.TbCharaterInfo`  
  - 输入：`DataTables/Datas/Character/#CharaterInfo.xlsx`
  - 输出：`Assets/Resources/Config/character_tbcharaterinfo.json`
  - 记录类：`cfg.Character.CharaterInfo`
- `Enemy.TbEnemyInfo`  
  - 输入：`DataTables/Datas/Enemy/#EnemyInfo.xlsx`
  - 输出：`Assets/Resources/Config/enemy_tbenemyinfo.json`
  - 记录类：`cfg.Enemy.EnemyInfo`
- `Enemy.TbEnemySkillInfo`  
  - 输入：`DataTables/Datas/Enemy/#EnemySkillInfo.xlsx`
  - 输出：`Assets/Resources/Config/enemy_tbenemyskillinfo.json`
  - 记录类：`cfg.Enemy.EnemySkillInfo`
- `Enemy.TbEncounter`  
  - 输入：`DataTables/Datas/Enemy/#Encounter.xlsx`
  - 输出：`Assets/Resources/Config/enemy_tbencounter.json`
  - 记录类：`cfg.Enemy.Encounter`
- `TbCustomColor`  
  - 输入：`DataTables/Datas/#CustomColor.xlsx`
  - 输出：`Assets/Resources/Config/tbcustomcolor.json`
  - 记录类：`cfg.CustomColor`
- `TbNounDictionary`  
  - 输入：`DataTables/Datas/#NounDictionary.xlsx`
  - 输出：`Assets/Resources/Config/tbnoundictionary.json`
  - 记录类：`cfg.NounDictionary`

## 6. 项目当前核心 Bean/多态结构（高频）

### 6.1 Effect 多态层级

- 抽象基类：`cfg.Effect`
- 已生成派生：
  - `AttackEffect`
  - `AttackExtraEffect`
  - `AttackConditionalEffect`
  - `DefenseEffect`
  - `InterceptEffect`
  - `HealEffect`
  - `ClearRecoilEffect`
  - `PushCollisionEffect`
  - `TimeShiftAllEffect`
  - `CollisionEffect`
  - `SwiftEffect`
  - `ChannelEffect`
  - `TauntEffect`
  - `BuffEffect`

### 6.2 结构引用（ref）关系

- `CharaterInfo.BaseDeck` -> `string#ref=Character.TbCardInfo`  
  生成后会在运行时解析为 `BaseDeck_Ref`。
- `Encounter.EnemySet` -> `string#ref=Enemy.TbEnemyInfo`  
  生成后会在运行时解析为 `EnemySet_Ref`。
- `EnemyIntention.EnemySkillIndex` -> `string#ref=Enemy.TbEnemySkillInfo`

## 7. AI 读取时的强制校验清单

每次读取或改表时，AI 至少执行以下检查：

1. `__enums__` / `__beans__` / `__tables__` 三表是否与业务表字段类型一致。
2. 业务表 `##var` 与 `##type` 列数是否一一对应（忽略注释列后）。
3. `#ref=` 引用值是否在目标表主键集合内。
4. 多态列（如 `Effects`）类型名是否存在于当前 Bean 派生集合。
5. `Assets/Gen/*.cs` 与 `Assets/Resources/Config/*.json` 是否与表结构同步（避免“表改了但未重新生成”）。

## 8. 与运行时加载链路的关系

运行时由 `Assets/Scripts/Config/ConfigLoader.cs` 加载：

1. `cfg.Tables` 按固定文件名读取 `Resources/Config/*.json`。
2. `Tables.ResolveRef()` 执行跨表引用解析。
3. 游戏系统只应消费解析后的只读配置，不直接依赖 Excel 原始文本。

## 9. 发现异常时的处理策略（给 AI）

- 若出现 `_x000D_` 这类尾缀字段名：先视为 Excel 历史换行污染，优先对齐生成代码中的最终字段名判定真实结构。
- 若出现中文乱码：先判断是否为终端编码显示问题，不要直接修改业务数据；以 Excel 客户端或生成 JSON 内容为准二次确认。
- 若 `Temp/` 与中间产物出现差异：忽略 `Temp`，只比较 `DataTables`、`Assets/Gen`、`Assets/Resources/Config`。

---

维护约定：当新增或删除表时，必须同时更新本文件第 5 节（表映射）和第 6 节（结构映射）。
