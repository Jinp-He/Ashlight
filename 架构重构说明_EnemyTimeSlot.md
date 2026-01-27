# EnemyTimeSlot 架构重构说明

## 问题描述

原来的架构存在以下问题：
1. UI 层需要从 Battle 层的 TimelineBlock 数据反向推导出 EnemyTimeSlot
2. 职责混乱：BattleManager 管理数据，UI 层又要解析这些数据
3. 数据流不清晰：先写数据，后读数据创建 UI

## 重构方案

采用**事件驱动 + 正向构建**的架构：

### 新的数据流

```
BattleManager.StartPlayerTurn()
    ↓
选择敌人意图 (EnemyIntention)
    ↓
发布事件: EnemyIntentionSelectedEvent
    ↓
UI_BattleScene 订阅事件
    ↓
调用 TimelineTrackView.PlaceEnemyTimeSlot()
    ↓
1. 创建 EnemyTimeSlot UI
2. 放置到指定位置
3. 标记时间轴格子为占用
```

### 架构优势

1. ✅ **单向数据流**：Battle → Event → UI
2. ✅ **职责分离**：BattleManager 只负责战斗逻辑，UI 只负责显示
3. ✅ **正向构建**：从意图直接创建 UI，而不是从数据反推
4. ✅ **解耦**：Battle 层和 UI 层通过事件解耦

## 修改的文件

### 1. GameEvents.cs
添加了新事件：
```csharp
public struct EnemyIntentionSelectedEvent
{
    public string EnemyUnitId;          // 敌人单位ID
    public EnemySkillInfo SkillInfo;    // 技能信息
    public int TimeSlotPosition;        // 时间槽位置
}
```

### 2. BattleManager.cs
在选择敌人意图后发布事件：
```csharp
GameEvent.Publish(new EnemyIntentionSelectedEvent
{
    EnemyUnitId = enemyUnit.UnitId,
    SkillInfo = skillInfo,
    TimeSlotPosition = timeSlot
});
```

### 3. TimelineTrackView.cs
添加了正向构建方法：
```csharp
public void PlaceEnemyTimeSlot(EnemySkillInfo skillInfo, int slotIndex)
{
    // 1. 创建 EnemyTimeSlot UI
    // 2. 初始化并显示
    // 3. 设置位置
    // 4. 标记时间轴格子为占用
}
```

移除了 `RefreshDisplay()` 中的自动创建逻辑（改为只清空）。

### 4. UI_BattleScene.Logic.cs
订阅并处理事件：
```csharp
private void OnEnemyIntentionSelected(EnemyIntentionSelectedEvent evt)
{
    _enemyTimeline.PlaceEnemyTimeSlot(evt.SkillInfo, evt.TimeSlotPosition);
}
```

## 运行流程

1. **游戏启动**：UI_BattleScene 订阅 `EnemyIntentionSelectedEvent`
2. **回合开始**：BattleManager.StartPlayerTurn() 被调用
3. **选择意图**：每个敌人随机选择技能和时间槽位置
4. **发布事件**：BattleManager 发布 EnemyIntentionSelectedEvent
5. **创建 UI**：UI_BattleScene 接收事件，调用 PlaceEnemyTimeSlot()
6. **显示结果**：EnemyTimeSlot 显示在时间轴上

## 与旧代码的兼容性

- ✅ BattleManager 仍然会将技能转换为 TimelineBlock 并插入到 SharedEnemyTrack（保持战斗逻辑不变）
- ✅ UI 层不再依赖 TimelineBlock 数据来创建 EnemyTimeSlot
- ✅ 两套逻辑并行运行，互不影响

## 未来改进

1. 可以移除旧的 `CreateEnemyTimeSlots()` 方法（已被替代）
2. 可以考虑将 TimelineBlock 的创建也改为事件驱动
3. 可以添加 EnemyTimeSlot 的点击、拖拽等交互功能

## 测试建议

1. 启动游戏，进入战斗场景
2. 观察 Console 日志，确认事件发布和接收
3. 检查敌人时间轴上是否正确显示 EnemyTimeSlot
4. 测试多个回合，确认清空和重新创建正常工作
