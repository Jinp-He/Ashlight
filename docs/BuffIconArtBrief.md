# Buff 图标美术需求

> **用途**：战斗 UI 中显示在角色/敌人头像下方的 Buff/Debuff 状态图标。
> **配置表**：[`DataTables/Datas/#BuffInfo.xlsx`](../DataTables/Datas/%23BuffInfo.xlsx)
> **配置加载后位置**：[`Assets/Resources/Config/tbbuffinfo.json`](../Assets/Resources/Config/tbbuffinfo.json)

---

## 一、技术规格

| 项目 | 要求 |
|---|---|
| 文件格式 | PNG，带透明通道，sRGB |
| 单图尺寸 | **96 × 96 px**（容器内会缩放到 48 px 显示，预留高分辨率） |
| 安全区 | 距离边缘留 6 px，便于战斗中数字角标叠加 |
| 文件命名 | `Icon_<BuffId>.png`，BuffId 见下表（严格大小写） |
| 资源路径 | `Assets/Resources/UI/Buff/Icon_<BuffId>.png` |
| 命名空间 | 不带子文件夹，全部平铺在 `UI/Buff/` 下 |

**Polarity 视觉区分**（图标本体不画框，框由 UI 层根据 Polarity 字段动态加）：
- **Buff（增益）**：图标主色偏暖（金/橙/绿）
- **Debuff（减益）**：图标主色偏冷（蓝/紫/灰）或加破损/锁链元素
- **Neutral（中性）**：单色描线，无明显冷暖倾向

---

## 二、图标清单（共 21 个，按优先级排序）

> 引擎内部状态 `Channeling` / `Recoil` 暂不需要图标（不在头像下显示，由时间轴自身的进度条/格子表达）。

### P0 — 试玩必需（10 个）

控制 / 数值类核心 buff，是首发关卡就会出现的：

| BuffId | 显示名 | Polarity | 含义 | 视觉建议 |
|---|---|---|---|---|
| `Stun` | 晕眩 | Debuff | 无法行动 | 头顶旋转的星星 / 螺旋眩晕符号 |
| `Vulnerable` | 易伤 | Debuff | 受到伤害 +X% | 破裂的盾 / 心脏裂口 / 红色锁定靶心 |
| `ReduceDmg` | 减伤 | Buff | 受到伤害 -X% | 蓝色光罩 / 半透明护壁 |
| `Strength` | 力量 | Buff | 攻击伤害 +X | 紧握拳头 / 上扬的剑刃 / 红色向上箭头 |
| `Weak` | 虚弱 | Debuff | 造成伤害 −X% | 折断的剑 / 弯曲的拳头 / 灰色向下箭头 |
| `Stagger` | 破韧 | Debuff | 累计承受 X 伤害后被打断 | 龟裂护甲 / 裂痕条 / 红色破碎进度环 |
| `Block` | 格挡 | Debuff | 受到 X 次伤害后被打断 | 剩余打击次数 / 破裂的盾牌 + 数字感 |
| `Poison` | 中毒 | Debuff | 每回合 V 伤，V −1 衰减 | 绿色毒滴 / 骷髅 + 绿色烟雾 |
| `Burn` | 燃烧 | Debuff | 每回合固定 V 伤 | 橙红色火焰 / 焰尖 |
| `Taunt` | 嘲讽 | Buff | 敌人优先攻击我 | 怒吼面具 / 喇叭 / 红色标靶（友方加成色） |

### P1 — 完整体验（11 个）

进阶机制 / 资源类，先 P0 顺到后再做：

| BuffId | 显示名 | Polarity | 含义 | 视觉建议 |
|---|---|---|---|---|
| `Taunted` | 被嘲讽 | Debuff | 必须攻击嘲讽者 | 视线被强制牵引 / 红色绳索从眼睛拉出 |
| `Chill` | 冰冻 | Debuff | 行动延迟（可叠 3 层） | 冰晶/雪花，叠层时颜色逐层加深 |
| `Root` | 禁锢 | Debuff | 无法移动 | 树根/锁链缠绕双足 |
| `Frail` | 脆弱 | Debuff | 获得护甲 −X% | 破裂的盾 + 向下箭头 / 破碎鳞片 |
| `Dexterity` | 敏捷 | Buff | 防御牌 +X 护甲 | 翅膀/羽毛 + 上升箭头 + 盾 |
| `Regen` | 再生 | Buff | 回合开始回血 V | 绿色十字 / 心脏 + 上升箭头 |
| `Energized` | 充能 | Buff | 下回合 +V 能量（可叠） | 闪电 / 电池 / 旋转能量球 |
| `ReduceChannel` | 引导缩短 | Buff | 下次引导 −X 格 | 沙漏加速 / 时钟向后箭头 |
| `CardCost` | 减费 | Buff | 下张牌 −V 费 | 卡片 + 向下箭头 / 折扣标签 |
| `DrawCard` | 灵感 | Buff | 多抽 X 张（可叠 3 层） | 卡片飞出 / 羽毛笔 + 灵光 |
| `Artifact` | 圣物 | Buff | 抵消下次 debuff（剩 V 次） | 圆形宝石 / 神圣光环 / 护身符 |

> **关于角标显示的特殊说明**：
> - **累计型**（`Stagger` / `Block`）：角标显示 `当前/阈值`（如 `35/50`），图标可预留一圈进度环位置。
> - **层数型 Value**（`Poison` / `Strength` / `Artifact`）：角标直接显示当前 Value 数值，不显示回合数。
> - **常规型**（其他 buff）：右下角显示剩余回合数，回合数 = -1（永久）不显示。
>
> 图标本体一律**不要画数字、不要带描边框**，全部由 UI 层动态叠加。

---

## 三、角标规则（程序方负责，美术无需画）

UI 层会在图标右下角自动绘制：

- **持续回合数**：白色数字，黑描边。`-1`（永久）不显示数字。
- **叠加层数**：仅当 `MaxStack > 1` 且当前层数 ≥ 2 时显示，黄色数字带 `×` 前缀（如 `×3`）。
- **Polarity 框**：图标外圈一个 2 px 描边，颜色由 Polarity 决定。

因此交付的图标本体**不要包含数字、不要带描边框**。

---

## 四、风格参考

> TODO（设计/美术补充）：贴 2-3 张参考图，明确线条粗细 / 色彩饱和度 / 是否扁平化 / 是否带轻微光效。
>
> 建议方向：单色调 + 强符号识别度（参考 Slay the Spire / Hades 的 buff icon 风格），保证缩到 48 px 仍可读。

---

## 五、交付检查清单

- [ ] 21 张 PNG，命名严格匹配 `Icon_<BuffId>.png`
- [ ] 全部 96×96，sRGB，带 alpha
- [ ] 同光照方向（建议左上 45° 主光）
- [ ] 同尺寸主体（主体外接矩形约 72×72，居中）
- [ ] 缩放到 48 px 后仍可分辨
- [ ] 单图 ≤ 30 KB（PNG-8 或压缩 PNG-24）

---

## 六、程序对接说明（备查）

资源放好后，程序侧无需改配置——`BuffInfo.IconPath` 已经按 `UI/Buff/Icon_<BuffId>` 配好。后续会通过 `Resources.Load<Sprite>(buffInfo.IconPath)` 加载。

如需调整图标路径、新增 buff 类型，修改 [`DataTables/Datas/#BuffInfo.xlsx`](../DataTables/Datas/%23BuffInfo.xlsx) 后跑 `DataTables/gen.bat` 重新生成即可。
