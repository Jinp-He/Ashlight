using UnityEngine;
using Ashlight.Config;

namespace Ashlight.Battle
{
    /// <summary>
    /// 配置表检查工具
    /// 用于验证配置表数据是否正确加载
    /// </summary>
    public class ConfigTableChecker : MonoBehaviour
    {
        [ContextMenu("检查配置表数据")]
        public void CheckConfigTables()
        {
            Debug.Log("======== 开始检查配置表 ========");

            // 加载配置表
            ConfigLoader.Load();

            // 1. 检查角色配置
            Debug.Log("\n--- 角色配置 (TbCharaterInfo) ---");
            var characters = ConfigLoader.Tables.TbCharaterInfo.DataList;
            Debug.Log($"角色数量: {characters.Count}");
            foreach (var character in characters)
            {
                Debug.Log($"  - {character.Name} (ID: {character.Character})");
                Debug.Log($"    BaseHp: {character.BaseHp}");
                //Debug.Log($"    SkeletonAssetPath: {character.SkeletonAssetPath}");
            }

            // 2. 检查敌人配置
            Debug.Log("\n--- 敌人配置 (TbEnemyInfo) ---");
            var enemies = ConfigLoader.Tables.TbEnemyInfo.DataList;
            Debug.Log($"敌人数量: {enemies.Count}");
            foreach (var enemy in enemies)
            {
                Debug.Log($"  - {enemy.Name} (ID: {enemy.Id})");
                Debug.Log($"    Hp: {enemy.Hp}");
                //Debug.Log($"    AssetPath: {enemy.AssetPath}");
            }

            // 3. 检查遭遇战配置
            Debug.Log("\n--- 遭遇战配置 (TbEncounter) ---");
            var encounters = ConfigLoader.Tables.TbEncounter.DataList;
            Debug.Log($"遭遇战数量: {encounters.Count}");
            foreach (var encounter in encounters)
            {
                Debug.Log($"  - Encounter ID: {encounter.Id}");
                Debug.Log($"    敌人数量: {encounter.EnemySet_Ref?.Count ?? 0}");
                if (encounter.EnemySet_Ref != null)
                {
                    foreach (var enemy in encounter.EnemySet_Ref)
                    {
                        if (enemy != null)
                        {
                            Debug.Log($"      → {enemy.Name}");
                        }
                    }
                }
            }

            // 4. 检查卡牌配置
            Debug.Log("\n--- 卡牌配置 (TbCardInfo) ---");
            var cards = ConfigLoader.Tables.TbCardInfo.DataList;
            Debug.Log($"卡牌数量: {cards.Count}");

            Debug.Log("\n======== 配置表检查完成 ========");
        }
    }
}

