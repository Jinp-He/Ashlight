using System.Collections;
using System;
using Ashlight.Battle.Prototype;

namespace Scripts.UI
{
    public partial class UI_BattleScene
    {
        private bool _tempoPrototypeAutoEnding;

        /// <summary>拖起非 0 费原型牌时，只预览行动卡插入位置，不修改真实 ATB。</summary>
        public void BeginTempoActionPreview(string ownerUnitId, int actionDelay)
        {
            if (!TempoPrototypeMode.IsActive || actionDelay <= 0 || string.IsNullOrEmpty(ownerUnitId))
            {
                return;
            }
            TurnOrderView?.BeginActionPreview(ownerUnitId, actionDelay);
        }

        /// <summary>卡牌成功结算后锁住预览，等待回合结束时由真实 ATB 重排接管。</summary>
        public void CommitTempoActionPreview(string ownerUnitId)
        {
            if (!TempoPrototypeMode.IsActive) return;
            TurnOrderView?.MarkActionPreviewCommitted(ownerUnitId);
        }

        /// <summary>一次拖拽结束；若没有成功出牌，则反向收起预览。</summary>
        public void EndTempoActionPreviewDrag()
        {
            if (!TempoPrototypeMode.IsActive) return;
            TurnOrderView?.CancelUncommittedActionPreview();
        }

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
