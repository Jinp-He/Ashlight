using System.Collections.Generic;
using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using UnityEngine;

namespace Ashlight.Battle.Core.Commands
{
    /// <summary>
    /// 周周 v3 的组合型卡牌命令。
    /// 基础伤害、移动、护甲仍复用通用 Command；只有“多个对象 + 本回合状态”的组合规则集中在这里。
    /// </summary>
    public sealed class GrantOwnerBuffCommand : ICommand
    {
        public string BuffId { get; }
        public float Value { get; }
        public GrantOwnerBuffCommand(string buffId, float value)
        {
            BuffId = buffId;
            Value = value;
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            new BuffCommand(BuffId, Value).Execute(state, ownerId, ownerId);
        }

        public int GetPriority() => 50;
        public string GetCommandType() => "GrantOwnerBuff";
        public ICommand Clone() => new GrantOwnerBuffCommand(BuffId, Value);
    }

    public sealed class MoveTwiceThenDodgeCommand : ICommand
    {
        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state?.GetUnitById(ownerId);
            if (owner == null || owner.IsDead) return;

            var beforeFirst = owner.RowPosition;
            new MoveSelfCommand("Toggle").Execute(state, ownerId, ownerId);
            bool firstMoved = owner.RowPosition != beforeFirst;
            if (!firstMoved || state.IsBattleEnded) return;

            var beforeSecond = owner.RowPosition;
            new MoveSelfCommand("Toggle").Execute(state, ownerId, ownerId);
            if (owner.RowPosition != beforeSecond)
            {
                new BuffCommand("Dodge", 1).Execute(state, ownerId, ownerId);
            }
        }

        public int GetPriority() => 90;
        public string GetCommandType() => "MoveTwiceThenDodge";
        public ICommand Clone() => new MoveTwiceThenDodgeCommand();
    }

    public sealed class MoveSelfAndAllyDefenseCommand : ICommand
    {
        public int Defense { get; }
        public MoveSelfAndAllyDefenseCommand(int defense) => Defense = Mathf.Max(0, defense);

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state?.GetUnitById(ownerId);
            var ally = state?.GetUnitById(targetId);
            if (owner == null || ally == null || owner.IsDead || ally.IsDead || owner.UnitId == ally.UnitId
                || owner.IsPlayerUnit != ally.IsPlayerUnit)
            {
                return;
            }

            new MoveSelfCommand("Toggle").Execute(state, ownerId, ownerId);
            if (!state.IsBattleEnded)
            {
                new MovePositionCommand("Toggle").Execute(state, ownerId, ally.UnitId);
            }
            if (state.IsBattleEnded) return;

            owner.AddDefense(Defense);
            ally.AddDefense(Defense);
        }

        public int GetPriority() => 90;
        public string GetCommandType() => "MoveSelfAndAllyDefense";
        public ICommand Clone() => new MoveSelfAndAllyDefenseCommand(Defense);
    }

    public sealed class MoveSelfThenDrawIfMoraleCommand : ICommand
    {
        public int RequiredMorale { get; }
        public int DrawCount { get; }
        public MoveSelfThenDrawIfMoraleCommand(int requiredMorale, int drawCount)
        {
            RequiredMorale = Mathf.Max(0, requiredMorale);
            DrawCount = Mathf.Max(0, drawCount);
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            new MoveSelfCommand("Toggle").Execute(state, ownerId, ownerId);
            var owner = state?.GetUnitById(ownerId);
            if (owner?.GetBuff("Morale")?.Value >= RequiredMorale && DrawCount > 0)
            {
                new DrawCommand(DrawCount).Execute(state, ownerId, ownerId);
            }
        }

        public int GetPriority() => 90;
        public string GetCommandType() => "MoveSelfThenDrawIfMorale";
        public ICommand Clone() => new MoveSelfThenDrawIfMoraleCommand(RequiredMorale, DrawCount);
    }

    public sealed class MoveSelfIfMoraleSpentCommand : ICommand
    {
        private readonly bool _moraleSpent;
        public MoveSelfIfMoraleSpentCommand(bool moraleSpent) => _moraleSpent = moraleSpent;

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (_moraleSpent)
            {
                new MoveSelfCommand("Toggle").Execute(state, ownerId, ownerId);
            }
        }

        public int GetPriority() => 90;
        public string GetCommandType() => "MoveSelfIfMoraleSpent";
        public ICommand Clone() => new MoveSelfIfMoraleSpentCommand(_moraleSpent);
    }

    public sealed class GrantTeamArmorAndConsumeMoraleCommand : ICommand
    {
        public int BaseArmor { get; }
        public int ArmorPerMorale { get; }
        public GrantTeamArmorAndConsumeMoraleCommand(int baseArmor, int armorPerMorale)
        {
            BaseArmor = Mathf.Max(0, baseArmor);
            ArmorPerMorale = Mathf.Max(0, armorPerMorale);
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state?.GetUnitById(ownerId);
            if (owner == null || owner.IsDead) return;

            int morale = Mathf.Max(0, Mathf.FloorToInt(owner.GetBuff("Morale")?.Value ?? 0f));
            if (morale > 0) owner.RemoveBuff("Morale");
            int armor = BaseArmor + morale * ArmorPerMorale;
            var allies = owner.IsPlayerUnit ? state.GetAlivePlayerUnits() : state.GetAliveEnemyUnits();
            foreach (var ally in allies)
            {
                ally.AddDefense(armor);
            }
        }

        public int GetPriority() => 100;
        public string GetCommandType() => "GrantTeamArmorAndConsumeMorale";
        public ICommand Clone() => new GrantTeamArmorAndConsumeMoraleCommand(BaseArmor, ArmorPerMorale);
    }

    public sealed class CreateFlashbackOfLastMoveCommand : ICommand
    {
        public int EnergyOverride { get; }
        public CreateFlashbackOfLastMoveCommand(int energyOverride) => EnergyOverride = Mathf.Max(0, energyOverride);

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state?.DeckSystem == null || state.LastMoveMainCardByOwner == null
                || !state.LastMoveMainCardByOwner.TryGetValue(ownerId, out string cardId)
                || string.IsNullOrEmpty(cardId))
            {
                Debug.Log("[CreateFlashbackOfLastMove] 没有可复制的移动主卡");
                return;
            }

            var owner = state.GetUnitById(ownerId);
            int added = state.DeckSystem.AddCardToHand(cardId, 1, owner?.GetCharacterId(), isFlashback: true, energyOverride: EnergyOverride);
            Debug.Log($"[CreateFlashbackOfLastMove] {ownerId} 获得 {cardId} 的闪回副本 x{added}");
        }

        public int GetPriority() => 40;
        public string GetCommandType() => "CreateFlashbackOfLastMove";
        public ICommand Clone() => new CreateFlashbackOfLastMoveCommand(EnergyOverride);
    }

    public sealed class MakeMoveCardsFreeAndAddStepCommand : ICommand
    {
        public int StepCount { get; }
        public MakeMoveCardsFreeAndAddStepCommand(int stepCount) => StepCount = Mathf.Max(0, stepCount);

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            if (state?.DeckSystem == null) return;
            if (state.MoveCardCostOverrideByOwner == null)
                state.MoveCardCostOverrideByOwner = new Dictionary<string, int>();
            state.MoveCardCostOverrideByOwner[ownerId] = 0;
            var owner = state.GetUnitById(ownerId);
            state.DeckSystem.AddCardToHand("Extra005", StepCount, owner?.GetCharacterId());
        }

        public int GetPriority() => 40;
        public string GetCommandType() => "MakeMoveCardsFreeAndAddStep";
        public ICommand Clone() => new MakeMoveCardsFreeAndAddStepCommand(StepCount);
    }

    public sealed class RepeatAttackByOwnMoveCommand : ICommand
    {
        public int Damage { get; }
        public int MaxRepeats { get; }
        public RepeatAttackByOwnMoveCommand(int damage, int maxRepeats)
        {
            Damage = Mathf.Max(0, damage);
            MaxRepeats = Mathf.Max(0, maxRepeats);
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            int moveCount = 0;
            state?.MovesByUnitThisTurn?.TryGetValue(ownerId, out moveCount);
            int times = 1 + Mathf.Min(MaxRepeats, Mathf.Max(0, moveCount));
            for (int i = 0; i < times && state != null && !state.IsBattleEnded; i++)
            {
                new DamageCommand(Damage).Execute(state, ownerId, targetId);
            }
        }

        public int GetPriority() => 80;
        public string GetCommandType() => "RepeatAttackByOwnMove";
        public ICommand Clone() => new RepeatAttackByOwnMoveCommand(Damage, MaxRepeats);
    }

    public sealed class MoveSelectedAlliesAndGrantDodgeCommand : ICommand
    {
        public int MaxTargets { get; }
        public int Dodge { get; }
        public MoveSelectedAlliesAndGrantDodgeCommand(int maxTargets, int dodge)
        {
            MaxTargets = Mathf.Max(1, maxTargets);
            Dodge = Mathf.Max(0, dodge);
        }

        public void Execute(BattleStateSnapshot state, string ownerId, string targetId)
        {
            var owner = state?.GetUnitById(ownerId);
            if (owner == null || owner.IsDead) return;
            var selectedIds = new List<string>();
            if (!string.IsNullOrEmpty(targetId))
            {
                foreach (string id in targetId.Split('|'))
                {
                    if (!string.IsNullOrEmpty(id) && !selectedIds.Contains(id)) selectedIds.Add(id);
                }
            }

            int moved = 0;
            foreach (string selectedId in selectedIds)
            {
                if (moved >= MaxTargets || state.IsBattleEnded) break;
                var ally = state.GetUnitById(selectedId);
                if (ally == null || ally.IsDead || ally.IsPlayerUnit != owner.IsPlayerUnit) continue;
                var before = ally.RowPosition;
                new MovePositionCommand("Toggle").Execute(state, ownerId, ally.UnitId);
                if (ally.RowPosition == before) continue;
                moved++;
                if (Dodge > 0) ally.AddBuff(new BuffState { BuffId = "Dodge", Value = Dodge, RemainingDuration = -1 });
            }
        }

        public int GetPriority() => 90;
        public string GetCommandType() => "MoveSelectedAlliesAndGrantDodge";
        public ICommand Clone() => new MoveSelectedAlliesAndGrantDodgeCommand(MaxTargets, Dodge);
    }
}
