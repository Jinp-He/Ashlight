# 双角色卡组设计 · #CardInfo 接表版 v1（周周 & 艾琳）

> **文档性质**：设计转表稿 —— 把《【双角色·百相卡牌与BD思路】》(GameDesigner, 2026-07-13) 的牌库设计，按 `DataTables/Datas/Character/#CardInfo.xlsx` 的真实列结构重排，供评审后直接录入。
> **转换日期**：2026-07-15
> **数值口径**：源文档数值全为 `[PLACEHOLDER]`，本文按源文档示例值填入，playtest 后回调。
> **落表状态标记**：✅ 现有 effect 体系可直接落表 ｜ 🟡 主体可落表、局部待补 ｜ 🔴 依赖未实装的新机制（见 §3.6 清单），落表前需先开发。
> **红线**：本文档只是接表稿，真正录入时走 `card-effect-authoring` 流程 —— 只改 Excel 源、`edit_cell.py` 按 Id 写入、`validate_tables.py` 校验、再导表。

---

## 1. 设计思路

### 1.1 两角色定位

| 维度 | 周周（游侠 · Zhouzhou） | 艾琳（法师 · Irene） |
|---|---|---|
| 幻想 | 位移驱动的刀客 / 战场调度 | 指挥副手 + 读轴爆发 |
| 主资源 | **士气**（行动投资，消耗式：飞刀打出即清空） | **易伤**（状态投资，衰减式：怪新回合层数减半） |
| 飞轮 | 移动（自己动 / 帮队友动）→ 攒士气 → 飞刀/闪回转伤害 | 召唤物铺场/转化 → 推拉保鲜易伤 → 定点引爆 |
| 执行卡子型 | **蓄力型**：不插队列、无费用、下次自身行动结算，卡面只标层数 | **标准插入型**：`执行·N` 从当前行动格往后数 N 格插入，恒 0E |
| 防御面 | 抱虚式 → 闪避（消耗型） | 牺牲契约清 debuff + 推迟错峰 |
| 主要红线 | 士气乘法复利 | 易伤无限叠 / 执行卡超频 |

### 1.2 公用规则（两人共享）

- **执行卡闸门**：每人每行动周期（10 格队列）限 1 张执行牌生效 ——「每轮 1 次」资源，不是「每次行动」资源。
- **牌库经济**：起始牌库 5 张 ＝ Draw 5 ⇒ 每回合抽满整库、前期连招确定性复现；总库 32 张经升级解锁 ⇒ 后期方差。起始牌库全普通 ⇒ 稀有/史诗不进起手，前期伤害被闸门天然限制。
- **基础参数**：两人均 `Energy=3`/回合、`Draw=5`/回合、HP=100、`Mastery=1 [PLACEHOLDER]`；周周 `Speed=3`（每轮动 2–3 次），艾琳 `Speed=5`（每轮动 2 次）。
- **平衡护栏**：周周单回合最多 3 次位移（Energy=3）⇒ 飞刀峰值受限；艾琳易伤靠 decay 自校正（建议聚合层数 ≤30）；每轮 1 张执行卡为时序护栏。

### 1.3 本文档的转表原则

1. 源文档「即时·E / 执行·N / 蓄力」映射到表列 `CardType + Energy + ExecutingCost`（对照见 §2.1）。
2. Effects 列一律给出单元格编码（多 effect 用 `;`，字段用 `,`，与现行表一致）；引用了**不存在的 effect 类型 / buff / 条件**时用 `⚠️` 内联标注并汇总到 §3.6。
3. 现有 Id 段已用到 `Zhouzhou020` / `Irene023`，新卡从 `Zhouzhou021` / `Irene024` 续编，衍生卡续 `Extra002`，避免与现行表冲突。**本设计与现行 Zhouzhou001–020 是两套牌库**，录入前需先定「替换还是并存」。
4. 被动 / 百相不是 CardInfo 数据：被动为角色固有逻辑（代码/角色表），百相走 `#UpgradeOptions` 的 `UpgradeEffect` 体系；召唤物需要新表（参照天气系统的 ATB 虚拟单位）。去向见 §3.7。

---

## 2. 名词解释

### 2.1 设计术语 → 表字段映射

| 设计写法 | 含义 | CardInfo 落表 |
|---|---|---|
| `即时·E` | 打出当回合结算，耗 E 点能量 | `CardType=Swift`，`Energy=E`，`ExecutingCost=0` |
| `执行·N` | 从当前行动格往后数 N 格插入队列，N 越大越晚生效；恒 0 能量 | `CardType=Execution`，`Energy=0`，`ExecutingCost=N` ⚠️现行表的 Execution 卡 Energy>0（如 Irene001=3E），「执行恒 0E」是本设计的新口径，录入前需统一 |
| `执行·蓄力`（周周专属） | 不插队列、无费用，挂`蓄力计数器`，下次自身行动结算；被`推迟 N`→层数+N，倒下作废 | `CardType=Execution`，`Energy=0`，`ExecutingCost=0` 🔴 整套蓄力机制未实装，字段映射待定（源文档 §5 亦列为待办） |
| `召唤·E` | 召唤插入行动队列的虚拟单位 | `CardType=Swift` + 🔴 新 `SummonEffect`（可参照天气系统「雷暴=ATB 虚拟单位」的实现路线） |
| `E`（能量费用） | 即时/召唤卡的资源消耗 | `Energy` 列 |
| `N`（插入格数） | 执行卡的时序成本，**不是能量** | `ExecutingCost` 列 |

### 2.2 关键词 → 现有机制映射

| 关键词 | 定义（源文档口径） | 引擎现状 |
|---|---|---|
| **士气 Morale** | 消耗式增伤 buff：造成伤害时消耗全部层数，每层 +2 伤害；周周自身位移成功执行获 `1×精通` 层 | 🔴 无此 buff，需新增 `#BuffInfo` 条目 + 结算逻辑（含被动触发） |
| **飞刀** | 0E 衍生牌（带闪回），把士气直接转伤害；基础伤害 `3×精通`，有惊鸿式时改 `当前士气×精通` | 🔴 卡壳可建（`Extra002`，同小刀 `Extra001` 路线），但「消耗士气加伤」需新 effect |
| **闪回** | 临时复制牌标签：不进牌库、回合末销毁 | 🟡 「回合末销毁」＝`IsEthereal=TRUE`、「打出即销毁」＝`IsExhaust=TRUE`（现行移动牌 `*000` 已用此组合）；但「复制某张牌的闪回副本」「按闪回牌计数」无现成 effect |
| **易伤 Vulnerable** | +10%/层、不消耗、怪自身新回合开始层数减半（向下取整） | 🟡 `Vulnerable` buff 已存在（按百分比值，如 `BuffEffect,F,Vulnerable,50`）；按本设计应以 10/层 落表。**衰减（新回合减半）未实装**——回合 buff 管线中 duration 衰减尚未接入 ATB 原子回合 |
| **推迟 / 加速（推拉）** | 队列退后 / 前进 N 格；推迟敌人＝延缓其新回合＝延缓易伤 decay | ✅ 单体 `PushCollisionEffect,T,±N,Stun`，AOE `TimeShiftAllEffect,T,N`（负数=提前，见 Irene014 拽引） |
| **当前行动格** | 此刻正在行动的那一格单位，读轴判定的唯一窗口 | ✅ 已有同口径 effect（`AttackCurrentRoundEffect` / `StunCurrentRoundEffect` 等「当前回合」系列） |
| **召唤物** | 插入行动队列的虚拟单位：无 HP、不可受伤、仅 1 个在场、按持续回合存在、跑固定脚本 | 🔴 未实装；天气系统的「ATB 虚拟单位第三方阵营」是现成参照 |
| **灌注**（艾琳被动） | 场上有召唤物时，全手牌能量费用 -1（最低 0）；执行卡恒 0E 不受影响 | 🔴 被动逻辑，不落 CardInfo |
| **保留** | 回合结束不弃，留在手中 | 🔴 无此关键词/字段 |
| **精通 Mastery** | 数值系数，固定值不堆叠，当前=1 `[PLACEHOLDER]` | 🟡 表内暂按 Mastery=1 折算成定值落表，系数化留待后续 |
| **蓄力计数器 / 蓄力层数** | 蓄力卡等待期的强度计量；被推迟层数反而+N（沉没成本补偿） | 🔴 未实装 |
| **百相** | 升级池的 multiplicative BD 层，每人 5 个 | 走 `#UpgradeOptions`（`GrantBuff`/`ModifyCardStat` 等 `UpgradeEffect` 体系），非 CardInfo |

### 2.3 CardInfo 列语义速查

| 列 | 含义 | 本文档取值约定 |
|---|---|---|
| `Id` | 唯一键，`角色名+三位序号` | 周周 021+，艾琳 024+，衍生 Extra002+ |
| `Name` / `Description` | 卡名 / 卡面文本，占位符 `{A}`伤害 `{D}`护甲 `{T}`推迟格 `{H}`治疗 `{V}`buff/数值 | 与 Effects 编码逐一对应 |
| `Effects` | 效果编码：`类型名,EffectType缩写,字段...`，多效果 `;` 分隔 | ⚠️ 标注=该段引用未实装机制 |
| `CardType` | `Swift`(即时/召唤) / `Execution`(执行) | 见 §2.1 |
| `BelongTo` | `Zhouzhou`(游侠) / `Irene`(法师) / `Rocket`(战士) | 分节固定 |
| `TargetType` | `SingleAlly / AllAlly / Self / SingleEnemy / AllEnemy` | 逐卡标注 |
| `Rarity` | `Normal / Rare / Epic` | 对应源文档 普通/稀有/史诗 |
| `Energy` / `ExecutingCost` | 能量费 / 执行插入格数 | 见 §2.1 |
| `IsEthereal` / `IsExhaust` | 回合末销毁 / 打出后销毁 | 衍生·闪回牌 TRUE，其余 FALSE |
| `TargetZone` / `CastZone` | 目标须在的排 / 施放者须在的排（`Front/Back/Any`） | 默认 `Any`，例外逐卡标注 |
| `IsLocked` / `IsInUpgrade` | 是否锁定 / 是否进升级池 | 起始 5 张 `IsLocked=FALSE`；其余 `TRUE`（升级解锁）⚠️ IsLocked 运行时语义需与解锁系统对齐 |

---

## 3. 卡组设计

### 3.1 周周（游侠）牌库

**本节固定列**：`BelongTo=Zhouzhou`；未标注时 `TargetZone=Any`、`CastZone=Any`、`IsEthereal=FALSE`、`IsExhaust=FALSE`、`IsInUpgrade=TRUE`。

#### 普通（Rarity=Normal · 起始牌库来源 · IsLocked=FALSE）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | 状态 |
|---|---|---|---|---|---|---|---|---|
| Zhouzhou021 | 崩拳 | 造成{A}点伤害 | `AttackEffect,A,4,false` | Swift | SingleEnemy | 1 | 0 | ✅ |
| Zhouzhou022 | 游身 | 获得{D}点护甲；若持有[士气]，获得[闪避]{V} | `DefenseEffect,D,2,false;BuffConditionalEffect,F,Dodge,1,HasMorale⚠️` | Swift | Self | 1 | 0 | 🔴 条件 `HasMorale` 与士气 buff 均未实装 |
| Zhouzhou023 | 踏歌 | [移动]一名队友，自身获得{V}层[士气] | `MovePositionEffect,N,Toggle;BuffEffect,F,Morale⚠️,1` | Swift | SingleAlly | 1 | 0 | 🔴 Morale 未实装；且 buff 需落在**施法者**而非卡目标，现行 BuffCommand 目标口径需确认 |
| Zhouzhou024 | 疾步 | [移动]，获得1张[飞刀] | `MovePositionEffect,N,Toggle;AddToHandEffect,N,Extra002,1` | Swift | Self | 1 | 0 | 🟡 结构=现行 Zhouzhou006（小刀版）；卡本身✅，依赖飞刀 Extra002（🔴） |
| Zhouzhou025 | 吐纳 | 我方全体获得{V}层[士气] | `BuffEffect,F,Morale⚠️,1` | Swift | AllAlly | 0 | 0 | 🔴 Morale 未实装 |
| Zhouzhou026 | 雁落京门 | 使目标[推迟]{T}格；若目标为友方，其获得{V}层[士气] | `PushCollisionEffect,T,2,Stun;BuffConditionalEffect,F,Morale⚠️,2,TargetIsAlly⚠️` | Swift | SingleAlly⚠️ | 1 | 0 | 🔴 敌我皆可选的单体目标现行 `TargetType` 枚举表达不了；Morale/条件未实装 |

#### 稀有（Rarity=Rare · IsLocked=TRUE）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | 状态 |
|---|---|---|---|---|---|---|---|---|
| Zhouzhou027 | 月惊山鸟 | 造成{A}点伤害；本回合每打出过1张[闪回]牌，重复1次 | `AttackPerEchoEffect⚠️,A,5` | Swift | SingleEnemy | 1 | 0 | 🔴 按闪回牌计数重复，无现成 effect |
| Zhouzhou028 | 仙人观棋 | 复制弃牌堆顶3张牌的[闪回]副本入手 | `CopyFromDiscardEffect⚠️,N,3` | Swift | Self | 1 | 0 | 🔴 弃牌堆复制，无现成 effect |
| Zhouzhou029 | 飒沓流星 | [移动]，获得1张[飞刀]；本回合每次移动，抽1张牌 | `MovePositionEffect,N,Toggle;AddToHandEffect,N,Extra002,1;OnMoveDrawEffect⚠️,F,1` | Swift | Self | 1 | 0 | 🟡 前两段✅；OnMoveDraw 未实装，但与现有 `OnMoveDamageEffect`/`OnMoveAddCardEffect` 同构（移动触发器已就位），补一个子类即可 |
| Zhouzhou030 | 问云手 | [蓄力]：结算时抽取等同蓄力层数的牌 | `ChargedDrawEffect⚠️` | Execution | Self | 0 | 0⚠️ | 🔴 蓄力子型整套未实装（不插队列/下次自身行动结算/推迟加层） |
| Zhouzhou031 | 叶底藏花 | [蓄力]：结算时造成 8×蓄力层数 点伤害 | `ChargedAttackEffect⚠️,A,8` | Execution | SingleEnemy | 0 | 0⚠️ | 🔴 同上 |
| Zhouzhou032 | 西决昆仑 | [蓄力]：结算时对所有敌人造成 6×蓄力层数 点伤害；等待期受击时[士气]翻倍 | `ChargedAttackEffect⚠️,A,6,true;OnHitMoraleDoubleEffect⚠️` | Execution | AllEnemy | 0 | 0⚠️ | 🔴 同上 + 受击触发器未实装 |

#### 史诗（Rarity=Epic · IsLocked=TRUE）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | 状态 |
|---|---|---|---|---|---|---|---|---|
| Zhouzhou033 | 千里不留行 | 本回合[移动]不消耗能量；获得3张[移动]的[闪回]副本 | `ModifyMoveCostThisTurnEffect⚠️,N,0;AddToHandEffect,N,Zhouzhou000,3` | Swift | Self | 2 | 0 | 🟡 加 3 张移动牌可用现行编码（Zhouzhou000 本身已是 Ethereal+Exhaust，天然闪回）；「本回合移动 0 费」无现成 effect |
| Zhouzhou034 | 十步杀一人 | 重复本回合已打出的所有[闪回]牌；本回合[移动]费用-1 | `ReplayEchoesEffect⚠️;ModifyMoveCostThisTurnEffect⚠️,N,-1` | Swift | Self | 3 | 0 | 🔴 终局爆点（combo ≈162），闪回重放无现成 effect |

#### 衍生卡（不入牌库 · 不进升级池）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | IsEthereal | IsExhaust | 状态 |
|---|---|---|---|---|---|---|---|---|---|---|
| Extra002 | 飞刀 | 造成{A}点伤害；消耗全部[士气]，每层额外+2伤害 | `MoraleAttackEffect⚠️,A,3` | Swift | SingleEnemy | 0 | 0 | TRUE | TRUE | 🔴 基础伤害=3×精通；消耗士气加伤需新 effect；惊鸿式百相改「当前士气×精通」走 UpgradeEffect |

**周周基础牌库（5 张 · 全普通）**：崩拳×2、疾步×2、踏歌×1。
（CardInfo 无「数量」列，起始牌组的张数配置落在角色初始卡组配置处，非本表。总库：普通 6 / 稀有 6 / 史诗 2，飞刀为衍生不入库。）

---

### 3.2 艾琳（法师）牌库

**本节固定列**：`BelongTo=Irene`；未标注时 `TargetZone=Any`、`CastZone=Any`、`IsEthereal=FALSE`、`IsExhaust=FALSE`、`IsInUpgrade=TRUE`。
⚠️ **与现行表冲突提醒**：现行 Irene001–023（含一张同名「连锁闪电」Irene009）是旧卡池，本节为新设计，录入前需定夺替换/并存/改名。

#### 普通（Rarity=Normal · 起始牌库来源 · IsLocked=FALSE）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | 状态 |
|---|---|---|---|---|---|---|---|---|
| Irene024 | 狐 | 召唤[狐]（持续{V}回合）：行动时对同格第一个敌人造成{A}点伤害 | `SummonEffect⚠️,N,Fox,3` | Swift | Self | 3 | 0 | 🔴 召唤物体系未实装（Speed=4/持续3/固定脚本，参照天气虚拟单位） |
| Irene025 | 蛇 | 召唤[蛇]（持续{V}回合）：行动时使最远的敌人获得{V:1}层[易伤] | `SummonEffect⚠️,N,Snake,3` | Swift | Self | 2 | 0 | 🔴 同上 |
| Irene026 | 牺牲契约 | 销毁你的召唤物，清除自身所有负面效果 | `DestroySummonEffect⚠️,N;CleanseSelfEffect⚠️,N` | Swift | Self | 1 | 0 | 🔴 依赖召唤物体系 + 无清 debuff effect（现有 `ClearOverloadEffect` 只清过载） |
| Irene027 | 幽灵诡计 | 复制你打出的上一张牌（[保留]：回合结束不弃置） | `CopyLastPlayedEffect⚠️,N` | Swift | Self | 0 | 0 | 🔴 复制上一张牌 + [保留]关键词均未实装 |
| Irene028 | 冰霜震击 | 造成{A}点伤害并[推迟]{T}格 | `AttackEffect,A,20,false;PushCollisionEffect,T,1,Stun` | Execution | SingleEnemy | 0⚠️ | 5 | ✅ 可直接落表（Energy=0 取决于「执行恒 0E」新口径，见 §2.1） |
| Irene029 | 迟滞 | 使目标[推迟]{T}格 | `PushCollisionEffect,T,3,Stun` | Execution | SingleEnemy | 0⚠️ | 4 | ✅ 同上 |
| — | 疾影步 | [移动] | `MovePositionEffect,N,Toggle` | Swift | Self | 1 | 0 | ✅ 即现行通用移动牌 **Irene000**，无需新建；若按设计需作为可持有的牌库卡（非每回合发放的临时牌），则另建新 Id 并去掉 Ethereal/Exhaust |

#### 稀有（Rarity=Rare · IsLocked=TRUE）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | 状态 |
|---|---|---|---|---|---|---|---|---|
| Irene030 | 豹 | 召唤[豹]（持续{V}回合）：行动时对同格所有单位造成{A}点伤害 | `SummonEffect⚠️,N,Panther,3` | Swift | Self | 3 | 0 | 🔴 召唤物体系 |
| Irene031 | 鹿 | 召唤[鹿]（持续{V}回合）：行动时使同队列所有敌人[推迟]{T}格 | `SummonEffect⚠️,N,Deer,3` | Swift | Self | 2 | 0 | 🔴 同上（鞭笞百相的连带易伤走 UpgradeEffect） |
| Irene032 | 协同攻击 | 造成{A}点伤害；你的召唤物立即行动1次 | `AttackEffect,A,6,false;SummonActNowEffect⚠️,N` | Swift | SingleEnemy | 2 | 0 | 🔴 依赖召唤物体系 |
| Irene033 | 暴风雪 | 对[前排]所有敌人造成{A}点伤害并[推迟]{T}格 | `AttackEffect,A,10,true;TimeShiftAllEffect,T,1` | Execution | AllEnemy | 0⚠️ | 4 | 🟡 编码完全可落表（同现行 Irene006 寒霜新星结构，`TargetZone=Front`）；源文档「前/后场」二择的选排交互暂不支持，先锁前排 |
| Irene034 | 连锁闪电 | 对所有敌人造成{A}点伤害 | `AttackEffect,A,12,true` | Execution | AllEnemy | 0⚠️ | 4 | 🟡 基础段✅；「连锁百相：当前行动格目标伤害翻倍」走 UpgradeEffect（🔴 需新百相钩子）。⚠️ 与现行 Irene009 同名，需改名或替换 |
| Irene035 | 碎冰葬 | 将场上所有[易伤]转移到目标身上，随后造成{A}点伤害 | `TransferVulnerableEffect⚠️,N;AttackEffect,A,40,false` | Execution | SingleEnemy | 0⚠️ | 5 | 🔴 易伤转移无现成 effect（终局爆点 ≈104–160，聚合层数建议 ≤30 护栏） |

#### 史诗（Rarity=Epic · IsLocked=TRUE）

| Id | Name | Description | Effects（单元格编码） | CardType | TargetType | Energy | ExecutingCost | 状态 |
|---|---|---|---|---|---|---|---|---|
| Irene036 | 魂 | 召唤[魂]（持续{V}回合）：行动时对所有带[易伤]的单位造成{A}点伤害，易伤加成部分转为真实伤害（穿盾） | `SummonEffect⚠️,N,Soul,3` | Swift | Self | 3 | 0 | 🔴 召唤物体系 + 真伤拆分结算 |
| Irene037 | 哀悼曲 | 场上所有[易伤]层数翻倍；你的下一张执行卡瞬发（插入格数视为0） | `DoubleVulnerableEffect⚠️,N;NextExecutionInstantEffect⚠️,N` | Swift | AllEnemy | 3 | 0 | 🔴 两段均无现成 effect |

**艾琳基础牌库（5 张 · 全普通）**：狐、疾影步、冰霜震击、迟滞、牺牲契约。
（总库：普通 7 / 稀有 6 / 史诗 2。召唤物花名册的 Speed/持续/脚本 数据不进 CardInfo，需新表，见 §3.7。）

---

### 3.3 易伤的落表口径

现行 `Vulnerable` buff 按百分比取值（如现行 Zhouzhou010 `BuffEffect,F,Vulnerable,50` = 易伤50%）。本设计「每层 +10%」建议统一为 **1 层 = 10**：蛇给 2 层 → `BuffEffect,F,Vulnerable,20`。**衰减（怪新回合层数减半）未实装** —— 现行回合 buff 管线中 duration/衰减尚未接入敌人原子回合，需先补管线再落易伤流。

### 3.4 起始牌库 & 解锁配置

| 角色 | 起始 5 张（IsLocked=FALSE） | 升级解锁（IsLocked=TRUE） |
|---|---|---|
| 周周 | 崩拳×2、疾步×2、踏歌×1 | 其余普通 3 种 + 稀有 6 + 史诗 2 |
| 艾琳 | 狐、疾影步、冰霜震击、迟滞、牺牲契约 | 其余普通 2 种 + 稀有 6 + 史诗 2 |

张数（×2）与解锁顺序不在 CardInfo 列内，属角色初始卡组 / 升级池配置。

### 3.5 立即可落表清单（✅，机制零依赖）

| Id | 卡 | 说明 |
|---|---|---|
| Zhouzhou021 崩拳 | 纯攻击 | `AttackEffect` |
| Irene028 冰霜震击 | 攻击+单体推迟 | 现有编码组合 |
| Irene029 迟滞 | 单体推迟 | `PushCollisionEffect` |
| 疾影步 | 移动 | 复用 Irene000 |
| Zhouzhou024 疾步 | 移动+加牌 | 卡本身✅（等飞刀卡壳时先塞 `Extra001` 小刀占位亦可） |
| Irene033 暴风雪 / Irene034 连锁闪电 | AOE+推迟 / AOE | 基础段可先落，百相增益后补 |

### 3.6 需新增机制清单（按依赖排序，供排期）

| # | 机制 | 覆盖卡 | 规模 |
|---|---|---|---|
| 1 | **士气 buff**（Morale：造成伤害时全消耗、每层+2）+ 周周位移被动触发 | 游身/踏歌/吐纳/雁落京门/飞刀 + 4 个百相 | 新 buff + 结算钩子 |
| 2 | **飞刀衍生卡 + MoraleAttackEffect** | 飞刀/疾步/飒沓流星/惊鸿式 | 新 effect（依赖 #1） |
| 3 | **闪回体系**（复制副本入手 / 按闪回计数 / 重放） | 月惊山鸟/仙人观棋/十步杀一人/摘星式/飞鸟式 | Ethereal+Exhaust 已覆盖销毁语义，缺「复制/计数/重放」三个 effect |
| 4 | **蓄力子型**（不插队列、下次自身行动结算、推迟加层、倒下作废） | 问云手/叶底藏花/西决昆仑 | 新卡子型 + `蓄力计数器` buff（源文档 §5 待办） |
| 5 | **召唤物体系**（ATB 虚拟单位 + 固定脚本 + 持续回合 + 仅1在场） | 狐/蛇/豹/鹿/魂/牺牲契约/协同攻击 + 灌注被动 + 3 个百相 | 最大件；天气系统（雷暴虚拟单位）是现成参照 |
| 6 | **易伤衰减**（怪新回合层数减半） | 蛇/鹿/魂/碎冰葬/哀悼曲/鞭笞 | 依赖回合 buff 管线补全 |
| 7 | **本回合费用修改**（移动0费/-1费）、**[保留]**、**复制上一张牌**、**清自身 debuff**、**易伤转移/翻倍**、**下张执行瞬发**、**OnMoveDraw** | 千里不留行/十步/幽灵诡计/牺牲契约/碎冰葬/哀悼曲/飒沓流星 | 零散小件，OnMoveDraw 最易（移动触发器同构扩展） |

### 3.7 非 CardInfo 数据的去向

| 内容 | 去向 |
|---|---|
| 被动（周周位移得士气 / 艾琳灌注减费） | 角色固有逻辑：代码 + `#CharaterInfo` 相关字段，不落卡表 |
| 百相 ×10（摘星/飞鸟/抱虚/借月/惊鸿；阴影绵绵/福音/鞭笞/连锁/心智连结） | `#UpgradeOptions` 的 `UpgradeEffect` 体系（`GrantBuff`/`ModifyCardStat`/…）；多数需要新触发钩子，现有四子类只覆盖静态数值修改 |
| 召唤物花名册（Speed/持续/固定脚本） | 🔴 需新表（建议 `#SummonInfo`，`#` 前缀自动导入），脚本行为参照 `#EnemySkillInfo` 或天气脚本 |
| Speed/Energy/Draw/HP/Mastery 基础参数 | `#CharaterInfo` |
| 士气/易伤(衰减版)/蓄力计数器 等新 buff | `#BuffInfo` |

---

## 4. 待校准（承接源文档 §5）

- 所有数值为 `[PLACEHOLDER]`，待 playtest；真实怪物 HP 锚点到位后回填（跨角色刻度不一致——崩拳 4 vs 碎冰葬 40——落表前归一 pass）。
- 「执行卡恒 0E」新口径 vs 现行表 Execution 卡 Energy>0：录入前统一。
- 蓄力子型字段映射（Energy / ExecutingCost / 蓄力计数器）待定案。
- 艾琳终局模拟按「每轮 1 张执行卡」重跑后回调 ExecutingCost。
- 本设计与现行 Zhouzhou001–020 / Irene001–023 两套卡池的替换/并存策略待定。

> **源文档**：`【双角色·百相卡牌与BD思路】.md`（GameDesigner, 2026-07-13）← `【周周角色设计优化】定稿.md` + `【艾琳角色设计优化】.md`
