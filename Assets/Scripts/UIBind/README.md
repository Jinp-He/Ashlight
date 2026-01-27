# UI Bind 系统

类似 QFramework 的 UI 绑定系统，用于自动生成 UI 组件绑定代码。

## 新特性 ✨

1. **自动填充名称**：BindName 留空时自动使用 GameObject 名称
2. **自定义组件类型**：可手动指定 ComponentType，留空则自动检测
3. **嵌套层级管理**：支持 UIBind 作为容器，管理复杂 UI 层级关系
4. **可视化 Inspector**：提供友好的 Inspector 界面和一键填充功能

## 使用方法

### 1. 在主面板上添加 UIBindParent 组件

选中你的主面板 GameObject（例如：LoginPanel），添加 `UIBindParent` 组件。

配置参数：
- **Class Name**: 生成的脚本类名（默认使用 GameObject 名称）
- **Namespace**: 命名空间（默认：_Scripts.UI）
- **Script Path**: 脚本生成路径（默认：_Scripts/UI/Generated）

### 2. 在需要绑定的 UI 上添加 UIBind 组件

在主面板的子物体上添加 `UIBind` 组件来标记需要绑定的 UI。

配置参数：
- **Bind Name**: 绑定名称（留空自动使用 GameObject 名称）
  - 💡 点击 "Auto Fill" 按钮快速填充
- **Component Type**: 组件类型（下拉选择）
  - 🎯 从当前 GameObject 的所有 Component 中选择
  - 💡 选择 "(Auto Detect)" 自动检测
  - 💡 点击 "Auto" 按钮智能推荐
  - 📝 可选择 "GameObject" 绑定整个对象
- **Is Container**: 是否作为容器（可包含子 UIBind）

### 3. 生成绑定代码

1. 选中主面板（带有 UIBindParent 的 GameObject）
2. 右键点击 → 选择 **"生成UI绑定代码"**
3. 代码会自动生成到指定路径

### 4. 使用生成的代码

生成的代码会创建一个 partial class，包含：
- 所有绑定的 UI 组件字段
- `InitUIBindings()` 方法用于初始化绑定

在你的脚本中：

```csharp
using UnityEngine;
using _Scripts.UI;

namespace _Scripts.UI
{
    // 这是你手动创建的部分
    public partial class LoginPanel : MonoBehaviour
    {
        void Start()
        {
            // 调用自动生成的初始化方法
            InitUIBindings();
            
            // 现在可以使用绑定的组件了
            LoginButton.onClick.AddListener(OnLoginClick);
            UsernameInput.text = "请输入用户名";
        }
        
        void OnLoginClick()
        {
            Debug.Log($"用户名: {UsernameInput.text}");
        }
    }
}
```

## 示例

### 示例 1：简单的平面结构

假设你有以下层级结构：

```
LoginPanel (UIBindParent)
├─ UsernameInput (UIBind)
├─ PasswordInput (UIBind)
├─ LoginButton (UIBind)
└─ RegisterButton (UIBind)
```

生成的代码会是：

```csharp
public partial class LoginPanel : MonoBehaviour
{
    #region UI Bindings
    
    public TMP_InputField UsernameInput;
    public TMP_InputField PasswordInput;
    public Button LoginButton;
    public Button RegisterButton;
    
    #endregion
    
    private void InitUIBindings()
    {
        // 自动初始化代码...
    }
}
```

### 示例 2：使用容器管理复杂层级

假设你有一个复杂的商店界面：

```
ShopPanel (UIBindParent)
├─ HeaderPanel (UIBind, IsContainer=true)
│   ├─ TitleText (UIBind)
│   ├─ CoinText (UIBind)
│   └─ CloseButton (UIBind)
├─ ItemListPanel (UIBind, IsContainer=true)
│   ├─ Item1 (UIBind)
│   ├─ Item2 (UIBind)
│   └─ Item3 (UIBind)
└─ FooterPanel (UIBind, IsContainer=true)
    ├─ BuyButton (UIBind)
    └─ CancelButton (UIBind)
```

生成的代码会保持层级关系（通过缩进显示）：

```csharp
public partial class ShopPanel : MonoBehaviour
{
    #region UI Bindings
    
    // Container: HeaderPanel
    public GameObject HeaderPanel;
    
        public TextMeshProUGUI TitleText;
        public TextMeshProUGUI CoinText;
        public Button CloseButton;
    
    // Container: ItemListPanel
    public GameObject ItemListPanel;
    
        public GameObject Item1;
        public GameObject Item2;
        public GameObject Item3;
    
    // Container: FooterPanel
    public GameObject FooterPanel;
    
        public Button BuyButton;
        public Button CancelButton;
    
    #endregion
    
    private void InitUIBindings()
    {
        // 自动初始化代码...
    }
}
```

**容器的优势**：
- 📁 清晰的代码结构，反映实际的 UI 层级
- 🎯 便于管理复杂的 UI 组件关系
- 📝 自动添加注释标记容器组件
- 🔍 在 Inspector 中可以看到子 Bind 列表

## 支持的组件类型

系统会自动检测以下组件类型：

### TextMeshPro
- TMP_Text
- TextMeshProUGUI
- TMP_InputField

### Unity UI
- Button
- Image
- RawImage
- Toggle
- Slider
- Scrollbar
- Dropdown
- InputField
- Text

### 布局
- ScrollRect
- RectTransform

如果没有检测到特定组件，会绑定为 GameObject。

## 高级用法

### 自定义 Component Type

使用下拉选择框可以轻松选择想要的组件类型：

**场景示例：**
```
GameObject "LoginButton" 上有以下组件：
- RectTransform
- CanvasRenderer  
- Image
- Button
- UIBind
```

**操作方式：**

1. **查看所有组件**：点击 Component Type 下拉框，会列出所有可选组件：
   ```
   (Auto Detect)    ← 自动检测（推荐 Button）
   GameObject       ← 绑定整个 GameObject
   RectTransform    ← 绑定 RectTransform 组件
   CanvasRenderer   ← 绑定 CanvasRenderer 组件
   Image           ← 绑定 Image 组件
   Button          ← 绑定 Button 组件
   UIBind          ← 绑定 UIBind 组件（一般不需要）
   ```

2. **手动选择**：直接从下拉框选择你需要的组件类型
   - 例如：你想要访问 Image 的 sprite，选择 "Image"
   - 例如：你想要控制整个对象，选择 "GameObject"

3. **智能推荐**：点击 "Auto" 按钮，系统会根据优先级推荐：
   - Button（如果有）
   - Image（如果有）
   - 其他常用 UI 组件
   - RectTransform（默认）

**使用场景：**
- 🎯 需要访问特定组件的属性或方法
- 🔄 GameObject 上有多个 UI 组件，需要绑定不常用的那个
- 📦 需要绑定整个 GameObject 进行显示/隐藏控制

### 嵌套容器的最佳实践

1. **只标记直接子级**：容器只会收集直接子级的 UIBind，不会递归
2. **合理分组**：把相关的 UI 元素放在同一个容器下
3. **命名规范**：容器建议用 "xxxPanel" 或 "xxxGroup" 命名

### 嵌套 UIBindParent 支持 🎯

系统支持嵌套的 UIBindParent，避免重复绑定：

**场景示例：**
```
MainPanel (UIBindParent)
├─ HeaderButton (UIBind)
├─ ContentArea (GameObject)
│   └─ SubPanel (UIBindParent)  ← 嵌套的 UIBindParent
│       ├─ SubButton1 (UIBind)
│       └─ SubButton2 (UIBind)
└─ FooterButton (UIBind)
```

**行为说明：**

1. **MainPanel 生成代码时**：
   - ✅ 会找到 `HeaderButton`
   - ✅ 会找到 `FooterButton`
   - ❌ **不会**找到 `SubButton1` 和 `SubButton2`（它们属于 SubPanel）

2. **SubPanel 生成代码时**：
   - ✅ 会找到 `SubButton1`
   - ✅ 会找到 `SubButton2`
   - ❌ 不会影响 MainPanel 的绑定

**特殊情况：子 Panel 同时有 UIBind 和 UIBindParent** 🎯

```
MainPanel (UIBindParent)
├─ HeaderButton (UIBind)
├─ SubPanel (UIBindParent + UIBind)  ← 同时有两个组件
│   ├─ SubButton1 (UIBind)
│   └─ SubButton2 (UIBind)
└─ FooterButton (UIBind)
```

在这种情况下：
- ✅ **MainPanel 会绑定 SubPanel**（因为 SubPanel 有 UIBind）
- ❌ MainPanel **不会**绑定 SubButton1 和 SubButton2（停止递归）
- ✅ SubPanel 可以绑定自己的子元素

**使用场景：**
```csharp
// MainPanel 可以控制 SubPanel 的显示/隐藏
SubPanel.SetActive(true/false);

// SubPanel 管理自己内部的按钮
SubButton1.onClick.AddListener(...);
```

**优势：**
- 🎯 每个 Panel 有独立的绑定范围
- 🔒 避免重复绑定同一个 UIBind
- 📦 支持复杂的嵌套 UI 结构
- ♻️ 可以复用 Panel 预制体
- 🎮 父 Panel 可以控制子 Panel（作为整体）

### 重要说明：不要在 UIBindParent 所在的对象上添加 UIBind ⚠️

**错误做法：**
```
SettingPage (UIBindParent + UIBind)  ❌ 不要这样做！
├─ Button1 (UIBind)
└─ Button2 (UIBind)
```

**问题：**
- UIBindParent 自己的 UIBind 会被忽略（不会生成绑定）
- 容易造成混淆

**正确做法：**
```
SettingPage (UIBindParent)  ✅ 只有 UIBindParent
├─ Button1 (UIBind)
└─ Button2 (UIBind)
```

**例外情况：**
只有当 SettingPage 需要被**另一个父级 Panel** 引用时，才同时添加 UIBind：

```
MainPanel (UIBindParent)
└─ SettingPage (UIBindParent + UIBind)  ✅ 这样可以
    ├─ Button1 (UIBind)
    └─ Button2 (UIBind)
```

这种情况下：
- MainPanel 可以引用 SettingPage（作为子 Panel）
- SettingPage 不会引用自己
- SettingPage 只绑定 Button1 和 Button2

### Inspector 增强功能

- **Bind Name**：
  - 🔘 Auto Fill 按钮：一键填充当前 GameObject 名称
  - 📋 实时预览：留空时显示将要使用的名称
  
- **Component Type**：
  - 📋 下拉选择框：列出当前 GameObject 的所有 Component
  - ⚡ Auto 按钮：智能推荐最合适的组件类型
  - 🔍 实时提示：显示当前选择或自动检测的类型
  
- **Container 管理**：
  - 📂 子 Bind 列表：容器模式下显示所有直接子 Bind
  - 🎯 Select 按钮：快速选择子 Bind

## 注意事项

1. 主面板必须有 `UIBindParent` 组件
2. 需要绑定的 UI 必须添加 `UIBind` 组件
3. 生成的代码是 partial class，可以在另一个文件中扩展
4. 如果有重名的绑定，会自动添加数字后缀
5. 生成的代码需要手动调用 `InitUIBindings()` 来初始化
6. **BindName 留空**会自动使用 GameObject 名称（推荐）
7. **ComponentType 留空**会自动检测组件类型（推荐）
8. 容器只收集**直接子级**的 UIBind，不会递归到更深层级
9. **嵌套的 UIBindParent**：遇到子级的 UIBindParent 会自动停止查找，避免重复绑定
10. **UIBindParent + UIBind 组合**：如果一个对象同时有 UIBindParent 和 UIBind，父级会收集它的 UIBind（但不递归到它的子级）
11. **不绑定自身**：UIBindParent 不会绑定自己的 UIBind（即使同一对象上有 UIBind 组件）

