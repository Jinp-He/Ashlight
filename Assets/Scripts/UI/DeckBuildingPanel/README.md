# DeckBuildingPanel 卡组构建面板

## 文件结构

```
DeckBuildingPanel/
├── ChooseCardPanel.Logic.cs    - 卡牌选择面板业务逻辑
└── CardLibrary.Logic.cs         - 卡牌库业务逻辑
```

## ChooseCardPanel 组件说明

### UI绑定（自动生成）

ChooseCardPanel包含以下UI组件：

| 组件名称 | 类型 | 用途 |
|---------|------|------|
| `CardLibrary` | RectTransform | 卡牌库容器 |
| `Txt_Statistics` | TextMeshProUGUI | 统计信息显示（如：卡组数量） |
| `CardDeckContainer` | RectTransform | 已选卡组容器 |
| `Img_TrashCanBas` | Image | 垃圾桶基础图片（删除卡牌区域） |
| `Img_Frame` | Image | 面板框架装饰 |
| `Img_PointerIcon` | Image | 指针/光标图标 |
| `Btn_Start` | Button | 开始游戏按钮 |

### 当前状态

**ChooseCardPanel.Logic.cs** - 已创建基础结构，暂未实现具体功能

- ✅ 基础架构完成
- ✅ UI绑定初始化
- ✅ 生命周期方法预留
- ⏳ 业务逻辑待实现

### 预期功能（待实现）

#### 1. 卡牌库管理
- 显示所有可用卡牌
- 卡牌筛选和搜索
- 卡牌预览

#### 2. 卡组构建
- 从卡牌库选择卡牌添加到卡组
- 从卡组移除卡牌（拖拽到垃圾桶）
- 卡组数量限制验证
- 卡组统计信息显示

#### 3. 交互功能
- 卡牌拖拽（从库到组，从组到垃圾桶）
- 卡牌点击选择
- 开始游戏按钮交互
- 卡组保存和加载

#### 4. UI反馈
- 统计信息实时更新
- 拖拽高亮显示
- 非法操作提示

## CardLibrary 组件说明

### 功能（已实现）

CardLibrary负责显示所有可用的卡牌：

- ✅ 从ConfigLoader加载所有CardInfo
- ✅ 使用CardViewController预制体实例化卡牌
- ✅ 卡牌搜索功能
- ✅ 阅览/战斗模式切换

详见：`CardLibrary.Logic.cs`

## 使用示例（未来）

### 初始化面板

```csharp
public class GameManager : MonoBehaviour
{
    public ChooseCardPanel chooseCardPanel;
    
    private void Start()
    {
        // 初始化卡牌选择面板
        chooseCardPanel.InitializePanel();
    }
}
```

### 订阅事件（计划）

```csharp
using Ashlight.Common.Events;

private void OnEnable()
{
    // 订阅卡牌选择事件
    GameEvent.Subscribe<CardSelectedEvent>(OnCardSelected);
    GameEvent.Subscribe<CardAddedToDeckEvent>(OnCardAddedToDeck);
    GameEvent.Subscribe<CardRemovedFromDeckEvent>(OnCardRemovedFromDeck);
}

private void OnDisable()
{
    // 取消订阅
    GameEvent.Unsubscribe<CardSelectedEvent>(OnCardSelected);
    GameEvent.Unsubscribe<CardAddedToDeckEvent>(OnCardAddedToDeck);
    GameEvent.Unsubscribe<CardRemovedFromDeckEvent>(OnCardRemovedFromDeck);
}
```

## 开发计划

### 第一阶段：基础功能
- [ ] 实现卡组数据管理
- [ ] 实现添加/移除卡牌逻辑
- [ ] 实现统计信息更新
- [ ] 实现开始按钮功能

### 第二阶段：交互优化
- [ ] 实现卡牌拖拽
- [ ] 实现垃圾桶高亮
- [ ] 实现卡组数量限制
- [ ] 实现卡牌筛选

### 第三阶段：数据持久化
- [ ] 实现卡组保存
- [ ] 实现卡组加载
- [ ] 实现默认卡组

### 第四阶段：UI优化
- [ ] 添加动画效果
- [ ] 添加音效
- [ ] 添加提示信息
- [ ] 优化性能

## 架构说明

### 面板职责

```
ChooseCardPanel
    ├── 管理卡组数据
    ├── 协调CardLibrary和CardDeck
    ├── 处理用户交互
    └── 更新统计信息

CardLibrary (子组件)
    ├── 显示所有可用卡牌
    ├── 提供搜索功能
    └── 响应卡牌选择

CardDeckContainer (UI容器)
    ├── 显示已选卡组
    ├── 支持卡牌排序
    └── 支持卡牌移除
```

### 数据流

```
ConfigLoader → CardInfo数据
    ↓
CardLibrary → 显示卡牌列表
    ↓
用户选择卡牌 → CardSelectedEvent
    ↓
ChooseCardPanel → 添加到卡组
    ↓
CardDeckContainer → 更新显示
    ↓
Txt_Statistics → 更新统计
```

## 注意事项

1. **命名空间**：使用`_Scripts.UI`而非`Scripts.UI`
2. **UI绑定**：所有UI组件通过自动生成的代码绑定
3. **事件系统**：使用GameEvent进行解耦通信
4. **卡牌预制体**：复用CardViewController组件
5. **数据验证**：添加卡牌时需要验证卡组规则

## 相关文件

- `Assets/Scripts/UI/Generated/ChooseCardPanel.cs` - 自动生成的UI绑定
- `Assets/Scripts/UI/DeckBuildingPanel/CardLibrary.Logic.cs` - 卡牌库逻辑
- `Assets/Scripts/UI/Card/CardViewController.Logic.cs` - 卡牌视图控制器
- `Assets/Scripts/Common/Events/GameEvent.cs` - 事件系统

## 扩展资源

- [GameEvent 使用指南](../Common/Events/README.md)
- [CardViewController 使用说明](../Card/README.md)
- [CardDescriptionParser 说明](../Card/CardDescriptionParser.cs)

