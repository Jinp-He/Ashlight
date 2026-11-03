# 意图 PSB 使用文档

> 源文件：`UI_意图.psb`（62×88 px，RGB 模式）
> 用途：战斗意图气泡视觉模板——含背景底板、索敌区域指示（Coord）、意图图标（IntentionIcon）、数值文字（Txt_figure）四块。
> 美术资源：`A:\ASHlight\Intention\` 下提供单图层 PNG 预览（用于引擎内引用与对比）。

---

## 0. 图层结构总览

```
UI_意图.psb
├── Img_IntentionBase                   [PixelLayer, 0,0-62,88]   ← 气泡底板（原名「图层 191」，已改英文名；导出: Img_IntentionBase.png）
├── Coord                               [Group,    15,3-48,9]
│   ├── Coord_Img_Aoe                   [PixelLayer, 15,3-39,9]   ← AOE 横条（左侧位）
│   └── Coord_Img_Monomer               [PixelLayer, 42,3-48,9]   ← 单体点（右侧位）
├── IntentionIcon                       [Group,    0,0-62,88]      ← 五个图标叠在同位置，每次只显一个
│   ├── IntentionIcon/Img_Attack        [PixelLayer]
│   ├── IntentionIcon_Img_Remote        [PixelLayer]
│   ├── IntentionIcon/Img_State         [PixelLayer]
│   ├── IntentionIcon/Img_Shield        [PixelLayer]
│   └── IntentionIcon/Img_Think         [PixelLayer]
└── Txt_figure                          [TypeLayer, 11,50-50,71]   ← 数值文字（"NxM" 格式）
```

> 坐标系：左上原点 (0,0)，x 向右、y 向下。整张 62×88。

---

## 1. Coord 组使用逻辑

### 1.1 设计意图

Coord 组是一个**横向指示条**（高度 6px，位于气泡顶部 y:3–9），由**「一点一横」**组成：
- **点** = `Coord_Img_Monomer`（单体）——窄而小（6px 宽）
- **横** = `Coord_Img_Aoe`（范围）——宽而扁（24px 宽）

两者**永远同时存在**（「一横一点」组合），通过**位置 + 颜色**两个维度表达「索敌行」+「单体/范围」：
- **位置**（左/右槽）= 索敌行
- **形状**（点/横）= 攻击范围类型
- **颜色** = 该槽是否被当前索敌激活

### 1.2 颜色规则

| 状态 | 填色 |
|---|---|
| 激活（当前索敌区域） | `#9c660a`（深金/赭石） |
| 未激活（非当前区域） | `#3f4447`（深灰） |

> 颜色为覆盖/重填色，不是替换贴图——所有 Coord 图层在引擎内用统一 tint 通道控制。

### 1.3 槽位定义

| 槽位 | x 范围 | 代表行 |
|---|---|---|
| **右侧槽** | x:42–48 | 前排（Front） |
| **左侧槽** | x:15–39 | 后排（Back） |

> 索敌前排 → 激活右侧槽；索敌后排 → 激活左侧槽。

### 1.4 四态对照表（核心）

| 索敌目标 | 右侧槽（前排） | 左侧槽（后排） |
|---|---|---|
| **前排·单体** | **单体点** · `#9c660a` ✅ | AOE 横条 · `#3f4447` |
| **前排·AOE** | **AOE 横条** · `#9c660a` ✅ | 单体点 · `#3f4447` |
| **后排·单体** | AOE 横条 · `#3f4447` | **单体点** · `#9c660a` ✅ |
| **后排·AOE** | 单体点 · `#3f4447` | **AOE 横条** · `#9c660a` ✅ |

**「一横一点」恒成立**：四态中始终恰好一个点 + 一个横，只是分布在不同槽位且颜色不同。

### 1.5 视觉示意（抽象）

```
默认导出状态（前排·单体，对照 PSB 当前布局）：

  ┌──────────────┬──┐
  │ ▬▬▬ AOE 横条 │ •│  ← 横=灰(3f4447), 点=金(9c660a)
  └──────────────┴──┘
   ←  左槽(后排)    右槽(前排) →
```

切换为「前排·AOE」时——两侧形状对调位置：

```
  ┌──┬──────────────┐
  │ •│ ▬▬▬ AOE 横条 │  ← 点=灰(3f4447), 横=金(9c660a)
  └──┴──────────────┘
   ←  左槽(后排)    右槽(前排) →
```

切换为「后排·单体」时——形状回到原始布局，但激活色翻到左侧：

```
  ┌──────────────┬──┐
  │ ▬▬▬ AOE 横条 │ •│  ← 横=金(9c660a), 点=灰(3f4447)
  └──────────────┴──┘
   ←  左槽(后排)✅  右槽(前排) →
```

### 1.6 引擎实现注意

- **不要**对 `Coord_Img_Aoe` / `Coord_Img_Monomer` 做缩放/旋转；只改 `color` 属性。
- 建议每张贴图在引擎里挂一个 `Image` 组件，通过 `Image.color` 设置 tint（Unity UGUI / Godot CanvasItem modulate 同理）。
- 四态切换时若需要**位置对调**（单体↔AOE 形状互换槽位），最简实现：在两个槽位各放一个常驻 `Image`（左槽 / 右槽），根据状态决定左槽显示哪张、右槽显示哪张，而不是移动贴图本身。

---

## 2. IntentionIcon 组使用逻辑

### 2.1 图标选择

依据单位意图类型，**只显示**对应的一个图标，其余四个隐藏：

| 意图类型 | 使用图层（PSB 实际名） | 导出 PNG 名 | 适用场景 |
|---|---|---|---|
| 近战攻击 | `IntentionIcon/Img_Attack` | `IntentionIcon_Img_Melee.png` | 近战单体/范围攻击 |
| 远程攻击 | `IntentionIcon_Img_Remote` | `IntentionIcon_Img_Remote.png` | 远程单体/范围攻击 |
| 施加状态 | `IntentionIcon/Img_State` | `IntentionIcon_Img_State.png` | 减益/控制类效果 |
| 防御/护盾 | `IntentionIcon/Img_Shield` | `IntentionIcon_Img_Shield.png` | 格挡、护甲、反击 |
| 无法执行 | `IntentionIcon/Img_Think` | `IntentionIcon_Img_Think.png` | 意图被封印/无法达成 |

> ⚠️ 命名不一致：PS B 图层用 `Img_Attack`，导出 PNG 用 `Img_Melee`。详见 §5。

### 2.2 颜色规则

所有显示的意图图标**统一覆盖为 `#9c660a`**（与 Coord 激活色一致）。

> 与 Coord 组一致：用 tint 通道控制，不要改原图。
> 「无法执行」(`Img_Think`) 也保持 `#9c660a`——通过图形本身传达「思考/犹豫」的语义，不靠灰色弱化。

### 2.3 显隐规则

IntentionIcon 组的 5 个图层**完全重叠**（bbox 都是 0,0–62,88），运行时四关一开：
- 通过图层 `enabled` / `visible` 切换；
- 或在引擎里用 5 个 `Image` 组件共用同一锚点，`SetActive(bool)` 控制。

---

## 3. Txt_figure 数值文字

### 3.1 字体

**字魂扁桃体**（商业字体，请确认项目已购授权）。

### 3.2 格式

`{次数}x{每次数值}`，如 `2x5` = 攻击 2 次、每次 5 点。

| 意图 | 文字格式 | 示例 |
|---|---|---|
| 近战/远程·多次攻击 | `{N}x{M}` | `2x5`、`1x8` |
| 近战/远程·单次攻击 | `1x{M}` | `1x6`（N=1 也保留） |
| 状态施加 | `{M}层` / `{N}回合`（待定） | `2层` |
| 护盾/防御 | `{M}` | `5` |
| 无法执行 | （留空/隐藏） | — |

> 攻击类建议恒用 `NxM` 格式保持统一；防御/状态文字格式需后续确认，本文档暂定占位。

### 3.3 位置与样式

- 位置：图层 bbox `(11, 50, 50, 71)`，宽度 39、高度 21，气泡下半部居中。
- 颜色：与图标同色系（建议 `#9c660a` 或稍亮的 `#c89241` 视可读性调整）。
- 大小：在 21px 高度内自适应，避免换行。

---

## 4. 美术资源清单

`A:\ASHlight\Intention\` 下的单图层 PNG 导出：

| 文件名 | 角色 | 尺寸感 |
|---|---|---|
| `Img_IntentionBase.png` | 气泡底板 | 62×88 |
| `Coord_Img_Aoe.png` | AOE 横条 | 24×6 |
| `Coord_Img_Monomer.png` | 单体点 | 6×6 |
| `IntentionIcon_Img_Melee.png` | 近战攻击图标 | 62×88（透明） |
| `IntentionIcon_Img_Remote.png` | 远程攻击图标 | 62×88（透明） |
| `IntentionIcon_Img_Shield.png` | 护盾图标 | 62×88（透明） |
| `IntentionIcon_Img_State.png` | 状态图标 | 62×88（透明） |
| `IntentionIcon_Img_Think.png` | 无法执行图标 | 62×88（透明） |
| `Txt_figure.png` | 数值文字样例（"1x2"） | 39×21 |

> 上述 PNG 为占位/预览，建议在 PSB 内完成所有微调后**重新统一导出**。

---

## 5. 命名规范备注（需决策）

PSB 图层与 PNG 导出之间存在三处不一致，建议在导入引擎前统一：

| 问题 | PSB 图层 | PNG 文件 | 建议 |
|---|---|---|---|
| 近战图标名 | `IntentionIcon/Img_Attack` | `IntentionIcon_Img_Melee.png` | 选其一：建议 `Melee`（与 Remote 对仗、语义精确） |
| 分组分隔符 | `IntentionIcon/Img_Attack`（斜杠）<br>`IntentionIcon_Img_Remote`（下划线） | 全部下划线 | PSB 内统一为下划线 `IntentionIcon_Img_Attack` 等 |
| Coord 命名 | `Coord_Img_Aoe` / `Coord_Img_Monomer` | 一致 | ✅ 保持现状 |

**项目既有约定**（参见 `A:\ASHlight\.workbuddy\memory\MEMORY.md` 2026-08-14）：UI 美术资源用英文 PascalCase + 下划线，不带中文。Coord 组与 IntentionIcon 组的下划线分组合规，斜杠风格建议改为下划线以保持统一。

> **已处理**：原底图图层 `图层 191`（中文名）已改名为 `Img_IntentionBase`，符合英文 PascalCase + 下划线规范；与导出的 `Img_IntentionBase.png` 一致。其余待决策项见上表。

---

## 6. 完整使用示例

### 示例 1：近战单体攻击 2×5（敌人当前索敌「前排·单体」）

| 部件 | 设置 |
|---|---|
| `Coord_Img_Aoe`（左槽） | color = `#3f4447` |
| `Coord_Img_Monomer`（右槽） | color = `#9c660a` |
| `Img_Attack` | visible = true, color = `#9c660a` |
| 其余 IntentionIcon | visible = false |
| `Txt_figure` | "2x5"，字魂扁桃体，颜色 `#9c660a` |

### 示例 2：远程 AOE 攻击 1×8（敌人当前索敌「后排·AOE」）

| 部件 | 设置 |
|---|---|
| `Coord_Img_Aoe`（左槽·后排） | color = `#9c660a`（激活） |
| `Coord_Img_Monomer`（右槽·前排） | color = `#3f4447` |
| `Img_Remote` | visible = true, color = `#9c660a` |
| 其余 IntentionIcon | visible = false |
| `Txt_figure` | "1x8" |

### 示例 3：施加 2 层易伤（敌人当前索敌「前排·单体」）

| 部件 | 设置 |
|---|---|
| `Coord_Img_Aoe`（左槽） | color = `#3f4447` |
| `Coord_Img_Monomer`（右槽） | color = `#9c660a` |
| `Img_State` | visible = true, color = `#9c660a` |
| `Txt_figure` | "2层"（待格式确认） |

### 示例 4：意图被封印（无法执行）

| 部件 | 设置 |
|---|---|
| `Coord` 组 | 视当前索敌按 §1.4 上色 |
| `Img_Think` | visible = true, color = `#9c660a` |
| `Txt_figure` | 留空或隐藏 |

---

## 7. 调色板速查

| 名称 | HEX | 用途 |
|---|---|---|
| Intent Gold | `#9c660a` | 激活色 / 图标主色 / 文字主色 |
| Inactive Gray | `#3f4447` | Coord 未激活色 |

> 建议在引擎内将这两个色值抽成常量（如 `Colors.IntentActive` / `Colors.IntentInactive`），方便后期主题切换。
