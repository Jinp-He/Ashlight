# ArcProjectile 弧线箭头飞行动效

`ArcProjectile.cs` 用于在战斗 UI 中播放从敌人意图指向锁定角色的弧线箭头。

## 效果

- 箭头头部沿二次贝塞尔曲线从 A 点飞向 B 点，并始终朝向轨迹切线。
- `ParticleSystem` 管理拖尾粒子的生命周期、透明度与尺寸衰减。
- 粒子通过 Unity UI `Graphic` 网格显示，可直接用于项目现有的 `Screen Space - Overlay` Canvas。
- 到达终点后箭头按 `fadeOutTime` 淡出，已有拖尾继续自然消失。
- 不再创建全屏 `Texture2D`，不会覆盖战斗画面，也不需要每帧 CPU 软件光栅化。

## 战斗界面触发规则

触发由 `IntentionView` 负责：

1. 敌人生成有效意图并锁定角色时，立即播放一次。
2. 玩家回合期间，每个仍有有效锁定目标的敌人意图每 `4` 秒播放一次。
3. 非玩家回合停止重复计时；重新进入玩家回合后重新计满 `4` 秒。
4. 思考态、目标丢失、意图隐藏或对象销毁时停止动画并清理粒子。

重复间隔由 `IntentionView._arrowRepeatInterval` 控制，默认 `4s`。

## ArcProjectile 参数

| 参数 | 默认值 | 说明 |
|---|---:|---|
| `arrowSize` | 13 | 箭头头部大小 |
| `flightSpeed` | 0.5s | A→B 飞行时间 |
| `fadeOutTime` | 0.25s | 到达后箭头淡出时间 |
| `arrowColor` | 红色 | 箭头及拖尾颜色 |
| `arcHeight` | 200 | 曲线控制点偏移 |
| `arcDir` | -0.7 | 正值上凸、负值下凸 |
| `trailLength` | 18 | 用于计算粒子容量；0 表示关闭拖尾 |
| `trailLife` | 0.3s | 单个拖尾粒子的存活时间 |
| `trailWidth` | 8 | 拖尾核心宽度 |
| `glow` | 5 | 拖尾外层柔光宽度 |
| `trailEmissionRate` | 60/s | 每秒发射的拖尾粒子数量 |
| `trailTaper` | 开 | 拖尾粒子随寿命缩小 |

## 公共接口

```csharp
// 使用屏幕坐标发射；兼容 Overlay 与 Camera Canvas。
projectile.Fire(startScreen, endScreen, canvas);

// 使用渲染器本地坐标发射。
projectile.FireLocal(startLocal, endLocal);

// 立即停止并清除箭头与拖尾。
projectile.StopAndClear();
```

战斗中无需手工创建 `ArcProjectile`。`IntentionView` 会在第一次播放时将它动态创建到当前 Canvas，并置于最上层且不拦截 UI 射线。
