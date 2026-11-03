using System.Collections.Generic;
using UnityEngine;

namespace Scripts.UI
{
    /// <summary>
    /// UI_BattleScene 的「胜利后升级」流程分部。
    /// 序列：隐藏所有敌人 -> 显示存活角色经验条 -> 打开 UpgradePanel（拖经验卡发放经验/选卡/百相）-> 全部用完进结算。
    /// </summary>
    public partial class UI_BattleScene
    {
        private UpgradePanel _upgradePanel;

        /// <summary>胜利入口：隐藏敌人，打开 UpgradePanel 进行经验发放。</summary>
        private void StartVictoryUpgradeFlow()
        {
            // 1. 隐藏所有敌人
            if (_enemies != null)
            {
                foreach (var enemy in _enemies)
                {
                    if (enemy != null) enemy.gameObject.SetActive(false);
                }
            }

            // 1.5 隐藏战斗手牌区（手牌容器 + 抽/弃牌堆），否则会和升级面板的经验区重叠
            if (CardContainer != null) CardContainer.gameObject.SetActive(false);
            if (CardDeck != null) CardDeck.gameObject.SetActive(false);
            if (CardBin != null) CardBin.gameObject.SetActive(false);

            // 2. 收集存活的玩家角色作为可发放经验的候选
            var candidates = new List<Character>();
            if (_playerCharacters != null)
            {
                foreach (var c in _playerCharacters)
                {
                    if (c == null) continue;
                    var us = c.GetUnitState();
                    if (us != null && us.IsDead) continue;
                    candidates.Add(c);
                }
            }

            if (candidates.Count == 0)
            {
                Debug.LogWarning("[UI_BattleScene] 升级流程：没有可发放经验的角色，直接进结算");
                ShowVictorySettlementPanel();
                return;
            }

            // 3. 显示经验条（hover 可提示离升级还差多少）
            foreach (var c in candidates) c.ShowExpBar(true);

            var panel = ResolveUpgradePanel();
            if (panel == null)
            {
                Debug.LogWarning("[UI_BattleScene] 无法解析 UpgradePanel，跳过升级直接进结算");
                foreach (var c in candidates) c.ShowExpBar(false);
                ShowVictorySettlementPanel();
                return;
            }

            // 4. 打开面板；经验全部发放完毕后收起经验条并进结算
            panel.Show(candidates, () =>
            {
                foreach (var c in candidates) c.ShowExpBar(false);
                ShowVictorySettlementPanel();
            });
        }

        /// <summary>
        /// 解析场景内的 UpgradePanel。场景里可能存在重复/空壳 UpgradePanel，优先选真正带 ExpArea 的那个。
        /// </summary>
        private UpgradePanel ResolveUpgradePanel()
        {
            if (_upgradePanel != null) return _upgradePanel;

            var all = FindObjectsOfType<UpgradePanel>(true);
            foreach (var p in all)
            {
                if (p != null && p.HasExpArea) { _upgradePanel = p; break; }
            }
            if (_upgradePanel == null && all.Length > 0) _upgradePanel = all[0];
            return _upgradePanel;
        }

        /// <summary>
        /// 新战斗开始时复位升级流程相关 UI：隐藏角色经验条、恢复被胜利流程隐藏的手牌区、收起升级面板。
        /// 应对「同场景复用、未重载」导致的上一场状态残留（经验条/手牌区/面板）。
        /// </summary>
        private void ResetUpgradeUIForNewBattle()
        {
            if (_playerCharacters != null)
            {
                foreach (var c in _playerCharacters)
                    if (c != null) c.ShowExpBar(false);
            }

            if (CardContainer != null) CardContainer.gameObject.SetActive(true);
            if (CardDeck != null) CardDeck.gameObject.SetActive(true);
            if (CardBin != null) CardBin.gameObject.SetActive(true);

            // 隐藏所有 UpgradePanel 实例（场景里可能有重复/空壳，且真面板默认 active）
            var panels = FindObjectsOfType<UpgradePanel>(true);
            foreach (var p in panels)
                if (p != null) p.gameObject.SetActive(false);
        }
    }
}
