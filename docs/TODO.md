# Ashlight TODO

> 维护方式：跟 Claude 说「查看 / 更新 TODO」即可增删条目。
> 状态流转：`TODO` → `进行中` → `已完成`。完成项保留一段时间便于回顾，过多时再归档。

## 下一步（下次优先）

- [ ] **构造敌人阵列**：设计实际遭遇（`#EnemyInfo` / `#Encounter`），按两区站位铺前/后排威胁（单体前排、单体后排、AOE 前排横扫 / 后排扫射 / 全体大招都各有覆盖）。当前敌人 HP 是测试基线（冰焰法师/急袭兵仅 10 血会被秒），需按 `docs/起手卡组设计_费用模型对齐.md` §6.4 重铺：普通遭遇≈180 / 精英≈330 / Boss≈540（切相位）。
- [ ] **重新构造卡牌数值**：起手三套已按 `起手卡组设计_费用模型对齐.md` §3/§4/§5 落地，但需 Unity 实测后按 §2 定价表迭代——条件牌 / 闪避层数 / 毒层 / 破韧阈值 / 执行延迟的手感数值都待调；之后再按稀有度扩普通/罕见/稀有卡池。
- [ ] **文字丢失问题**：部分文字显示缺失（疑似 TMP 字体图集缺字 / 动态字体未包含某些字形，见 `Assets/Fonts/*`）。需补字集重新生成 SDF，或改用带 fallback 的动态字体。

## 进行中

- [ ] 升级系统（UpgradeOptions）—— 战斗胜利三选一升级
  - 已完成（待 Unity 编译 + 预制体接线验证）：
    - 数据表 `#UpgradeOptions.xlsx` + 多态 bean（`GrantBuff`/`ModifyUnitStat`/`ModifyCardStat`/`ModifyCardFlag`，见 `defines.xml`）
    - 战斗开局派生：`UpgradeEffectApplier`（贴永久buff / 改单位属性 / 写 `CardModifierRegistry` 卡牌修正），`BattleManager` 初始化第 3.5 步调用
    - 卡牌修正落地：`CardPlayResolver` 建 Command 时叠加（伤害/护甲/IsAoe）
    - 存储：`CharacterRuntimeState.AcquiredUpgrades` + `AddUpgrade/HasUpgrade`
    - 胜利前端流程：移除敌人→点选角色(indicator)→`ChoosePanel` 三选一→写升级→原结算面板
      （`UI_BattleScene.UpgradeFlow.cs` / `ChoosePanel.Logic.cs` / `UpgradeSelectionService`）
  - 待办：
    - [ ] Unity 编译 3 个新 .cs + 接 `ChoosePanel` 预制体（按钮下 `Title`/`Description` 文本、`Resources/UI/BattleScene/ChoosePanel`）
    - [ ] `ModifyCardStat` 目前只接了 Damage/Defense，`ModifyUnitStat` 只接 MaxHp/Armor/Energy；按需扩（如卡牌 Energy/ExecutingCost 需在 `TurnResolver.GetCardEnergyCost` 加 hook）
    - [ ] 确认选完升级后是否保留 WinPanel（当前保留）

## TODO

- [ ] 用动画插件（DOTween）让行动顺序卡片的翻转和加入更明显 —— 模块：`TurnOrder`

### 三职业设计落地（基准见 `docs/职业设计_三职业与公用系统.md`）

- [ ] **敌人索敌逻辑**（头号前置）：整个两区站位主题压在此。
  - [x] 数据字段：`EnemySkillInfo` 加 `TargetZone`（`TargetZoneEnum`: Front/Back/Any/Conditional），14 条敌技已按描述填好分区示例。单/群仍由 `TargetType`(SingleEnemy/AllEnemy) 管。
  - [x] 代码消费：索敌层读 `TargetZone` 选目标——载体选择+AOE扩散都按区过滤(共用 `ZoneTargeting.FilterByZone`)。玩家卡默认 `Any` 不受影响。
  - [x] **动态索敌**：目标在「执行那一刻」按当前站位重解(`BattleManager.ResolveExecutionTarget`，锁定目标显式入参)——原目标仍在区内则继续打、被移出/死亡则改选该区其他人；预告期玩家移动后 `RefreshPendingEnemyIntentTargets` 同步重锁；AOE 本就在 `DamageCommand` 执行时现算。→ 移动躲伤害真正生效。
  - [x] **敌人单轨制（砍掉思考回合）**：敌人不再跑两趟（规划亮意图→执行轨等）；改为结算后当场重报意图、退到执行轨第 `ExecutingCost` 格，走到 0 直接落地(`UI_BattleScene.DeclareEnemyIntentAndQueue`)。→ 一轮更短。**注**：`ExecutingCost` 现在=预告窗口格数，手感可能需在表里调小。
  - [x] **空排落空 miss**：`ZoneTargeting.FilterByZone(strict)` 空区不回退全体；命中时目标区空排 → `EnemyIntentResolveResult.Missed`，敌人头飘「MISS」(`BattleAnimationHandler.ShowFloatingLabel`)。群体技 `DamageCommand` 同走 strict：只打区内剩余、整排空才落空。
  - [x] **意图目标区/抛物线表达**：`IntentionView` 坐标点已表达前/后/全体；hover 意图复用 `TargetArrowRenderer` 画抛物线指向锁定角色（`TargetTransformResolver` 注入）。
  - [~] **修改卡牌设计**：三套起手 10 张已按 `docs/起手卡组设计_费用模型对齐.md` 落地（含玩家卡 `TargetZone` 分区打击、`BuffConditionalEffect` 条件毒、群体友军加甲、嘲讽索敌、闪避受击免伤+客观回合衰减）。数值迭代与扩池见「下一步·重新构造卡牌数值」。
  - [ ] 嘲讽 `Taunt` 覆盖索敌。
  - [ ] `Conditional` 的具体规则（枚举位已占，逻辑待定）。
- [~] **角色特性（百相）实装**：`CharaterInfo` 加 `Trait` 字段；游侠 `FirstMoveFree`＝本回合首张「带移动」的牌费用0（`BattleManager.IsFreeMoveForOwner`）已实装。法师双开执行仍待做。开局贴百相的通用框架（复用 `UpgradeEffectApplier`）仍待做。
- [x] **每回合注入移动牌**：`Irene000/Rocket000/Zhouzhou000`（Swift，虚无+消耗，不污染牌库），`StartPlayerTurn` 里 `InjectBasicMoveCard` 注入。各角色牌库「更多移动牌+触发」仍待做。
- [x] **过载代价（离散）**：改为「本回合过载过 → 结束回合重排 +1 格」(`UI_BattleScene` 1547)。触发＝能量不足时自动透支(每回合1次)。旧 0–100 `ApplyOverloadPenalty` 仅剩敌人 `ActionBarResolver` 路径在用，待清理。
  - [ ] 过载显式图标：目前靠 +1 格后移体现，`UI_行动顺序` 卡片加过载徽标需 Unity 侧动预制体。
  - [ ] 过载触发 UX 待定：现为「能量不足自动透支」，若要改成按钮/确认再说。
- [ ] **移动牌费用的 UI 显示**：游侠首张移动实际扣0但卡面仍显示1，需动态显示。
- [ ] **两区站位系统**：前排区/后排区（可多人），移动＝相邻交换一步，射程软处理。
- [ ] 待测：法师执行是否可被敌人打断；过载「越用越贵」是否需要；移动/毒/小刀/闪避数值。

## 已完成

- [ ] （暂无）
