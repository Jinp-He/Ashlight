using System.Collections;
using Ashlight.Battle.Prototype;

namespace Scripts.UI
{
    public partial class UI_BattleScene
    {
        private bool _tempoPrototypeAutoEnding;

        /// <summary>
        /// 卡牌 UI 完成消耗/刷新后调用。原型模式下等待一帧让本次演出完成，再自动结束角色行动。
        /// </summary>
        public void OnTempoPrototypeCardPlayed(string ownerUnitId)
        {
            if (!TempoPrototypeMode.IsActive
                || _tempoPrototypeAutoEnding
                || !TempoPrototypeMode.HasPlayedCard(ownerUnitId))
            {
                return;
            }

            _tempoPrototypeAutoEnding = true;
            StartCoroutine(AutoEndTempoPrototypeTurn());
        }

        private IEnumerator AutoEndTempoPrototypeTurn()
        {
            yield return null;

            if (IsPlayerTurnActive())
            {
                yield return EndRoundCoroutine();
            }

            _tempoPrototypeAutoEnding = false;
        }
    }
}
