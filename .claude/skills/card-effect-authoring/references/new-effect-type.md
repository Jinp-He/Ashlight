# 新增 effect 类型（重活，别和录数据混淆）

当没有现成 effect 类型能表达需求时才走这条路。它要改 schema 和战斗代码，影响面比录一行数据大得多。

0. **先向用户明确输出，等确认再动手**：说明「现有类型覆盖不了，需要新增 `XxxEffect`」，列出：字段、拟映射到哪个 Command、要改哪些文件（defines.xml + 战斗侧映射 + 可能的新 Command）。
1. 在 [defines.xml](../../../../DataTables/Defines/defines.xml) 加 `<bean name="XxxEffect" parent="Effect">`，按需加 `<var>`。
2. 跑导表——Luban 自动在 `Effect.cs` 的 `DeserializeEffect` switch 里注册 `case "XxxEffect"` 并生成 `Assets/Gen/XxxEffect.cs`。
3. **加战斗映射（关键，最容易漏，漏了就是哑效果）**：在 `ConvertEffectToCommand` 里加 `if (effect is XxxEffect e) return new XxxCommand(...)`。这段逻辑有**三份**，按谁会用到这个 effect 决定改哪几处：
   - [CardPlayResolver.cs](../../../../Assets/Scripts/Battle/Core/Engine/CardPlayResolver.cs) —— **玩家卡牌**当前 ATB 路径（用到必改）
   - [EnemySkillToTimelineConverter.cs](../../../../Assets/Scripts/Battle/Core/Engine/EnemySkillToTimelineConverter.cs) —— **敌人技能**当前路径（敌技用到必改）
   - [CardToTimelineConverter.cs](../../../../Assets/Scripts/Battle/Core/Engine/CardToTimelineConverter.cs) —— 旧时间轴路径（一般不用改）
   位移类 effect：三处 `ActionBarShiftCommand` 传参已统一同号（正数 = 延后 N 公共回合，经 `UnitState.PendingRoundDelay` 由 ATB 落账）。
4. 若需要新 Command，在 `Assets/Scripts/Battle/Core/Commands/` 下新建（参考同目录现有 Command）。
5. **登记进 [effect-catalog.md](effect-catalog.md)**：类型名、字段顺序、单元格示例、战斗映射。catalog 是速查表，漏登记下次录数据找不到；`validate_tables.py` 对照 defines.xml 和战斗代码动态解析，无需改。
6. 回 Unity 确认 `Assets/Gen/` 能编译，然后按 SKILL.md 标准流程把新类型录进 Excel。
