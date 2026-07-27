# 火箭卡牌：特殊效果实现说明

原待办清单中的效果现已全部接入运行时，本文保留为实现口径说明。

## 蓄力

- `Rocket006`、`Rocket010`、`Rocket016`、`Rocket018` 使用独立的 `Charge` 卡牌类型。
- 蓄力牌固定为 0 费；每名角色每次行动至多打出一张，同时只能维持一张待结算蓄力牌。
- 蓄力在该角色下次行动开始时完成；层数为开始与完成之间经过的公共回合数，至少为 1。行动被推迟会增加层数。
- `ChargeStartEffects` 在打出时结算，`ChargeWhileEffects` 只在蓄力期间保留，普通 `Effects` 在完成时结算。
- `Rocket006` 立即获得护甲，蓄力期间获得嘲讽，完成或施法者失效时移除蓄力提供的嘲讽。
- `Rocket018` 使用 `ChargedAttackEffect`，按每层伤害乘蓄力层数，并继续经过力量、虚弱、易伤和护甲结算。

## 其他特殊效果

- `Rocket007`：`AddRandomToHandEffect` 从 `Extra002`、`Extra003`、`Extra004` 中无重复随机生成；后排数量翻倍。
- `Rocket009`：`AllyActingThisRound` 在除施法者外有存活友方于当前公共回合行动时成立。
- `Rocket016`：`MoveOnArmorBreakEffect` 注册一次性护甲耗尽触发，护甲从正数降为 0 时移动至后排。
- `Rocket019`：群体护甲的 `TargetZone=Conditional` 在结算时解析为施法者当前所在排。

三张小发明均为 0 费、临时、虚无、消耗牌，不进入奖励池或永久牌库。
