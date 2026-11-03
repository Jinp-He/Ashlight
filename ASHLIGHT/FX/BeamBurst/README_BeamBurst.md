# BeamBurst · 白色光束扩散特效（像素风）

移植自网页预览《光束扩散特效_预览.html》，参数与数学一一对应，可直接进 Godot 4 工程。

## 文件
- `BeamBurst.gd` —— 主节点脚本（`Node2D`，`class_name BeamBurst`）
- `BeamBurst.tscn` —— 已挂脚本的场景，直接拖进场景树即可
- `README_BeamBurst.md` —— 本说明

## 集成方式（任选其一）
1. **代码动态创建**
   ```gdscript
   var b := preload("res://FX/BeamBurst/BeamBurst.gd").new()
   add_child(b)
   b.position = Vector2(640, 360)   # 特效中心点
   b.angle_deg = 30.0               # 可选：旋转角度
   b.play()
   ```
2. **场景里拖 `BeamBurst.tscn`**，在检查器调参，勾 `autoplay` 让其进场景自动播放；
   运行时也可 `get_node("BeamBurst").play()` / `.stop()`。

## 可调参数（@export，对应网页 12 项）
| 参数 | 默认 | 说明 |
|------|------|------|
| `pixel_size` | 5 | 像素块大小，量化网格，越大越像素风 |
| `angle_deg` | 0 | 旋转角度（度） |
| `length` | 100 | 最大长度（全长，px） |
| `thickness` | 25 | 垂直厚度（全厚，px） |
| `burst` | 10 | 扩散缓动，越大首爆越快 |
| `duration_ms` | 800 | 持续时间（ms） |
| `alpha` | 1.0 | 整体亮度 |
| `core` | 0.29 | 核心加亮（中心亮线权重 0~1） |
| `particle_count` | 10 | 粒子数量（开场一次性迸发） |
| `particle_speed` | 0.60 | 粒子速度倍率 |
| `particle_bias` | 0.0 | 0=全向点缀，1=强轴向(向两边)扩散 |
| `loop` | true | 循环播放 |
| `autoplay` | false | 进场景自动播放 |

运行时改参数后下一次 `play()` 生效；`angle_deg` / `pixel_size` 等也会实时影响下一轮。

## 数学映射（与网页一致）
- 扩散：`expand = 1 - (1-p)^burst`，`hx = length * expand`，`hy = thickness`
- 主透明度：`a = (1 - p/0.95)^1.3 * alpha`（扩散同时淡出至消失）
- **光束外观**（网页 `gx`/`gy` 线性渐变）：
  - 轴向（`ux = |x|/hx`）：`ux ≤ 0.48` 实心 → `0.48~0.68` 线性收口到 0
  - 垂直（`v = |y|/hy`）：`v ≤ 0.16` 实心 → `0.16~0.40` 线性收口到 0
  - 即 `ax = clamp((0.68-ux)/0.20)`、`vy = clamp((0.40-v)/0.24)`
- **热核**（网页 `gx2`/`gy2`，`ch = 0.5·hy`）：
  - 轴向：`ux ≤ 0.40` 实心 → `0.60` 收口；权重 `core`
  - 垂直（`w = |y|/ch`）：`w ≤ 0.30` 实心 → `1.00` 收口
- 像素化：光束纹理按 `pixel_size` 分块（块内取中心值填满整块）、粒子位置与尺寸都 snap 到网格 → 方块
- 粒子：开场一次性生成，之后按阻尼 `0.985^(dt*60)` 外飞，尺寸 `(1-p*0.92)` 缩小、`(1-p)` 透明淡出

> **修正记录（2026-10-01）**：早期版本把网页渐变的 stop 位置误当成 `|coord|/h` 比值，导致遮罩在中心为 0、边缘为 1 —— 光束呈**空心/发灰**（中心发暗、两边亮）。已改为按「对称实心带 + 线性收口」正确换算（见上），光束恢复为 **实心亮带 + 更亮的中心热核**，与网页一致。预览见 `Preview_BeamBurst_Horizontal.png` / `Preview_BeamBurst_FirstCell_Vertical.png`。

## 渲染说明
- 光束：程序生成的 `Image`/`ImageTexture`，由 `Sprite2D` 显示，`CanvasItemMaterial.blend_mode = ADD`（发光叠加），`rotation_degrees = angle_deg`。
- 粒子：节点 `_draw()` 用 `draw_rect` 方块 + `BLEND_MODE_ADD`，随进度缩小/淡出。

## 像素化（可选进阶）
当前实现靠「分块纹理 + 方块粒子」获得像素观感。若需要整屏硬像素、更彻底的马赛克，可把 `BeamBurst` 包进一个 `SubViewport`（尺寸按 `pixel_size` 缩小），再用一个 `TextureRect`（`texture = SubViewport`，`texture_filter = TEXTURE_FILTER_NEAREST`，`stretch_mode = TextureRect.STRETCH_SCALE`）放大到全屏。

## 性能提示
- 光束纹理每帧 `fill_rect` 更新（步长 = `pixel_size`，约 1300 次/帧，开销很低）。
- 若粒子/光束长时间静止（如 `loop=false` 播放结束后），`_process` 在 `_playing=false` 时直接 return，不再重绘。
- 大量实例（如同时数十个）建议把纹理更新改为「进度变化时再更新」的缓存策略，本实现为简洁每帧更新。

## 未验证项
- 脚本按 Godot 4.x GDScript 编写，建议在引擎内打开 `BeamBurst.tscn` 确认无报错（首次打开会自动生成 `.uid`）。
- 若 `Image.fill_rect` / `CanvasItemMaterial.BLEND_MODE_ADD` 在你的 Godot 小版本签名有差异，按引擎提示微调即可。
