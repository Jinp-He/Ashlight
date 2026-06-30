# Ashlight TODO

> 维护方式：跟 Claude 说「查看 / 更新 TODO」即可增删条目。
> 状态流转：`TODO` → `进行中` → `已完成`。完成项保留一段时间便于回顾，过多时再归档。

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

## 已完成

- [ ] （暂无）
