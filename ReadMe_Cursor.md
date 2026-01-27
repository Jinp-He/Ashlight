项目目标与协作指南（Cursor 使用说明）
一、项目总体目标

这是一个 以 Unity 为核心的独立游戏项目，目标是：

构建一个 可持续扩展的中小体量游戏原型

架构优先于功能堆砌，强调 可维护性、数据驱动与清晰职责边界

支持后期玩法叠加（挂机 / Roguelite / 卡牌 / 数值成长），而不是一次性写死逻辑

项目当前阶段以 核心系统搭建 + 可玩闭环 为主，而不是内容量。

二、核心设计原则（必须遵守）
1. 架构优先（Architecture First）

任何新功能，先想清楚它属于哪个系统，再写代码

不允许把：

状态逻辑

存档逻辑

UI 展示

配置读取 混写在同一个类里

2. 数据驱动（Data Driven）

所有可调数值 必须来自配置表（Luban / Config）

运行时状态 ≠ 配置数据

配置数据：

不可变

只读

状态数据：

可存档

可恢复

3. 低耦合、高内聚

系统之间 通过接口 / 事件 / Manager 通信

禁止互相 GetComponent / FindObjectOfType 滥用

禁止 UI 直接改业务状态

三、推荐系统划分
1. 数据层（Data / Config）

职责：

读取 Luban 生成的数据

提供只读访问接口

示例：

BuildingConfig

TouristLevelConfig

ItemConfig

禁止：

在 Config 中存运行时状态

2. 状态层（Runtime State / Save）

职责：

记录游戏进行中的真实状态

支持 Save / Load

示例：

BuildingState

TouristState

PlayerProgressState

推荐：

使用纯 C# 数据结构（无 MonoBehaviour）

3. 系统层（Manager / System）

职责：

驱动规则

更新状态

处理系统级逻辑

示例：

SaveManager

GameLoopManager

TouristSystem

EconomySystem

规则：

一个 Manager 只负责一类规则

4. 表现层（View / UI）

职责：

展示状态

发送玩家输入

示例：

UI 面板

HUD

动画 / 特效

规则：

UI 只能 读状态 / 发指令

不直接写数值规则

四、Cursor 在项目中的角色

Cursor 是：

一个 严格遵守架构约束的协作工程师，而不是快速堆代码的工具。

Cursor 应该做的事情

根据现有结构 补全代码

按模块生成：

清晰的类

明确的职责

可扩展的接口

写代码时：

优先给出结构

再补实现

Cursor 不应该做的事情

不要：

自动把逻辑塞进现有类

修改已有架构设计

为了“跑起来”破坏分层

五、代码风格要求

命名清晰，拒绝缩写

方法短小，一个方法只做一件事

明确区分：

Init / Tick / Update

Command / Query

示例：

// 好
GetCurrentTicketPrice()
ApplyHappinessDelta(int delta)


// 坏
DoStuff()
Handle()
六、当前阶段的优先级

当前优先级（从高到低）：

核心循环可跑通（生成 → 消耗 → 反馈）

Save / Load 稳定

配置表结构清晰

UI 最小可用

暂时不做：

复杂特效

美术 polish

过度优化

七、协作方式建议

每次修改前：

先确认改动属于哪个系统

每次新增文件：

明确它的层级归属

如果不确定：

停下来，先问“这个类的职责是什么”

最终原则（非常重要）

这个项目不是为了“写得快”， 而是为了 半年后你还能一眼看懂自己在干什么。

Cursor 的目标： 帮助维持秩序，而不是制造复杂度。