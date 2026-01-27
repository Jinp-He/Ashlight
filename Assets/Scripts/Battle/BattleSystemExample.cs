using Ashlight.Battle.Core.Data;
using Ashlight.Battle.Core.Engine;
using cfg;
using UnityEngine;
using Sirenix.OdinInspector;
using Ashlight.Config;
namespace Ashlight.Battle
{
    /// <summary>
    /// 战斗系统使用示例
    /// 展示如何使用TimelineResolver和BattlePredictor
    /// </summary>
    public class BattleSystemExample : MonoBehaviour
    {
        [Button("测试按钮")]
        [ContextMenu("运行完整战斗示例")]
        public void RunFullBattleExample()
        {
            Debug.Log("======== 开始战斗系统测试 ========\n");

            // 1. 创建战场状态
            var battleState = CreateTestBattleState();
            Debug.Log("已创建测试战场状态");

            // 2. 加载配置表
            ConfigLoader.Load();
            var cardInfo = ConfigLoader.Tables.TbCardInfo.Get("W001"); // 打击卡牌

            if (cardInfo == null)
            {
                Debug.LogError("找不到卡牌配置: W001");
                return;
            }

            // 3. 使用BattlePredictor预测效果
            var predictor = new BattlePredictor();
            var predictionResult = predictor.Simulate(
                battleState,
                cardInfo,
                "player_1",
                "enemy_1",
                0 // 插入到时间轴第0格
            );

            Debug.Log("\n" + predictionResult.GetSummary());
            Debug.Log("\n======== 战斗系统测试完成 ========");
        }

        [ContextMenu("运行时间轴解算示例")]
        public void RunTimelineResolverExample()
        {
            Debug.Log("======== 开始时间轴解算测试 ========\n");

            // 1. 创建战场状态
            var battleState = CreateTestBattleState();

            // 2. 手动添加Blocks到时间轴
            var playerUnit = battleState.GetUnitById("player_1");
            var enemyUnit = battleState.GetUnitById("enemy_1");

            // 玩家在第0格攻击敌人（造成6点伤害）
            var damageBlock = new TimelineBlock
            {
                Phase = PhaseEnum.Active,
                OwnerId = "player_1",
                TargetId = "enemy_1",
                Priority = 80,
                Commands = new System.Collections.Generic.List<Core.Commands.ICommand>
                {
                    new Core.Commands.DamageCommand(6, false)
                }
            };
            playerUnit.Track.SetBlock(0, damageBlock);

            // 敌人在第1格攻击玩家（造成5点伤害）
            var enemyAttackBlock = new TimelineBlock
            {
                Phase = PhaseEnum.Active,
                OwnerId = "enemy_1",
                TargetId = "player_1",
                Priority = 80,
                Commands = new System.Collections.Generic.List<Core.Commands.ICommand>
                {
                    new Core.Commands.DamageCommand(5, false)
                }
            };
            enemyUnit.Track.SetBlock(1, enemyAttackBlock);

            // 3. 使用TimelineResolver解算
            var resolver = new TimelineResolver();
            resolver.ResolveFullTimeline(battleState);

            Debug.Log($"\n最终状态:");
            Debug.Log($"玩家HP: {playerUnit.CurrentHp}/{playerUnit.MaxHp}");
            Debug.Log($"敌人HP: {enemyUnit.CurrentHp}/{enemyUnit.MaxHp}");
            Debug.Log($"战斗结束: {battleState.IsBattleEnded}");

            Debug.Log("\n======== 时间轴解算测试完成 ========");
        }

        [ContextMenu("测试深拷贝功能")]
        public void TestDeepClone()
        {
            Debug.Log("======== 开始深拷贝测试 ========\n");

            // 创建原始状态
            var originalState = CreateTestBattleState();
            var originalPlayer = originalState.GetUnitById("player_1");
            originalPlayer.CurrentHp = 50;

            Debug.Log($"原始状态 - 玩家HP: {originalPlayer.CurrentHp}");

            // 深拷贝
            var clonedState = originalState.Clone();
            var clonedPlayer = clonedState.GetUnitById("player_1");

            Debug.Log($"拷贝状态 - 玩家HP: {clonedPlayer.CurrentHp}");

            // 修改拷贝状态
            clonedPlayer.CurrentHp = 10;
            Debug.Log($"修改拷贝后 - 拷贝状态玩家HP: {clonedPlayer.CurrentHp}");
            Debug.Log($"修改拷贝后 - 原始状态玩家HP: {originalPlayer.CurrentHp}");

            if (originalPlayer.CurrentHp == 50)
            {
                Debug.Log("✓ 深拷贝测试通过：修改拷贝不影响原始状态");
            }
            else
            {
                Debug.LogError("✗ 深拷贝测试失败：原始状态被修改");
            }

            Debug.Log("\n======== 深拷贝测试完成 ========");
        }

        /// <summary>
        /// 创建测试用战场状态
        /// </summary>
        private BattleStateSnapshot CreateTestBattleState()
        {
            var state = new BattleStateSnapshot();

            // 添加玩家单位
            state.PlayerUnits.Add(new UnitState
            {
                UnitId = "player_1",
                CurrentHp = 100,
                MaxHp = 100,
                Defense = 0,
                IsPlayerUnit = true,
                ConfigId = "warrior"
            });

            // 添加敌人单位
            state.EnemyUnits.Add(new UnitState
            {
                UnitId = "enemy_1",
                CurrentHp = 80,
                MaxHp = 80,
                Defense = 0,
                IsPlayerUnit = false,
                ConfigId = "goblin"
            });

            return state;
        }
    }
}

