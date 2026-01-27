# 时间轴战斗系统 (Timeline Battle System)

## 概述

这是一个基于时间轴的确定性战斗系统，支持**真实战斗演算**和**行动结果预测**。

### 核心特性

- ✅ **纯数据驱动**：战斗逻辑与UI完全分离
- ✅ **无状态设计**：核心算法类不存储状态，只做 Input State → Output State 转换
- ✅ **POCO架构**：所有核心数据类不继承MonoBehaviour
- ✅ **确定性计算**：相同输入必然产生相同输出
- ✅ **深拷贝支持**：所有状态类支持高效深拷贝，用于预测系统
- ✅ **NvN架构**：支持多个玩家角色对战多个敌人

## 架构设计

```
Core/
├── Data/              # 核心数据结构
│   ├── PhaseEnum.cs           # 阶段枚举（Startup/Active/Recoil）
│   ├── TimelineBlock.cs       # 时间轴最小单元
│   ├── TimelineTrack.cs       # 单位时间轴（15格）
│   ├── BuffState.cs           # Buff状态
│   ├── UnitState.cs           # 战斗单位状态
│   └── BattleStateSnapshot.cs # 战场完整快照
│
├── Commands/          # 指令系统
│   ├── ICommand.cs            # 指令接口
│   ├── DamageCommand.cs       # 伤害指令
│   ├── DefenseCommand.cs      # 防御指令
│   ├── HealCommand.cs         # 治疗指令
│   ├── TimeShiftCommand.cs    # 时间推迟指令
│   └── BuffCommand.cs         # Buff指令
│
├── Engine/            # 核心引擎
│   ├── TimelineResolver.cs        # 时间轴解算器
│   ├── CardToTimelineConverter.cs # 卡牌转换器
│   ├── BattlePredictor.cs         # 战斗预测器
│   └── PredictionResult.cs        # 预测结果
│
└── Utils/             # 工具类
    └── PriorityComparer.cs    # 优先级比较器
```

## 核心概念

### 1. TimelineTrack（时间轴）

每个战斗单位拥有独立的时间轴，固定长度为 **15格**。

```csharp
TimelineTrack track = new TimelineTrack();
track.SetBlock(0, block);          // 设置指定位置的Block
track.ShiftBlocks(5, 2);           // 从索引5开始向后推迟2格
```

### 2. TimelineBlock（时间块）

时间轴上的最小单元，包含：

- **Phase**：阶段（Startup前摇 / Active生效 / Recoil僵直）
- **Commands**：指令列表（仅在Active阶段执行）
- **Priority**：优先级（用于排序）

### 3. 优先级系统

同一时间格的多个Block按优先级顺序执行：

| 优先级 | 类型 | 数值 |
|--------|------|------|
| 最高 | Defense/Intercept | 100 |
| 高 | Swift | 90 |
| 中高 | Attack | 80 |
| 中 | Heal | 70 |
| 中低 | TimeShift | 60 |
| 低 | Buff | 50 |
| 最低 | Others | 0 |

### 4. Effect → Command 映射

卡牌的Effect会被转换为对应的Command：

- `AttackEffect` → `DamageCommand`
- `DefenseEffect` → `DefenseCommand`
- `HealEffect` → `HealCommand`
- `PushCollisionEffect` → `TimeShiftCommand`
- `BuffEffect` → `BuffCommand`

## 使用示例

### 1. 真实战斗演算

```csharp
// 创建战场状态
var battleState = new BattleStateSnapshot();
battleState.PlayerUnits.Add(new UnitState { ... });
battleState.EnemyUnits.Add(new UnitState { ... });

// 使用TimelineResolver解算
var resolver = new TimelineResolver();
resolver.ResolveStep(battleState, 0);  // 解算第0格
// 或
resolver.ResolveFullTimeline(battleState); // 解算完整时间轴
```

### 2. 行动结果预测

```csharp
// 加载卡牌配置
ConfigLoader.Load();
var card = ConfigLoader.Tables.TbCardInfo.Get("W001");

// 使用BattlePredictor预测
var predictor = new BattlePredictor();
var result = predictor.Simulate(
    currentState,  // 当前状态（不会被修改）
    card,          // 卡牌配置
    "player_1",    // 使用者ID
    "enemy_1",     // 目标ID
    0              // 插入到时间轴第0格
);

// 查看预测结果
Debug.Log(result.GetSummary());
foreach (var kvp in result.HpChangeMap)
{
    Debug.Log($"{kvp.Key}: HP变化 {kvp.Value}");
}
```

### 3. 深拷贝验证

```csharp
// 创建原始状态
var originalState = CreateBattleState();

// 深拷贝
var clonedState = originalState.Clone();

// 修改拷贝不会影响原始状态
clonedState.PlayerUnits[0].CurrentHp = 0;
// originalState.PlayerUnits[0].CurrentHp 保持不变
```

## 测试

在Unity Editor中，找到 `BattleSystemExample.cs` 组件，右键点击：

- **运行完整战斗示例**：测试BattlePredictor
- **运行时间轴解算示例**：测试TimelineResolver
- **测试深拷贝功能**：验证深拷贝正确性

## 验收标准

✅ **预测准确性**：`Simulate()` 的结果必须与真实执行完全一致  
✅ **无副作用**：调用 `Simulate()` 绝不修改 `currentState`  
✅ **深拷贝正确性**：修改虚拟状态不影响真实状态  
✅ **优先级正确性**：同一时间格多个Block按优先级顺序执行  
✅ **POCO原则**：所有核心类不继承MonoBehaviour  

## 扩展点

- [ ] 完整Buff系统（持续时间、叠加、数值修正）
- [ ] 完整Effect支持（Taunt、Intercept、Swift等）
- [ ] 动画播放接口（通过事件通知UI层）
- [ ] AI决策系统（基于Simulate选择最优卡牌）
- [ ] 战斗重放系统
- [ ] 联机对战支持

## 技术细节

### 回合末处理

每个时间格解算完成后：

1. 清空所有单位的护甲值
2. 更新Buff状态（减少持续时间，移除过期Buff）
3. 检查战斗是否结束

### 伤害计算

```csharp
实际伤害 = Max(0, 攻击伤害 - 护甲值)
剩余护甲 = Max(0, 护甲值 - 攻击伤害)
```

### 时间轴位移

```csharp
track.ShiftBlocks(startIndex, shiftAmount);
// 正数：向后推迟（推迟敌人行动）
// 负数：向前拉（加快己方行动）
```

## 注意事项

1. **配置表依赖**：使用前必须调用 `ConfigLoader.Load()`
2. **时间轴长度**：固定15格，超出部分会被丢弃
3. **死亡单位**：死亡单位的Block不会被执行
4. **目标检查**：Commands执行前会检查目标是否存在且存活

## 许可证

MIT License

