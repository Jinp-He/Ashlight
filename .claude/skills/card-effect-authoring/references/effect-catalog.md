# Effect 类型速查表

权威来源：[defines.xml](../../../../DataTables/Defines/defines.xml)（字段定义）+ [Effect.cs](../../../../Assets/Gen/Effect.cs)（`DeserializeEffect` 注册）+ `CardPlayResolver.cs` / `CardToTimelineConverter.cs`（战斗映射）。此表是人读的摘要，字段以 defines.xml 为准。

## EffectType 枚举（单元格第 2 段）

| 缩写 | 枚举 | 值 |
|---|---|---|
| A | Attack | 0 |
| D | Defense | 1 |
| T | TimeSlot | 2 |
| H | Heal | 3 |
| N | Null | 4 |
| B | Buff | 5 |
| F | Fill | 6 |

`Null`（N）用于本身不属于攻/防/治疗/位移分类的效果（如 MovePosition、AddToHand、Taunt 等），取哪个值参考现有同类行。

## Effect 子类

字段顺序 = 单元格里 EffectType 之后各段的顺序。**战斗映射**列标注该 effect 在 `ConvertEffectToCommand` 里是否有对应 Command——`—` 表示当前**没有映射，导表能过但战斗中不生效**，用到前需确认是否在别处处理或需要补映射。

| 类型名 | 字段（顺序） | 单元格示例 | 战斗映射 (→Command) |
|---|---|---|---|
| `AttackEffect` | damage:int, is_aoe:bool | `AttackEffect,A,12,false` | `DamageCommand` |
| `AttackExtraEffect` | damage:int, conditions:string(用`|`), multiplier:float | `AttackExtraEffect,A,9,Channeling|Recoil,2.0` | — |
| `AttackConditionalEffect` | bonus_damage:int, condition_type:string | `AttackConditionalEffect,A,5,IsAttacking` | `AttackConditionalCommand` |
| `DefenseEffect` | value:int, per_hit:bool | `DefenseEffect,D,3,true` | `DefenseCommand` |
| `DefenseConditionalEffect` | value:int, condition_type:string | `DefenseConditionalEffect,D,4,SelfInFrontRow` | `DefenseConditionalCommand` |
| `InterceptEffect` | shield_value:int | `InterceptEffect,D,16` | — |
| `HealEffect` | value:int | `HealEffect,H,8` | `HealCommand` |
| `ClearRecoilEffect` | (无) | `ClearRecoilEffect,N` | — |
| `PushCollisionEffect` | shift_value:int, collision_result:string | `PushCollisionEffect,T,1,Stun` | `ActionBarShiftCommand`（单体，注意符号） |
| `TimeShiftAllEffect` | shift_value:int | `TimeShiftAllEffect,T,2` | `ActionBarShiftCommand`（AOE） |
| `CollisionEffect` | result:string | `CollisionEffect,N,Stun` | — |
| `SwiftEffect` | (无) | `SwiftEffect,N` | — |
| `MovePositionEffect` | mode:string (Toggle/FrontRow/BackRow) | `MovePositionEffect,N,Toggle` | `MovePositionCommand` |
| `AddToHandEffect` | card_id:string, count:int | `AddToHandEffect,N,Rocket001,1` | `AddToHandCommand` |
| `ChannelEffect` | duration:int | `ChannelEffect,N,3` | — |
| `TauntEffect` | target:string (如 All) | `TauntEffect,N,All` | — |
| `BuffEffect` | buff_id:string, value:float | `BuffEffect,B,ReduceDmg,0.5` | `BuffCommand` |
| `BuffConditionalEffect` | buff_id:string, value:float, condition_type:string | `BuffConditionalEffect,F,Poison,2,MovedThisTurn` | `BuffConditionalCommand`（条件满足才贴 buff；条件目前支持 `MovedThisTurn`＝施法者本回合移动过） |
| `DrawEffect` | count:int | `DrawEffect,F,1` | `DrawCommand`（即时抽牌，非 buff；描述用 `{V}` 需 EffectType=F） |

> 示例里 EffectType 缩写（第 2 段）是常见取值，个别 effect 的实际取值请对照该表现有行——同类 effect 保持一致即可。

## 多态判别符对照

| 位置 | 判别符写法 |
|---|---|
| Excel 单元格 | 首段 `AttackEffect,...` |
| 生成的 json | `"$type": "AttackEffect"` |
| 生成的 C# | `Effect.cs` 的 `case "AttackEffect": return new AttackEffect(_buf);` |

三者的类型名必须完全一致，新增/改名时同步。

## UpgradeEffect（升级选项，另一套多态体系）

`UpgradeOptions.Effects` 用的是**独立**的 `UpgradeEffect` 基类（不是上面的 `Effect`），子类：`GrantBuff` / `ModifyUnitStat` / `ModifyCardStat` / `ModifyCardFlag`。定义见 defines.xml 末段。若需求是「胜利升级三选一 / 百相」的效果，用这套而非上面的 Effect。字段：

| 类型名 | 字段（顺序） | 示例 |
|---|---|---|
| `GrantBuff` | buff_id:string, stack:int, target:TargetTypeEnum | `GrantBuff,Strength,3,Self` |
| `ModifyUnitStat` | stat:string(MaxHp/Armor/Energy), delta:int, target:TargetTypeEnum | `ModifyUnitStat,MaxHp,5,AllAlly` |
| `ModifyCardStat` | selector:string, stat:string, delta:int | `ModifyCardStat,Rocket001,Damage,5` |
| `ModifyCardFlag` | selector:string, flag:string, value:bool | `ModifyCardFlag,CardType:Swift,IsAoe,true` |
