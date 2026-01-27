# CardViewController 鼠标悬停功能说明

## 功能概述

CardViewController现在支持鼠标悬停交互：
1. **卡牌缩放**：鼠标悬停时，卡牌略微放大
2. **标签描述**：鼠标悬停在关键词上时，右侧显示详细描述

## 配置步骤

### 1. 在Unity Inspector中配置

选择CardViewController对象，设置以下字段：

#### 描述面板
- **Description View Controller Prefab**: 拖入DescriptionViewController预制体

#### 悬停设置
- **Hover Scale**: 悬停时的缩放比例（默认：1.1）
- **Scale Duration**: 缩放动画时长（默认：0.2秒）
- **Description Offset**: 描述面板相对于卡牌的偏移（默认：X=300, Y=0）

#### 层级设置
- **Hover Sorting Order**: 悬停时的Canvas排序顺序（默认：100）
  - 数值越大，渲染优先级越高
  - 设置足够大的值确保卡牌显示在所有其他UI之上

### 2. 确保依赖项

需要安装DOTween插件用于动画效果。如果没有安装：
1. 通过Package Manager安装DOTween
2. 或使用Assets/Import Package导入

## 功能详解

### 卡牌缩放动画

**触发条件：**
- 鼠标进入卡牌区域

**动画效果：**
- 使用DOTween的OutBack缓动
- 平滑放大到设定的缩放比例
- 鼠标离开时恢复原始大小

**代码实现：**
```csharp
public void OnPointerEnter(PointerEventData eventData)
{
    // 提升渲染层级
    ElevateCard();
    
    // 放大动画
    transform.DOScale(_originalScale * hoverScale, scaleDuration)
        .SetEase(Ease.OutBack);
}
```

### 层级管理系统（脱离Mask）

**问题场景：**
在ScrollView或其他带Mask的容器中，放大的卡牌会被遮罩裁剪。

**解决方案：**
通过动态调整Canvas的sortingOrder，使卡牌渲染在遮罩之上。

**工作原理：**
1. **初始化时**：
   - 为每张卡牌添加Canvas组件
   - 设置`overrideSorting = false`（默认不覆盖）
   - 添加GraphicRaycaster接收鼠标事件

2. **鼠标进入时（ElevateCard）**：
   - 设置`overrideSorting = true`
   - 设置`sortingOrder = 100`（可配置）
   - 卡牌脱离父Canvas的遮罩限制
   - 显示在所有其他UI之上

3. **鼠标离开时（RestoreCard）**：
   - 设置`overrideSorting = false`
   - 恢复原始渲染顺序
   - 重新受父Canvas的遮罩影响

**代码实现：**
```csharp
private void ElevateCard()
{
    _hoverCanvas.overrideSorting = true;
    _hoverCanvas.sortingOrder = hoverSortingOrder;
}

private void RestoreCard()
{
    _hoverCanvas.overrideSorting = false;
}
```

**为什么有效：**
- Canvas的`overrideSorting`允许子Canvas独立控制渲染顺序
- 高sortingOrder的Canvas会渲染在低sortingOrder之上
- 不受父Canvas的RectMask2D或Mask组件影响

### 标签描述显示

**触发条件：**
- 鼠标悬停在带有link标签的关键词上
- 关键词必须在NounDictionary配置中存在

**工作流程：**
1. 每帧检测鼠标位置
2. 使用`TMP_TextUtilities.FindIntersectingLink`检测link
3. 提取link ID（即关键词名称）
4. 从`TbNounDictionary`获取描述
5. 显示DescriptionViewController面板
6. 鼠标移开时隐藏面板

## NounDictionary配置

### 配置文件位置
`GeneratedDatas/json/tbnoundictionary.json`

### 配置格式
```json
[
  {
    "Name": "僵直",
    "Desc": "打出此牌后，需要休息{T}格才可以行动"
  },
  {
    "Name": "引导",
    "Desc": "需持续占用 {T} 格时间。中途被打断,施法失败"
  }
]
```

### 支持的字段
- **Name**: 关键词名称（用于匹配）
- **Desc**: 关键词描述（支持{}标签）

## 标签系统

### TextMeshPro Link标签

CardDescriptionParser会为未定义的[]标签自动添加link：

**输入：**
```
"若目标处于[僵直]中"
```

**输出：**
```html
若目标处于<link="僵直"><color=#9c660a>僵直</color></link>中
```

### 检测link的代码
```csharp
int linkIndex = TMP_TextUtilities.FindIntersectingLink(
    textComponent, 
    mousePosition, 
    null
);
```

## 使用示例

### 完整配置示例

**卡牌配置（character_tbcardinfo.json）：**
```json
{
  "Id": "W002",
  "Name": "迅猛打击",
  "Description": "造成{A}点伤害。若目标处于[僵直]或[引导]中，伤害翻倍。"
}
```

**名词字典（tbnoundictionary.json）：**
```json
[
  {
    "Name": "僵直",
    "Desc": "打出此牌后，需要休息{T}格才可以行动"
  },
  {
    "Name": "引导",
    "Desc": "需持续占用 {T} 格时间。中途被打断,施法失败"
  }
]
```

**运行效果：**
1. 卡牌显示：造成<color=#921303>6</color>点伤害。若目标处于<link="僵直"><color=#9c660a>僵直</color></link>或<link="引导"><color=#9c660a>引导</color></link>中，伤害翻倍。
2. 鼠标悬停卡牌 → 卡牌放大
3. 鼠标悬停"僵直" → 右侧显示描述框
4. 描述框显示：
   - 标题：僵直
   - 内容：打出此牌后，需要休息{T}格才可以行动

## DescriptionViewController

### 公共方法

```csharp
// 显示描述
void Show(string nounName)

// 隐藏描述
void Hide()

// 设置位置
void SetPosition(Vector3 position)
```

### UI组件

- **Txt_EntryName**: 显示关键词名称
- **Txt_Entry**: 显示关键词描述

## 调试技巧

### 检查link是否生成
1. 在Scene视图中选择CardViewController
2. 查看Txt_Effect的Text内容
3. 确认包含`<link="...">`标签

### 检查NounDictionary
```csharp
var dict = ConfigLoader.Tables.TbNounDictionary.GetOrDefault("僵直");
Debug.Log($"Found: {dict?.Name} - {dict?.Desc}");
```

### 调试日志
```csharp
// 在CheckLinkHover中添加
Debug.Log($"Link Index: {linkIndex}, Link ID: {linkId}");
```

## 注意事项

1. **Canvas设置**：CardViewController必须在Canvas下，用于定位描述面板
2. **DOTween依赖**：确保已安装DOTween插件
3. **预制体引用**：必须在Inspector中设置DescriptionViewController预制体
4. **配置加载**：确保ConfigLoader.Load()在使用前已调用
5. **TextMeshPro组件**：Txt_Effect必须使用TMP_Text组件
6. **link标签限制**：只有[]标签中的未定义关键词才会添加link
7. **自动添加组件**：
   - Canvas组件会自动添加到卡牌GameObject
   - GraphicRaycaster会自动添加用于接收鼠标事件
   - 不需要手动配置这些组件
8. **sortingOrder值**：
   - 默认值100通常足够
   - 如果卡牌仍被遮挡，增加这个值
   - 确保大于其他UI元素的sortingOrder

## 层级系统调试

### 检查Canvas设置
在Scene视图中选择卡牌，查看Canvas组件：
- **Override Sorting**: 鼠标悬停时应为true
- **Sorting Order**: 悬停时应为设定值（默认100）
- 鼠标离开后应恢复为false

### 调试日志
```csharp
// ElevateCard中会输出：
"卡牌提升层级: sortingOrder=100"

// RestoreCard中会输出：
"卡牌恢复原始层级"
```

### 常见问题

**问题1：卡牌放大后仍被裁剪**
- 检查sortingOrder是否足够大
- 确认父Canvas的sortingOrder
- 增加hoverSortingOrder的值

**问题2：卡牌无法接收鼠标事件**
- 确认GameObject上有GraphicRaycaster组件
- 检查是否有其他UI遮挡
- 确认Canvas的Raycaster设置

**问题3：卡牌层级不恢复**
- 检查OnPointerExit是否被正确触发
- 确认没有异常中断事件流
- 查看调试日志确认RestoreCard被调用

## 层级系统可视化

```
正常状态：
┌─────────────────────────────┐
│ Canvas (sortingOrder=0)     │
│  ├─ ScrollView               │
│  │   ├─ Mask/RectMask2D     │
│  │   │   └─ Content         │
│  │   │       ├─ Card1       │ ← overrideSorting=false
│  │   │       ├─ Card2       │ ← overrideSorting=false
│  │   │       └─ Card3       │ ← overrideSorting=false
└─────────────────────────────┘
所有卡牌受Mask限制，超出区域被裁剪

悬停状态（鼠标在Card2上）：
┌─────────────────────────────┐
│ Canvas (sortingOrder=0)     │
│  ├─ ScrollView               │
│  │   ├─ Mask/RectMask2D     │
│  │   │   └─ Content         │
│  │   │       ├─ Card1       │ ← overrideSorting=false (sortingOrder=0)
│  │   │       ├─ Card2       │ ← overrideSorting=true  (sortingOrder=100) ★ 脱离Mask
│  │   │       └─ Card3       │ ← overrideSorting=false (sortingOrder=0)
└─────────────────────────────┘
Card2显示在最上层，不受Mask裁剪，可以完整显示放大效果
```

## 扩展功能

### 自定义描述位置
修改`descriptionOffset`向量：
```csharp
descriptionOffset = new Vector2(300f, 100f); // 右上方
```

### 自定义缩放效果
修改缩放参数：
```csharp
hoverScale = 1.2f;        // 放大20%
scaleDuration = 0.3f;     // 动画时长0.3秒
```

### 自定义层级优先级
修改sortingOrder：
```csharp
hoverSortingOrder = 200;  // 更高的优先级
```

### 添加音效
在`OnPointerEnter`中添加：
```csharp
AudioManager.PlaySound("card_hover");
```

## 性能优化

- link检测在Update中进行，仅当鼠标在卡牌上时才激活
- 使用string缓存避免重复显示相同描述
- 描述面板采用对象池可进一步优化性能

