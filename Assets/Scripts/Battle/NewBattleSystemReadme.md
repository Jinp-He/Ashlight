# ATB战斗系统 (ATB Battle System)

## 概述

这是一个基于 **行动条（ATB, Active Time Battle）** 的确定性战斗系统。

系统核心战斗流程为：

- 每个战斗单位根据自身 **速度（Speed）** 推进行动条
- 当行动条到达终点时，该单位获得一次行动回合
- 单位在自己的回合中消耗能量打出卡牌
- 当能量不足时，可通过 **过载（Overload）** 继续出牌，以牺牲未来节奏为代价换取当前爆发
- 回合结束后，单位根据自身状态、速度和过载负债，重新进入下一轮行动条循环

---

## 核心特性

- ✅ **ATB核心循环**：战斗由行动条推进与单位回合构成
- ✅ **纯数据驱动**：战斗逻辑与UI完全分离
- ✅ **无状态设计**：核心算法类不存储状态，只做 Input State → Output State 转换
- ✅ **POCO架构**：所有核心数据类不继承 MonoBehaviour
- ✅ **确定性计算**：相同输入必然产生相同输出
- ✅ **深拷贝支持**：所有状态类支持高效深拷贝，用于预测系统
- ✅ **NvN架构**：支持多个玩家角色对战多个敌人
- ✅ **预测系统辅助**：支持敌方伤害预览、卡牌拖动预览、目标选择预览等功能

---

## 系统定位

本项目包含两个层次：

### 1. 核心战斗系统
负责真实战斗执行，包括：

- 行动条推进
- 单位获得回合
- 能量与抽牌刷新
- 卡牌结算
- 过载处理
- Buff / Debuff 更新
- 战斗结束判定

### 2. 预测系统
负责对当前战斗状态进行**局部模拟**，用于：

- 预测敌人下一次行动可能造成的伤害
- 在卡牌拖动状态下预览效果
- 预览目标选择后的结果
- 为AI提供局部演算支持

> 预测系统不是战斗主循环本身，而是建立在真实战斗规则之上的辅助演算模块。

---

## 架构设计

```text
Core/
├── Data/                     # 核心数据结构
│   ├── ActionBarState.cs            # 单位行动条状态
│   ├── OverloadState.cs             # 过载状态
│   ├── BuffState.cs                 # Buff状态
│   ├── UnitState.cs                 # 战斗单位状态
│   └── BattleStateSnapshot.cs       # 战场完整快照
│
├── Commands/                 # 指令系统
│   ├── ICommand.cs                   # 指令接口
│   ├── DamageCommand.cs              # 伤害指令
│   ├── DefenseCommand.cs             # 防御指令
│   ├── HealCommand.cs                # 治疗指令
│   ├── ActionBarShiftCommand.cs      # 行动条位移指令
│   ├── OverloadCommand.cs            # 过载指令
│   └── BuffCommand.cs                # Buff指令
│
├── Engine/                   # 核心引擎
│   ├── ActionBarResolver.cs          # 行动条推进与下一行动者计算
│   ├── TurnResolver.cs               # 单位回合解算器
│   ├── CardPlayResolver.cs           # 卡牌结算器
│   ├── BattlePredictor.cs            # 预测器（辅助系统）
│   └── PredictionResult.cs           # 预测结果
│
└── Utils/                    # 工具类
    ├── DeepCloneHelper.cs            # 深拷贝工具
    └── UnitQueryHelper.cs            # 单位查询工具


核心概念
1. ActionBarState（行动条状态）

每个战斗单位拥有独立的行动条状态。

行动条的关键属性包括：

CurrentProgress：当前行动条进度

MaxProgress：达到该值后即可获得回合

RestartProgress：本次回合结束后，下次进入行动条时的起始位置

Speed：单位速度，决定行动条推进效率与回合节奏

IsOverloaded：是否处于过载状态

OverloadDebt：过载负债值，会影响下次起跑位置与风险

示例：

var actionBar = new ActionBarState
{
    CurrentProgress = 72,
    MaxProgress = 100,
    RestartProgress = 45,
    Speed = 18,
    IsOverloaded = false,
    OverloadDebt = 0
};
2. UnitState（单位状态）

每个单位包含完整的战斗状态：

基础属性

HP / MaxHP

Speed

BaseEnergy

BaseDrawCount

行动条相关

ActionBar

RestartProgress

OverloadDebt

回合资源

CurrentEnergy

CurrentHand

CurrentBlock / Armor

状态效果

Buffs

Vulnerable / Weak / Shield / 等扩展状态

3. 速度（Speed）

速度决定单位在战斗中的节奏质量，主要影响：

行动条推进效率

回合结束后的重新起跑位置

过载恢复效率

速度可以弱影响资源，但不建议同时强绑定：

行动频率

能量数

抽牌数

以避免速度成为万能属性。

4. 回合（Turn）

当单位的行动条到达终点时，该单位进入自己的回合。

一个标准回合通常包括：

结算回合开始效果

刷新当前回合能量

抽取本回合手牌

玩家或AI打出卡牌

若能量不足，可选择进入过载继续出牌

结算所有指令与状态变化

计算新的 RestartProgress

重新进入行动条循环

5. 过载（Overload）

过载允许单位在当前能量耗尽后，继续出牌。

其本质是：

透支未来节奏，换取当前回合的额外操作空间

过载影响

当前回合继续打牌

增加 OverloadDebt

使单位下次行动更慢到来

提高单位的脆弱性

可与特定卡牌、Buff或角色机制形成联动

预测系统
设计目标

预测系统用于对当前战斗状态做局部、短期、无副作用模拟。

它不负责驱动真实战斗循环，而是为以下场景提供支持：

敌方伤害预测

例如显示“该敌人下次行动将造成 12 点伤害”

卡牌拖动预览

玩家拖动卡牌到目标身上时，预览可能造成的伤害、护甲变化、Buff变化等

目标切换预览

不同目标下结算结果不同，可在UI上即时反馈

AI辅助评估

在多个候选动作中进行局部比较

PredictionResult（预测结果）

预测结果通常包括：

HpChangeMap：各单位生命变化

BlockChangeMap：各单位护甲变化

BuffChangeMap：各单位Buff变化

WillKillTargets：是否会击杀目标

ExpectedDamage：预计伤害值

ExpectedOverloadDebt：若本次行动涉及过载，预估负债变化

Summary：供UI快速展示的摘要文本

Effect → Command 映射

卡牌的 Effect 会被转换为对应的 Command：

AttackEffect → DamageCommand

DefenseEffect → DefenseCommand

HealEffect → HealCommand

BuffEffect → BuffCommand

ActionBarShiftEffect → ActionBarShiftCommand

OverloadEffect → OverloadCommand