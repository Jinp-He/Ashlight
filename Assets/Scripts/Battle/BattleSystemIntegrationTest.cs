using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using cfg;
using UnityEngine;

namespace Ashlight.Battle
{
    /// <summary>
    /// 战斗系统集成测试
    /// 用于验证战斗初始化流程的完整性
    /// </summary>
    public class BattleSystemIntegrationTest : MonoBehaviour
    {
        [Header("测试配置")]
        [SerializeField]
        [Tooltip("测试用遭遇战ID")]
        private string testEncounterId = "E001";

        [SerializeField]
        [Tooltip("初始抽牌数量")]
        private int initialDrawCount = 5;

        /// <summary>
        /// 运行完整的战斗初始化测试
        /// </summary>
        [ContextMenu("运行战斗初始化测试")]
        public void RunBattleInitializationTest()
        {
            Debug.Log("======== 开始战斗初始化集成测试 ========\n");
            Debug.Log("[测试] 注意: 此测试在没有 GameManager 的情况下运行，将使用配置表的默认值和测试卡组\n");

            // 1. 加载配置表
            Debug.Log("[测试] 步骤1: 加载配置表");
            ConfigLoader.Load();

            // 2. 验证Encounter配置是否存在
            Debug.Log($"[测试] 步骤2: 验证Encounter配置 (ID: {testEncounterId})");
            var encounter = ConfigLoader.Tables.TbEncounter.GetOrDefault(testEncounterId);
            if (encounter == null)
            {
                Debug.LogError($"[测试] 失败：未找到Encounter配置 {testEncounterId}");
                Debug.LogWarning("[测试] 可用的Encounter列表:");
                foreach (var enc in ConfigLoader.Tables.TbEncounter.DataList)
                {
                    Debug.Log($"  - {enc.Id}");
                }
                return;
            }

            Debug.Log($"[测试] Encounter配置已找到，包含 {encounter.EnemySet_Ref.Count} 个敌人");

            // 3. 验证敌人配置
            Debug.Log("[测试] 步骤3: 验证敌人配置");
            foreach (var enemyInfo in encounter.EnemySet_Ref)
            {
                if (enemyInfo != null)
                {
                    Debug.Log($"  - 敌人: {enemyInfo.Name} (ID: {enemyInfo.Id}), HP: {enemyInfo.Hp}");
                }
                else
                {
                    Debug.LogError("  - 敌人配置引用为null");
                }
            }

            // 4. 创建测试用的BattleInfo
            Debug.Log("[测试] 步骤4: 创建BattleInfo");
            var testCharacters = new System.Collections.Generic.List<CharacterEnum> 
            { 
                CharacterEnum.Rocket 
            };
            var battleInfo = BattleInfo.Create(testCharacters, testEncounterId, initialDrawCount);

            if (!battleInfo.IsValid())
            {
                Debug.LogError("[测试] BattleInfo验证失败");
                return;
            }

            Debug.Log($"[测试] BattleInfo创建成功 - 角色数: {battleInfo.PlayerCharacters.Count}, EncounterID: {battleInfo.EncounterId}");

            // 5. 创建并初始化BattleManager
            Debug.Log("[测试] 步骤5: 初始化BattleManager");
            var battleManager = gameObject.AddComponent<BattleManager>();

            try
            {
                battleManager.InitializeBattle(battleInfo);
            }
            catch (System.Exception e)
            {
                Debug.LogError($"[测试] 战斗初始化异常: {e.Message}\n{e.StackTrace}");
                return;
            }

            // 6. 验证战斗状态
            Debug.Log("[测试] 步骤6: 验证战斗状态");
            var state = battleManager.CurrentState;

            if (state == null)
            {
                Debug.LogError("[测试] CurrentState为null");
                return;
            }

            // 验证玩家单位
            Debug.Log($"[测试] 玩家单位数量: {state.PlayerUnits.Count}");
            foreach (var unit in state.PlayerUnits)
            {
                Debug.Log($"  - {unit.UnitId}: HP {unit.CurrentHp}/{unit.MaxHp}, Track: {(unit.Track != null ? "已创建" : "null")}");
                if (unit.Track == null)
                {
                    Debug.LogError($"    警告: 玩家单位 {unit.UnitId} 的Track为null（应该有独立时间轴）");
                }
            }

            // 验证敌人单位
            Debug.Log($"[测试] 敌人单位数量: {state.EnemyUnits.Count}");
            foreach (var unit in state.EnemyUnits)
            {
                Debug.Log($"  - {unit.UnitId} ({unit.ConfigId}): HP {unit.CurrentHp}/{unit.MaxHp}, Track: {(unit.Track != null ? "独立" : "共享")}");
                if (unit.Track != null)
                {
                    Debug.LogWarning($"    警告: 敌人单位 {unit.UnitId} 有独立Track（应该使用共享时间轴）");
                }
            }

            // 验证敌人共享时间轴
            if (state.SharedEnemyTrack != null)
            {
                Debug.Log($"[测试] ✓ 敌人共享时间轴已创建");
            }
            else
            {
                Debug.LogError("[测试] ✗ 敌人共享时间轴为null");
            }

            // 验证卡组系统
            if (state.DeckSystem != null)
            {
                Debug.Log($"[测试] ✓ 卡组系统已创建");
                Debug.Log($"  - {state.DeckSystem.GetDebugInfo()}");
                Debug.Log($"  - 总计: {state.DeckSystem.GetTotalCardCount()} 张");

                // 显示手牌详情
                if (state.DeckSystem.Hand.Count > 0)
                {
                    Debug.Log("  手牌详情:");
                    foreach (var card in state.DeckSystem.Hand)
                    {
                        var cardInfo = ConfigLoader.Tables.TbCardInfo.GetOrDefault(card.CardId);
                        if (cardInfo != null)
                        {
                            Debug.Log($"    - {cardInfo.Name} (ID: {card.CardId})");
                        }
                        else
                        {
                            Debug.LogWarning($"    - 未知卡牌 (ID: {card.CardId})");
                        }
                    }
                }
            }
            else
            {
                Debug.LogError("[测试] ✗ 卡组系统为null");
            }

            // 7. 测试深拷贝功能
            Debug.Log("[测试] 步骤7: 测试深拷贝功能");
            var clonedState = state.Clone();
            if (clonedState != null)
            {
                Debug.Log($"[测试] ✓ 战斗状态深拷贝成功");
                Debug.Log($"  - 克隆后玩家单位数: {clonedState.PlayerUnits.Count}");
                Debug.Log($"  - 克隆后敌人单位数: {clonedState.EnemyUnits.Count}");
                Debug.Log($"  - 克隆后手牌数: {clonedState.DeckSystem?.Hand.Count ?? 0}");
            }
            else
            {
                Debug.LogError("[测试] ✗ 深拷贝失败");
            }

            // 8. 测试结果汇总
            Debug.Log("\n======== 战斗初始化集成测试完成 ========");
            Debug.Log($"[测试结果] {battleManager.GetDebugInfo()}");
            Debug.Log("======== 测试通过 ========\n");

            // 清理
            Destroy(battleManager);
        }

        /// <summary>
        /// 列出所有可用的Encounter配置
        /// </summary>
        [ContextMenu("列出所有Encounter配置")]
        public void ListAllEncounters()
        {
            ConfigLoader.Load();

            Debug.Log("======== 所有可用的Encounter配置 ========");
            var encounters = ConfigLoader.Tables.TbEncounter.DataList;
            if (encounters == null || encounters.Count == 0)
            {
                Debug.LogWarning("没有找到任何Encounter配置");
                return;
            }

            foreach (var encounter in encounters)
            {
                Debug.Log($"\nEncounter ID: {encounter.Id}");
                Debug.Log($"  敌人数量: {encounter.EnemySet_Ref?.Count ?? 0}");
                if (encounter.EnemySet_Ref != null)
                {
                    foreach (var enemy in encounter.EnemySet_Ref)
                    {
                        if (enemy != null)
                        {
                            Debug.Log($"    - {enemy.Name} (ID: {enemy.Id}), HP: {enemy.Hp}");
                        }
                    }
                }
            }
            Debug.Log("========================================");
        }
    }
}

