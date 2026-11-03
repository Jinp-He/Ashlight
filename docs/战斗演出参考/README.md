# 战斗演出预览归档说明

本目录来自 `战斗演出预览` 交付包，用于保留视觉目标、参数和原型实现。

## 已进入运行时资源目录

- 背景：`Assets/Resources/UI/BattleScene/BattleCutscene/Img_BattleBG.jpg`
- 插入特效：`Assets/Resources/UI/BattleScene/BattleCutscene/Frames/anim_CutsceneEffect_01.png` ～ `14.png`

序列帧设计播放速度为 15 FPS，玩家方向按原图播放，敌方方向水平翻转。各帧原始尺寸不同，属于交付稿设定，接入时不要批量裁成相同尺寸。

Unity 首次导入这些图片后，应检查：

- Texture Type：Sprite (2D and UI)
- Sprite Mode：Single
- Alpha Is Transparency：开启
- Max Size：至少 2048
- UI Image：关闭 Raycast Target

## 仅作参考、不会参与 Unity 编译

`Scripts` 中的代码是独立网页原型的 Unity 翻译版，内部自带另一套手牌、震屏和演出管理逻辑。Ashlight 已有 `CardViewController`、`BattleAnimation_CenterStage` 和 `BattleAnimationHandler`，因此这些脚本保存在 `docs` 下供移植算法时查阅，不直接复制到 `Assets/Scripts`。

`BattleCutscene_Preview.html` 是可视化原稿；`演出逻辑文档.md` 是完整参数和触发说明。
