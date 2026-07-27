using System;
using Ashlight.Battle.Core.Data;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    public abstract class IreneTimelineCommandBase : ICommand
    {
        public abstract void Execute(BattleStateSnapshot state, string ownerId, string targetId);
        public virtual int GetPriority() => 60;
        public abstract string GetCommandType();
        public abstract ICommand Clone();

        protected static Ashlight.Battle.BattleManager LiveManager(BattleStateSnapshot state)
        {
            var manager = Ashlight.Battle.BattleManager.Instance;
            return manager != null && ReferenceEquals(manager.CurrentState, state) ? manager : null;
        }
    }

    public sealed class CastShiftCommand : IreneTimelineCommandBase
    {
        public int ShiftValue { get; }
        public CastShiftCommand(int shiftValue) => ShiftValue = shiftValue;
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.ShiftPendingCast(targetId, ownerId, ShiftValue);
        public override string GetCommandType() => "CastShift";
        public override ICommand Clone() => new CastShiftCommand(ShiftValue);
    }

    public sealed class CastDamageBonusCommand : IreneTimelineCommandBase
    {
        public int DamageBonus { get; }
        public CastDamageBonusCommand(int damageBonus) => DamageBonus = damageBonus;
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.AddPendingCastDamageBonus(targetId, ownerId, DamageBonus);
        public override string GetCommandType() => "CastDamageBonus";
        public override ICommand Clone() => new CastDamageBonusCommand(DamageBonus);
    }

    public sealed class CastResolveDrawCommand : IreneTimelineCommandBase
    {
        public int Count { get; }
        public CastResolveDrawCommand(int count) => Count = count;
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.AddPendingCastResolveDraw(targetId, ownerId, Count);
        public override string GetCommandType() => "CastResolveDraw";
        public override ICommand Clone() => new CastResolveDrawCommand(Count);
    }

    public sealed class CastResolveBuffCommand : IreneTimelineCommandBase
    {
        public string BuffId { get; }
        public float Value { get; }
        public CastResolveBuffCommand(string buffId, float value) { BuffId = buffId; Value = value; }
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.AddPendingCastResolveBuff(targetId, ownerId, BuffId, Value);
        public override string GetCommandType() => "CastResolveBuff";
        public override ICommand Clone() => new CastResolveBuffCommand(BuffId, Value);
    }

    public sealed class CastImmediateCommand : IreneTimelineCommandBase
    {
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.MarkPendingCastImmediate(targetId, ownerId);
        public override string GetCommandType() => "CastImmediate";
        public override ICommand Clone() => new CastImmediateCommand();
    }

    public sealed class CastEchoCommand : IreneTimelineCommandBase
    {
        public int Delay { get; }
        public float Multiplier { get; }
        public CastEchoCommand(int delay, float multiplier) { Delay = delay; Multiplier = multiplier; }
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.AddPendingCastEcho(targetId, ownerId, Delay, Multiplier);
        public override string GetCommandType() => "CastEcho";
        public override ICommand Clone() => new CastEchoCommand(Delay, Multiplier);
    }

    public sealed class CastShiftAllCommand : IreneTimelineCommandBase
    {
        public int ShiftValue { get; }
        public CastShiftAllCommand(int shiftValue) => ShiftValue = shiftValue;
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
            => LiveManager(state)?.ShiftAllPendingCasts(ownerId, ShiftValue);
        public override string GetCommandType() => "CastShiftAll";
        public override ICommand Clone() => new CastShiftAllCommand(ShiftValue);
    }

    public sealed class WeatherConditionalDamageCommand : IreneTimelineCommandBase
    {
        public int Damage { get; }
        public float Multiplier { get; }
        public bool IsAoe { get; }
        public cfg.TargetZoneEnum TargetZone { get; set; } = cfg.TargetZoneEnum.Any;
        public WeatherConditionalDamageCommand(int damage, float multiplier, bool isAoe)
        { Damage = damage; Multiplier = multiplier; IsAoe = isAoe; }
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            int value = state != null && state.LastWeatherResolvedRound == state.CurrentRound
                ? Mathf.RoundToInt(Damage * Multiplier)
                : Damage;
            new DamageCommand(Mathf.Max(0, value), IsAoe) { TargetZone = TargetZone }
                .Execute(state, ownerId, targetId);
        }
        public override int GetPriority() => 80;
        public override string GetCommandType() => "WeatherConditionalDamage";
        public override ICommand Clone() => new WeatherConditionalDamageCommand(Damage, Multiplier, IsAoe) { TargetZone = TargetZone };
    }

    public sealed class WeatherSyncEnergyCommand : IreneTimelineCommandBase
    {
        public int Value { get; }
        public WeatherSyncEnergyCommand(int value) => Value = value;
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state == null || state.NextWeatherRound < state.CurrentRound) return;
            var manager = LiveManager(state);
            if (manager != null && manager.HasFriendlyPendingCastAtRound(ownerId, state.NextWeatherRound))
                new EnergyCommand(Value).Execute(state, ownerId, ownerId);
        }
        public override int GetPriority() => 40;
        public override string GetCommandType() => "WeatherSyncEnergy";
        public override ICommand Clone() => new WeatherSyncEnergyCommand(Value);
    }

    public sealed class AlignToWeatherCommand : IreneTimelineCommandBase
    {
        public int MaxShift { get; }
        public AlignToWeatherCommand(int maxShift) => MaxShift = Mathf.Max(0, maxShift);
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var target = state?.GetUnitById(targetId);
            if (target == null || target.IsDead || target.NextActionRound < 0 || state.NextWeatherRound < state.CurrentRound)
                return;

            int projected = target.NextActionRound + target.PendingRoundDelay;
            int distance = state.NextWeatherRound - projected;
            if (distance == 0) return;
            int shift = Math.Sign(distance) * Math.Min(Math.Abs(distance), MaxShift);
            target.PendingRoundDelay += shift;
            Debug.Log($"[AlignToWeatherCommand] {targetId} 向天气回合 {state.NextWeatherRound} 移动 {shift} 格");
        }
        public override string GetCommandType() => "AlignToWeather";
        public override ICommand Clone() => new AlignToWeatherCommand(MaxShift);
    }

    public sealed class WeatherGuardCommand : IreneTimelineCommandBase
    {
        public int Armor { get; }
        public WeatherGuardCommand(int armor) => Armor = Mathf.Max(0, armor);
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state == null || state.NextWeatherRound < state.CurrentRound || Armor <= 0) return;
            state.WeatherGuardRound = state.NextWeatherRound;
            state.WeatherGuardArmor += Armor;
        }
        public override int GetPriority() => 100;
        public override string GetCommandType() => "WeatherGuard";
        public override ICommand Clone() => new WeatherGuardCommand(Armor);
    }

    public sealed class WeatherShiftCommand : IreneTimelineCommandBase
    {
        public int ShiftValue { get; }
        public WeatherShiftCommand(int shiftValue) => ShiftValue = shiftValue;
        public override void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state != null && state.NextWeatherRound >= state.CurrentRound)
                state.PendingWeatherDelay += ShiftValue;
        }
        public override string GetCommandType() => "WeatherShift";
        public override ICommand Clone() => new WeatherShiftCommand(ShiftValue);
    }
}
