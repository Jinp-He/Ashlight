# Scripts 四层架构指南

本文档说明 `Assets/Scripts/` 文件夹的架构设计和文件归属规则。

---

## 📐 架构概览

项目采用 **四层架构** 设计，确保代码职责清晰、易于维护：

```
Assets/Scripts/
├── Config/          📊 数据层 - 配置数据访问
├── State/           💾 状态层 - 运行时状态 & 存档
├── Systems/         ⚙️ 系统层 - 游戏逻辑（注意是复数，避免与 .NET System 冲突）
├── View/            🖼️ 表现层 - UI & 视觉
├── Common/          🔧 通用模块
├── UIBind/          🔗 UI绑定工具
└── ThirdParty/      📦 第三方插件
```

---

## 📊 Config/ - 数据层

### 职责
- 读取 Luban 生成的配置数据
- 提供**只读**访问接口

### 可以做
- 加载配置表
- 提供配置查询接口
- 封装配置访问逻辑

### 禁止做
- ❌ 存储运行时状态
- ❌ 修改配置数据
- ❌ 包含业务逻辑

### 示例文件
```
Config/
├── ConfigLoader.cs      # 配置加载器
├── ItemConfigAccess.cs  # 物品配置访问
└── BuildingConfig.cs    # 建筑配置访问
```

---

## 💾 State/ - 状态层

### 职责
- 记录游戏运行时的可变状态
- 支持 Save / Load 存档
- 使用纯 C# 数据结构（无 MonoBehaviour）

### 子目录

| 目录 | 用途 |
|------|------|
| `Runtime/` | 运行时状态数据 |
| `Save/` | 存档相关数据结构 |

### 可以做
- 定义状态数据类
- 标记 `[Serializable]` 支持序列化
- 提供默认值工厂方法

### 禁止做
- ❌ 包含业务逻辑
- ❌ 直接操作 UI
- ❌ 使用 MonoBehaviour

### 示例文件
```
State/
├── Runtime/
│   ├── PlayerState.cs       # 玩家状态
│   ├── InventoryState.cs    # 背包状态
│   └── BuildingState.cs     # 建筑状态
└── Save/
    ├── SaveData.cs          # 存档数据结构
    └── SaveSerializer.cs    # 序列化工具
```

---

## ⚙️ Systems/ - 系统层

> **注意**：使用复数 `Systems` 而非 `System`，以避免与 .NET 的 `System` 命名空间冲突。

### 职责
- 驱动游戏规则
- 更新状态数据
- 处理系统级逻辑
- **一个 Manager/System 只负责一类规则**

### 子目录

| 目录 | 用途 |
|------|------|
| `Core/` | 核心系统（GameManager、SaveManager） |
| `Battle/` | 战斗系统 |
| `Economy/` | 经济系统 |
| `Adventure/` | 冒险系统 |
| `Building/` | 建筑系统 |

### 可以做
- 读取 Config 配置
- 修改 State 状态
- 通过事件通知其他系统
- 提供公共 API 供调用

### 禁止做
- ❌ 直接操作 UI 组件
- ❌ 存储配置数据
- ❌ 跨系统直接修改状态

### 示例文件
```
Systems/
├── Core/
│   ├── GameManager.cs      # 游戏主循环
│   └── SaveManager.cs      # 存档管理
├── Economy/
│   └── EconomySystem.cs    # 经济系统
└── Battle/
    └── BattleSystem.cs     # 战斗系统
```

---

## 🖼️ View/ - 表现层

### 职责
- 展示状态数据
- 接收玩家输入
- 发送指令给 System 层

### 子目录

| 目录 | 用途 |
|------|------|
| `UI/Panels/` | UI 面板 |
| `UI/HUD/` | HUD 元素 |
| `Component/` | 视觉组件（角色、建筑等） |

### 可以做
- 读取 State 状态并显示
- 监听事件刷新显示
- 调用 System 的公共 API
- 处理用户输入

### 禁止做
- ❌ 直接修改 State 数据
- ❌ 包含业务规则逻辑
- ❌ 直接读取 Config（应通过 System）

### 示例文件
```
View/
├── Component/
│   ├── ViewBase.cs         # 视图基类
│   ├── Character.cs        # 角色视图
│   └── BuildingView.cs     # 建筑视图
└── UI/
    ├── Panels/
    │   ├── InventoryPanel.cs   # 背包面板
    │   └── ShopPanel.cs        # 商店面板
    └── HUD/
        ├── GoldDisplay.cs      # 金币显示
        └── HealthBar.cs        # 血条
```

---

## 🔧 Common/ - 通用模块

### 职责
- 提供跨模块通用功能
- 工具类和扩展方法
- 事件系统

### 子目录

| 目录 | 用途 |
|------|------|
| `Events/` | 事件总线、事件定义 |
| `Utils/` | 工具函数 |
| `Extension/` | C# 扩展方法 |

### 示例文件
```
Common/
├── Events/
│   ├── GameEvent.cs        # 事件总线
│   └── PlayerEvents.cs     # 玩家相关事件定义
├── Utils/
│   ├── MathHelper.cs       # 数学工具
│   └── TimeHelper.cs       # 时间工具
└── Extension/
    ├── ListExtensions.cs   # List 扩展
    └── StringExtensions.cs # String 扩展
```

---

## 🎯 文件归属决策流程

创建新文件时，问自己：

```
这个类的主要职责是什么？
         │
         ▼
┌─────────────────────────────────────────────────┐
│                                                 │
│  它只是"读取配置数据"吗？ → Config/            │
│                                                 │
│  它是"运行时会变化的状态"吗？ → State/         │
│                                                 │
│  它负责"处理规则、驱动逻辑"吗？ → Systems/     │
│                                                 │
│  它负责"显示画面、接收输入"吗？ → View/        │
│                                                 │
│  它是"到处都可能用到的工具"吗？ → Common/      │
│                                                 │
└─────────────────────────────────────────────────┘
```

---

## 📋 快速判断表

| 如果你要创建的是... | 放在 | 示例 |
|-------------------|------|------|
| 配置数据的访问类 | `Config/` | `ItemConfigAccess.cs` |
| 纯数据类（无MonoBehaviour）| `State/Runtime/` | `InventoryState.cs` |
| 存档相关的数据结构 | `State/Save/` | `SaveData.cs` |
| XxxManager / XxxSystem | `Systems/` | `EconomySystem.cs` |
| 继承 MonoBehaviour 的显示组件 | `View/Component/` | `BuildingView.cs` |
| UI 面板 | `View/UI/Panels/` | `InventoryPanel.cs` |
| HUD 元素 | `View/UI/HUD/` | `GoldDisplay.cs` |
| 扩展方法 | `Common/Extension/` | `ListExtensions.cs` |
| 工具函数 | `Common/Utils/` | `MathHelper.cs` |
| 事件定义 | `Common/Events/` | `PlayerEvents.cs` |

---

## 🔑 三个关键问题

### 1️⃣ 这个类需要 MonoBehaviour 吗？

| 答案 | 去向 |
|------|------|
| **需要** + 处理显示 | `View/` |
| **需要** + 管理逻辑 | `Systems/` |
| **不需要** + 是数据 | `State/` 或 `Config/` |

### 2️⃣ 这个类的数据会变化吗？

| 答案 | 去向 |
|------|------|
| **不变**（来自配置表） | `Config/` |
| **会变**（游戏运行中） | `State/Runtime/` |

### 3️⃣ 这个类会直接操作 UI 吗？

| 答案 | 去向 |
|------|------|
| **会**（显示/接收输入） | `View/` |
| **不会**（纯逻辑） | `Systems/` |

---

## 📝 实际例子：背包系统

| 文件 | 放在 | 原因 |
|------|------|------|
| `ItemConfig.cs` | `Config/` | 读取物品配置（名称、图标、价格） |
| `InventoryState.cs` | `State/Runtime/` | 记录玩家拥有的物品（数量、位置） |
| `InventorySystem.cs` | `Systems/` | 处理添加/删除物品的逻辑规则 |
| `InventoryPanel.cs` | `View/UI/Panels/` | 显示背包界面、响应点击 |
| `ItemSlotView.cs` | `View/Component/` | 单个物品格子的显示组件 |

---

## ⚠️ 常见错误

| ❌ 错误做法 | ✅ 正确做法 |
|------------|------------|
| 把业务逻辑写在 UI 里 | UI 只发送指令给 System |
| 在 Config 里存运行时数据 | 运行时数据放 State |
| Manager 直接改 UI 组件 | Manager 改 State，UI 监听刷新 |
| 到处 GetComponent 找依赖 | 通过事件或接口通信 |

---

## 🔄 数据流向

```
     Config（只读配置）
           │
           ▼
┌──────────────────┐
│     System       │ ◄─── 玩家输入（来自 View）
│   (处理逻辑)     │
└────────┬─────────┘
         │ 修改
         ▼
┌──────────────────┐
│     State        │
│   (运行时状态)   │
└────────┬─────────┘
         │ 读取/监听
         ▼
┌──────────────────┐
│     View         │ ───► 显示给玩家
│   (UI/视觉)      │
└──────────────────┘
```

---

## 📚 相关文档

- `ReadMe_Cursor.md` - 项目协作指南
- `Assets/Gen/` - Luban 生成的配置类（自动生成，勿手动修改）

---

**最终原则：** 每个类只做一件事，不确定时就问 **"这个类的职责是什么？"**

