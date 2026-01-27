using System.Collections.Generic;
using UnityEngine;
using UnityEngine.EventSystems;
using cfg;
using Ashlight.Battle.Core.Data;

namespace Scripts.UI
{
    /// <summary>
    /// 目标选择管理器
    /// 负责检测和验证卡牌目标
    /// </summary>
    public class TargetSelectionManager : MonoBehaviour
    {
        private List<Character> _allPlayerCharacters = new List<Character>();
        private List<Enemy> _allEnemies = new List<Enemy>();

        /// <summary>
        /// 初始化管理器
        /// </summary>
        /// <param name="playerCharacters">所有玩家角色</param>
        /// <param name="enemies">所有敌人</param>
        public void Initialize(List<Character> playerCharacters, List<Enemy> enemies)
        {
            _allPlayerCharacters = playerCharacters ?? new List<Character>();
            _allEnemies = enemies ?? new List<Enemy>();

            Debug.Log($"[TargetSelectionManager] 初始化完成: {_allPlayerCharacters.Count}个玩家角色, {_allEnemies.Count}个敌人");
        }

        /// <summary>
        /// 检测鼠标下的目标
        /// </summary>
        /// <param name="eventData">指针事件数据</param>
        /// <returns>检测到的Character或Enemy GameObject,如果没有则返回null</returns>
        public GameObject DetectTargetUnderMouse(PointerEventData eventData)
        {
            if (eventData == null)
            {
                return null;
            }

            // 使用EventSystem进行射线检测
            List<RaycastResult> results = new List<RaycastResult>();
            EventSystem.current.RaycastAll(eventData, results);

            foreach (var result in results)
            {
                // 检查是否为Character
                var character = result.gameObject.GetComponent<Character>();
                if (character != null)
                {
                    return result.gameObject;
                }

                // 检查是否为Enemy
                var enemy = result.gameObject.GetComponent<Enemy>();
                if (enemy != null)
                {
                    return result.gameObject;
                }

                // 检查父级是否为Character或Enemy
                character = result.gameObject.GetComponentInParent<Character>();
                if (character != null)
                {
                    return character.gameObject;
                }

                enemy = result.gameObject.GetComponentInParent<Enemy>();
                if (enemy != null)
                {
                    return enemy.gameObject;
                }
            }

            return null;
        }

        /// <summary>
        /// 验证目标是否合法
        /// </summary>
        /// <param name="target">目标GameObject</param>
        /// <param name="targetType">卡牌目标类型</param>
        /// <param name="ownerId">卡牌所有者ID</param>
        /// <returns>是否为合法目标</returns>
        public bool IsValidTarget(GameObject target, TargetTypeEnum targetType, CharacterEnum ownerCharacterId)
        {
            if (target == null)
            {
                return false;
            }

            switch (targetType)
            {
                case TargetTypeEnum.SingleAlly:
                    return ValidateSingleAlly(target, ownerCharacterId);

                case TargetTypeEnum.AllAlly:
                    return ValidateAllAlly(target, ownerCharacterId);

                case TargetTypeEnum.Self:
                    return ValidateSelf(target, ownerCharacterId);

                case TargetTypeEnum.SingleEnemy:
                    return ValidateSingleEnemy(target);

                case TargetTypeEnum.AllEnemy:
                    return ValidateAllEnemy(target);

                case TargetTypeEnum.TimeSlot:
                    // TimeSlot类型不使用目标选择系统
                    return false;

                default:
                    return false;
            }
        }

        /// <summary>
        /// 获取所有合法的队友
        /// </summary>
        /// <param name="ownerId">卡牌所有者ID</param>
        /// <returns>合法的队友列表</returns>
        public List<Character> GetAllValidAllies(CharacterEnum ownerCharacterId)
        {
            List<Character> validAllies = new List<Character>();

            foreach (var character in _allPlayerCharacters)
            {
                if (character == null)
                {
                    continue;
                }

                UnitState state = character.GetUnitState();
                if (state != null && state.IsPlayerUnit && !state.IsDead)
                {
                    validAllies.Add(character);
                }
            }

            return validAllies;
        }

        /// <summary>
        /// 获取所有合法的敌人
        /// </summary>
        /// <returns>合法的敌人列表</returns>
        public List<Enemy> GetAllValidEnemies()
        {
            List<Enemy> validEnemies = new List<Enemy>();

            foreach (var enemy in _allEnemies)
            {
                if (enemy == null)
                {
                    continue;
                }

                UnitState state = enemy.GetUnitState();
                if (state != null && !state.IsDead)
                {
                    validEnemies.Add(enemy);
                }
            }

            return validEnemies;
        }

        #region 私有验证方法

        /// <summary>
        /// 验证单个队友目标
        /// </summary>
        private bool ValidateSingleAlly(GameObject target, CharacterEnum ownerCharacterId)
        {
            var character = target.GetComponent<Character>();
            if (character == null)
            {
                return false;
            }

            UnitState state = character.GetUnitState();
            if (state == null)
            {
                return false;
            }

            // 必须是玩家角色且存活
            return state.IsPlayerUnit && !state.IsDead;
        }

        /// <summary>
        /// 验证全体队友目标
        /// </summary>
        private bool ValidateAllAlly(GameObject target, CharacterEnum ownerCharacterId)
        {
            // 与SingleAlly相同,只要是合法队友即可
            return ValidateSingleAlly(target, ownerCharacterId);
        }

        /// <summary>
        /// 验证自己目标
        /// </summary>
        private bool ValidateSelf(GameObject target, CharacterEnum ownerCharacterId)
        {
            var character = target.GetComponent<Character>();
            if (character == null)
            {
                return false;
            }

            UnitState state = character.GetUnitState();
            if (state == null)
            {
                return false;
            }

            var characterId = state.GetCharacterId();
            if (!characterId.HasValue)
            {
                return false;
            }

            // 必须是卡牌所有者且存活
            return characterId.Value == ownerCharacterId && !state.IsDead;
        }

        /// <summary>
        /// 验证单个敌人目标
        /// </summary>
        private bool ValidateSingleEnemy(GameObject target)
        {
            var enemy = target.GetComponent<Enemy>();
            if (enemy == null)
            {
                return false;
            }

            UnitState state = enemy.GetUnitState();
            if (state == null)
            {
                return false;
            }

            // 必须存活
            return !state.IsDead;
        }

        /// <summary>
        /// 验证全体敌人目标
        /// </summary>
        private bool ValidateAllEnemy(GameObject target)
        {
            // 与SingleEnemy相同,只要是合法敌人即可
            return ValidateSingleEnemy(target);
        }

        #endregion
    }
}
