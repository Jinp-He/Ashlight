# CardViewController 使用说明

## 概述

CardViewController 是卡牌UI的控制器，负责显示卡牌的所有信息。它由两部分组成：

1. **CardViewController.cs (Generated)** - 自动生成的UI绑定代码
2. **CardViewController.Logic.cs** - 手动编写的业务逻辑代码

## CardDescriptionParser 描述解析器

### 功能

CardDescriptionParser 负责解析卡牌的 Description 字段，将标签替换为实际数值。

### 支持的标签

#### 1. {} 标签 - Effect数值

根据 EffectType 自动匹配对应的 Effect 数据：

- `{A}` - 攻击伤害 (EffectType.Attack)
- `{D}` - 防御/护甲值 (EffectType.Defense)
- `{H}` - 治疗数值 (EffectType.Heal)
- `{T}` - 时间轴推迟格数 (EffectType.TimeSlot)
- `{B}` - Buff数值 (EffectType.Buff)
- `{N}` - 空效果 (EffectType.Null)

**带索引的标签：**

如果同一类型的Effect有多个，可以使用索引指定：

- `{A:0}` - 第一个攻击效果
- `{A:1}` - 第二个攻击效果
- `{D:0}` - 第一个防御效果

#### 2. [] 标签 - 卡牌属性和关键词

**预定义属性标签（会被替换为实际值）：**

- `[Channeling]` - 引导时间
- `[Duration]` - 持续时间
- `[Recoil]` - 后摇时间
- `[TargetType]` - 目标类型（转换为中文）
- `[Rarity]` - 稀有度（转换为中文）
- `[BelongTo]` - 所属角色（转换为中文）
- `[Name]` - 卡牌名称

**自定义关键词（保留内容，仅添加颜色）：**

- `[僵直]` → 僵直（绿色）
- `[引导]` → 引导（绿色）
- `[任意关键词]` → 任意关键词（绿色）

所有[]标签内的内容都会被染成绿色，用于突出显示游戏中的关键词、状态名称等。

### 使用示例

#### 配置数据示例

```json
{
  "Id": "card_001",
  "Name": "火球术",
  "Description": "对[TargetType]造成{A}点火焰伤害，并恢复自身{H}点生命值。引导时间[Channeling]，持续[Duration]回合。",
  "Effects": [
    {
      "$type": "AttackEffect",
      "EffectType": 0,  // Attack
      "damage": 10,
      "is_aoe": false
    },
    {
      "$type": "HealEffect",
      "EffectType": 3,  // Heal
      "value": 5
    }
  ],
  "Channeling": 2,
  "Duration": 3,
  "Recoil": 1,
  "TargetType": 3,  // SingleEnemy
  "Rarity": 1,      // Rare
  "BelongTo": 0     // Mage
}
```

#### 解析结果

**阅览模式 (DescriptionMode.View):**
```
对<color=#00FF00>单个敌人</color>造成<color=#FFA500>10</color>点火焰伤害，并恢复自身<color=#FFA500>5</color>点生命值。引导时间<color=#00FF00>2</color>，持续<color=#00FF00>3</color>回合。
```

**实际显示效果：**
- "单个敌人"、"2"、"3" → 绿色显示
- "10"、"5" → 橙色显示

**战斗模式 (DescriptionMode.Battle):**
```
对<color=#00FF00>单个敌人</color>造成<color=#FFA500>15</color>点火焰伤害，并恢复自身<color=#FFA500>8</color>点生命值。引导时间<color=#00FF00>2</color>，持续<color=#00FF00>3</color>回合。
```
（假设受buff影响，伤害和治疗量增加）

### 颜色系统

**橙色 (#FFA500) - 数值标签 {}**
- 用于效果数值：伤害、治疗、护甲等
- 示例：{A}、{D}、{H}

**绿色 (#00FF00) - 属性标签 []**
- 用于卡牌属性和游戏关键词
- 示例：[TargetType]、[Duration]、[僵直]、[引导]

### 实际配置示例

**配置数据（来自实际的character_tbcardinfo.json）：**

```json
{
  "Id": "W002",
  "Name": "迅猛打击",
  "Description": "造成{A}点伤害。若目标处于[僵直]或[引导]中，伤害翻倍。",
  "Effects": [
    {
      "$type": "AttackExtraEffect",
      "damage": 6
    }
  ]
}
```

**解析后的输出：**
```
造成<color=#FFA500>6</color>点伤害。若目标处于<color=#00FF00>僵直</color>或<color=#00FF00>引导</color>中，伤害翻倍。
```

**显示效果：**
- "6" → 橙色（效果数值）
- "僵直"、"引导" → 绿色（游戏关键词）

### 代码使用示例

#### 初始化卡牌（阅览模式）

```csharp
CardInfo cardInfo = ConfigLoader.Tables.TbCardInfo.Get("W002");
cardViewController.Initialize(cardInfo, DescriptionMode.View);
```

#### 初始化卡牌（战斗模式）

```csharp
CardInfo cardInfo = ConfigLoader.Tables.TbCardInfo.Get("W002");
cardViewController.Initialize(cardInfo, DescriptionMode.Battle);
```

#### 切换显示模式

```csharp
// 从阅览模式切换到战斗模式
cardViewController.SetDisplayMode(DescriptionMode.Battle);

// 从战斗模式切换回阅览模式
cardViewController.SetDisplayMode(DescriptionMode.View);
```

## 显示模式说明

### 阅览模式 (View)

- 使用配置文件中的静态数值
- 用于卡牌图鉴、商店、背包等非战斗场景
- 显示卡牌的基础属性

### 战斗模式 (Battle)

- 使用运行时动态计算的数值
- 受buff、装备、技能等影响
- 显示当前实际生效的数值
- **注意：** 当前 BattleSystem 未实装，暂时返回静态数值

## 扩展说明

### 添加新的Effect类型

1. 在 `defines.xml` 中定义新的 Effect bean
2. 在 `EffectEnum` 中添加对应的枚举值
3. 在 `CardDescriptionParser.GetStaticEffectValue()` 中添加对应的 case 分支
4. 在配置的 Description 中使用对应的标签

### 添加新的属性标签

在 `CardDescriptionParser.ReplaceBracketTags()` 方法中添加新的 case 分支。

## 注意事项

1. **标签名称区分大小写**：`[TargetType]` ✅，`[targettype]` ❌
2. **{} 标签必须匹配Effect**：如果配置中没有对应类型的Effect，标签不会被替换
3. **[] 标签是通用的**：任何内容都可以放在[]中，用于突出显示游戏术语、状态名称等
4. **Effect索引从0开始**：`{A:0}` 表示第一个攻击效果，`{A:1}` 表示第二个
5. **战斗模式需要BattleSystem**：当前未实装，暂时返回静态值
6. **使用TextMeshPro组件**：确保UI使用的是TMP_Text组件，而不是Unity.UI.Text

## [] 标签的双重用途

### 用途1：引用卡牌属性
```
"引导[Channeling]回合" → "引导2回合"
"目标[TargetType]" → "目标单个敌人"
```

### 用途2：突出显示关键词
```
"若目标处于[僵直]中" → "若目标处于僵直中"（绿色）
"打断[引导]状态" → "打断引导状态"（绿色）
"获得[护盾]" → "获得护盾"（绿色）
```

两种用途都会将内容染成绿色，用于在UI中突出显示重要信息。

