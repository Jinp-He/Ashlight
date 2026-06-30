using System;
using System.Collections;
using UnityEngine;
using Ashlight.Battle.Core.Data;

/// <summary>
/// 战斗演出动画播放器统一接口。
/// 由 BattleAnimation（原地播放版）与 BattleAnimation_CenterStage（中央舞台版）共同实现，
/// 便于 BattleAnimationHandler 按需在两套演出之间切换。
/// </summary>
public interface IBattleAnimationPlayer
{
    /// <summary>
    /// 播放战斗演出动画。
    /// </summary>
    IEnumerator PlayBattleAnimation(
        UnitState casterState,
        UnitState targetState,
        MonoBehaviour casterUI,
        MonoBehaviour targetUI,
        bool isAttackCard,
        int damage,
        Action onHit);
}
