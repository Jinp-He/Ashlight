# 战斗系统（方案3）

> **参考原型**：杀戮尖塔（能量+卡牌）× 敌人意图/执行轴
> **状态**：主推方案

---

## 概述

战斗由 **ATB 行动条** 驱动回合顺序。玩家侧采用类杀戮尖塔的能量+多张手牌机制，敌人侧保留意图轴/执行轴制造战术压力窗口。

**一句话理解**：玩家回合内自由出牌（全场暂停），敌人行动通过可见的意图轴给予反应机会。

```
玩家侧（回合制手感）         敌人侧（意图轴 + 执行轴）
─────────────────           ──────────────────────
ATB 推进 → 触发玩家回合      敌人 ATB 推进 → 到达行动点
全场暂停                     → 公示意图，进入【意图轴】
摸牌 + 获得能量 → 出牌        意图轴倒计时 → 玩家反应窗口
即时牌立刻结算               意图轴结束 → 进入【执行轴】
结束回合 → 【执行】牌触发     执行轴结束 → 敌人效果生效
全场恢复                     复位回 ATB 起点
```

---

## 一、ATB 行动条

### 行动条结构

所有单位共享一条 ATB 行动条：

- **内部实现**：0–100 段（`ActionBarState.MaxSegment = 100`）
- **UI 展示**：7 格（纯显示层映射）
- 每个 tick，所有存活单位的行动条按各自 `Speed` 值推进
- 当行动条达到 100 时，该单位获得行动权

### Speed 与回合频率

```
Speed 3（猎人）→ 推进最快，行动最频繁
Speed 4（法师）→ 中速
Speed 5（战士）→ 推进最慢，但每回合能量最多
```

### 同时到达处理

多个单位同时到达 100 时，优先级：Speed 更高者先行动 → 玩家单位优先于敌人。

### 回合结束后复位

行动结束后，单位行动条复位至 `RestartSegment`（默认 0，可能受过载债务影响）。

---

## 二、全场暂停机制

### 核心规则

当 **玩家角色** 到达行动点时，**全场暂停**：

- 所有其他单位 ATB 停止推进
- 所有敌人的意图轴 / 执行轴倒计时冻结
- 硬直计时器冻结

暂停持续到玩家手动结束回合。

### 实现

- `BattleStateSnapshot.IsGlobalPaused = true`
- `ActionBarResolver.AdvanceUntilAction()` 在暂停时直接返回 null
- 敌人轴推进（`EnemyIntentAxisResolver`）检查暂停标志后跳过

> **设计意图**：战术压力来源于出牌选择本身，而非操作速度。

---

## 三、能量系统

### 每回合能量

每个角色行动时，获得固定能量，用于支付本回合打出的卡牌。

| 角色 | Speed | 每回合能量 | 设计意图 |
|------|-------|----------|---------|
| 猎人 Zhouzhou | 3 | **3** | 行动频繁，单回合少量，靠高频循环 |
| 法师 Irene | 4 | **4** | 中速中能量，稳定治疗+辅助 |
| 战士 Rocket | 5 | **5** | 行动最慢，单回合最多，重型技能组合 |

**核心公式：能量 = Speed**（配置在 `CharaterInfo.xlsx` 的 `Energy` 字段）

### 能量规则

- 每回合 **重置**，不累计到下一回合
- 卡牌费用从 `CardInfo.Energy` 读取（每张牌 1–3 不等）
- 能量不足时无法打出该牌
- 回合内未使用的能量丢弃

---

## 四、手牌系统

### 摸牌

每回合开始时，角色从自己的技能牌组中摸牌（数量由 `CharaterInfo.Draw` 决定）。

### 打牌

- 一回合内可打出 **任意数量** 的牌，只要能量足够
- 牌的打出顺序影响效果结算
- 回合结束时，未打出的手牌弃掉

### 牌组循环

- 牌组打完后自动洗牌重抽（类杀戮尖塔）
- 每角色独立牌组（`CardInfo.BelongTo` 区分归属）
- 支持 Hand / DrawPile / DiscardPile / InPlayPile / RemovedPile 五种状态

---

## 五、卡牌类型

仅两种卡牌类型：

### 即时牌（CardType = Normal）

打出后 **立刻** 结算效果。这是大多数牌的形式。

```
猎人"飞刀"（1能量）→ 打出 → 立刻造成伤害+叠标记
法师"治愈之光"（1能量）→ 打出 → 立刻恢复目标HP
战士"嘲讽"（2能量）→ 打出 → 立刻强制敌人改变目标
```

### 【执行】牌（CardType = Execution）

打出后效果 **不立刻生效**，等玩家结束回合时统一触发。

```
战士"破甲猛击"【执行】（3能量）→ 打出 → 等待
                                ↓
                      玩家结束回合
                                ↓
                      破甲猛击效果触发
```

特征：
- 效果比同费用即时牌更强，代价是延迟触发
- 若角色在结算前被击倒，【执行】牌取消
- 建议每角色不超过 1–2 张【执行】牌

---

## 六、敌人系统（意图轴 + 执行轴）

### 完整行动流程

```
[ATB 推进] ──────────→ [行动点（100段）]
                        │
                   公示意图（图标+数值立刻可见）
                        │
                   进入【意图轴】
                        │  ← 玩家反应窗口
                        │  ← 全场暂停时此处冻结
                        ↓
                   意图轴结束 → 进入【执行轴】
                        │  ← 无法打断，仅可延后/硬直
                        ↓
                   执行轴结束 → 效果生效
                        │
                   复位回 ATB 起点
```

### 意图轴（Intent Axis）

- 敌人到达行动点后进入意图轴
- **公示意图**：显示意图图标+预计效果数值
- **倒计时可见**：剩余格数对玩家可见
- **意图不变**：进入后不再改变（除非被打断）
- **可被干预**：延后、硬直、打断均可

意图轴长度（暂用 `EnemySkillInfo.ExecutingCost` 字段）：

| 意图轴长度 | 反应窗口 | 典型用途 |
|----------|---------|---------|
| 1 格 | 极短 | 快速普攻，几乎无法反应 |
| 2 格 | 短 | 标准攻击，需提前预判 |
| 3 格 | 中 | 强力技能，有明确反应机会 |
| 4 格 | 长 | 大招/全体技能，充分反应时间 |

### 执行轴（Execute Axis）

- 意图轴结束后进入，通常极短（默认 1 格）
- 进入后 **无法被打断**，只能被延后或硬直
- 执行轴结束 → 效果立刻生效

### 数据追踪（UnitState 字段）

```
CurrentPhase: EnemyPhase (None / IntentAxis / ExecuteAxis)
IntentAxisLength / IntentAxisProgress
ExecuteAxisLength / ExecuteAxisProgress
PendingSkillId / PendingTargetId
IsStunned / StunRemainingTicks
```

---

## 七、干预手段

| 干预手段 | 时机 | 效果 | 实现 |
|---------|------|------|------|
| **延后** | 意图轴 / 执行轴均可 | 轴进度后移 N 格 | `ActionBarShiftCommand`（负值作用于 IntentAxisProgress）|
| **硬直** | 意图轴 / 执行轴均可 | 冻结敌人，停止所有推进 N tick | `StunCommand` |
| **打断** | **仅意图轴** | 取消当前意图，敌人复位重来 | `InterruptCommand` |

> 打断是稀有且高代价的操作，不应常态化。大多数情况下玩家面对的选择是"延后还是承受"。

---

## 八、条件触发系统

卡牌可根据目标状态触发追加效果（`AttackConditionalCommand`）：

| 条件 | 含义 |
|------|------|
| `EnemyInIntentAxis` | 目标处于意图轴中 |
| `EnemyInExecuteAxis` | 目标处于执行轴中 |
| `IsStunned` | 目标处于硬直状态 |
| `IsDefending` / `HasShield` | 目标有护盾 |
| `LowHp` | 目标 HP < 50% |
| `HighHp` | 目标 HP > 80% |

---

## 九、过载系统（保留）

过载提供额外能量，代价是行动条复位位置惩罚：

| 过载等级 | 债务（段） | 效果 |
|---------|----------|------|
| Light | 5 | 下回合 RestartSegment -= 5 |
| Medium | 12 | 下回合 RestartSegment -= 12 |
| Heavy | 25 | 下回合 RestartSegment -= 25 |

债务在下回合消耗并清除。详见 `OverloadState`。

---

## 十、完整回合流程

```
1. ATB 推进，某角色到达行动点
         │
    ┌────┴────┐
  玩家角色    敌人单位
    │             │
2. 全场暂停      进入意图轴
   摸牌           公示意图
   获得能量       意图轴倒计时
    │             │
3. 出牌阶段      意图轴结束
   即时牌结算     进入执行轴
   执行牌入队     │
    │            执行轴结束
4. 手动结束回合   效果生效
   执行牌触发     │
   弃手牌        复位 ATB
   复位 ATB
    │
5. 全场恢复
   下一单位行动
```

---

## 十一、核心文件索引

| 模块 | 文件 |
|------|------|
| 战斗管理 | `Scripts/Battle/BattleManager.cs` |
| 回合解析 | `Scripts/Battle/Core/Engine/TurnResolver.cs` |
| ATB推进 | `Scripts/Battle/Core/Engine/ActionBarResolver.cs` |
| 卡牌结算 | `Scripts/Battle/Core/Engine/CardPlayResolver.cs` |
| 敌人轴 | `Scripts/Battle/Core/Engine/EnemyIntentAxisResolver.cs` |
| 单位状态 | `Scripts/Battle/Core/Data/UnitState.cs` |
| 行动条 | `Scripts/Battle/Core/Data/ActionBarState.cs` |
| 战斗快照 | `Scripts/Battle/Core/Data/BattleStateSnapshot.cs` |
| 敌人阶段 | `Scripts/Battle/Core/Data/EnemyPhase.cs` |
| 过载 | `Scripts/Battle/Core/Data/OverloadState.cs` |
| 牌组 | `Scripts/Battle/Core/Data/BattleDeckSystem.cs` |
| 命令 | `Scripts/Battle/Core/Commands/` |
| 预测 | `Scripts/Battle/Core/Engine/BattlePredictor.cs` |

---

*方案3：能量+多张手牌 × 即时/【执行】双类型 × 仅敌人保留意图轴/执行轴 × 玩家回合全场暂停 × 过载系统保留。*
