using System.Collections.Generic;
using UnityEngine;
using Ashlight.Battle;
using Ashlight.Battle.Core.Data;

namespace Scripts.UI
{
    /// <summary>
    /// 战斗单位UI管理器
    /// 负责管理战斗中玩家角色和敌人的UI组件，提供统一的查找接口
    /// </summary>
    public class BattleUnitUIManager
    {
        #region 私有字段

        /// <summary>
        /// 玩家角色UI列表
        /// </summary>
        private List<Character> _playerCharacters = new List<Character>();

        /// <summary>
        /// 敌人UI列表
        /// </summary>
        private List<Enemy> _enemies = new List<Enemy>();

        /// <summary>
        /// 战斗管理器引用（用于查找UnitState）
        /// </summary>
        private BattleManager _battleManager;

        #endregion

        #region 公共属性

        /// <summary>
        /// 获取所有玩家角色（只读）
        /// </summary>
        public IReadOnlyList<Character> PlayerCharacters => _playerCharacters;

        /// <summary>
        /// 获取所有敌人（只读）
        /// </summary>
        public IReadOnlyList<Enemy> Enemies => _enemies;

        /// <summary>
        /// 玩家角色数量
        /// </summary>
        public int PlayerCount => _playerCharacters.Count;

        /// <summary>
        /// 敌人数量
        /// </summary>
        public int EnemyCount => _enemies.Count;

        #endregion

        #region 初始化

        /// <summary>
        /// 设置战斗管理器引用
        /// </summary>
        /// <param name="battleManager">战斗管理器</param>
        public void SetBattleManager(BattleManager battleManager)
        {
            _battleManager = battleManager;
        }

        #endregion

        #region 注册/注销

        /// <summary>
        /// 注册玩家角色
        /// </summary>
        /// <param name="character">角色UI组件</param>
        public void RegisterCharacter(Character character)
        {
            if (character != null && !_playerCharacters.Contains(character))
            {
                _playerCharacters.Add(character);
            }
        }

        /// <summary>
        /// 注册敌人
        /// </summary>
        /// <param name="enemy">敌人UI组件</param>
        public void RegisterEnemy(Enemy enemy)
        {
            if (enemy != null && !_enemies.Contains(enemy))
            {
                _enemies.Add(enemy);
            }
        }

        /// <summary>
        /// 注销玩家角色
        /// </summary>
        /// <param name="character">角色UI组件</param>
        public void UnregisterCharacter(Character character)
        {
            _playerCharacters.Remove(character);
        }

        /// <summary>
        /// 注销敌人
        /// </summary>
        /// <param name="enemy">敌人UI组件</param>
        public void UnregisterEnemy(Enemy enemy)
        {
            _enemies.Remove(enemy);
        }

        #endregion

        #region 清理

        /// <summary>
        /// 清空所有单位（销毁游戏对象）
        /// </summary>
        public void ClearAll()
        {
            // 清空玩家角色
            foreach (var character in _playerCharacters)
            {
                if (character != null && character.gameObject != null)
                {
                    Object.Destroy(character.gameObject);
                }
            }
            _playerCharacters.Clear();

            // 清空敌人
            foreach (var enemy in _enemies)
            {
                if (enemy != null && enemy.gameObject != null)
                {
                    Object.Destroy(enemy.gameObject);
                }
            }
            _enemies.Clear();

            Debug.Log("[BattleUnitUIManager] 已清空所有单位UI");
        }

        /// <summary>
        /// 清空列表引用（不销毁游戏对象）
        /// </summary>
        public void ClearReferences()
        {
            _playerCharacters.Clear();
            _enemies.Clear();
        }

        #endregion

        #region 查找方法

        /// <summary>
        /// 根据UnitId查找角色UI
        /// </summary>
        /// <param name="unitId">单位ID</param>
        /// <returns>角色UI组件，未找到返回null</returns>
        public Character FindCharacter(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return null;

            foreach (var character in _playerCharacters)
            {
                if (character != null && character.GetUnitState()?.UnitId == unitId)
                {
                    return character;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据UnitId查找敌人UI
        /// </summary>
        /// <param name="unitId">单位ID</param>
        /// <returns>敌人UI组件，未找到返回null</returns>
        public Enemy FindEnemy(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return null;

            foreach (var enemy in _enemies)
            {
                if (enemy != null && enemy.GetUnitState()?.UnitId == unitId)
                {
                    return enemy;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据UnitId查找对应的UI GameObject
        /// </summary>
        /// <param name="unitId">单位ID</param>
        /// <returns>UI对象，未找到返回null</returns>
        public GameObject FindUnitObject(string unitId)
        {
            if (string.IsNullOrEmpty(unitId))
                return null;

            // 先在玩家角色中查找
            var character = FindCharacter(unitId);
            if (character != null)
                return character.gameObject;

            // 再在敌人中查找
            var enemy = FindEnemy(unitId);
            if (enemy != null)
                return enemy.gameObject;

            return null;
        }

        /// <summary>
        /// 根据UnitId查找对应的UI组件（Character或Enemy）
        /// </summary>
        /// <param name="unitId">单位ID</param>
        /// <returns>UI组件，未找到返回null</returns>
        public MonoBehaviour FindUnitComponent(string unitId)
        {
            // 在玩家角色中查找
            foreach (var character in _playerCharacters)
            {
                if (character == null) continue;
                var unitState = character.GetUnitState();
                if (unitState != null && unitState.UnitId == unitId)
                {
                    return character;
                }
            }

            // 在敌人中查找
            foreach (var enemy in _enemies)
            {
                if (enemy == null) continue;
                var unitState = enemy.GetUnitState();
                if (unitState != null && unitState.UnitId == unitId)
                {
                    return enemy;
                }
            }

            return null;
        }

        /// <summary>
        /// 根据UnitId查找UnitState
        /// </summary>
        /// <param name="unitId">单位ID</param>
        /// <returns>单位状态，未找到返回null</returns>
        public UnitState FindUnitState(string unitId)
        {
            if (_battleManager?.CurrentState == null) return null;

            // 在玩家单位中查找
            foreach (var unit in _battleManager.CurrentState.PlayerUnits)
            {
                if (unit.UnitId == unitId) return unit;
            }

            // 在敌人单位中查找
            foreach (var unit in _battleManager.CurrentState.EnemyUnits)
            {
                if (unit.UnitId == unitId) return unit;
            }

            return null;
        }

        #endregion

        #region 遍历操作

        /// <summary>
        /// 对所有玩家角色执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        public void ForEachCharacter(System.Action<Character> action)
        {
            if (action == null) return;
            foreach (var character in _playerCharacters)
            {
                if (character != null)
                {
                    action(character);
                }
            }
        }

        /// <summary>
        /// 对所有敌人执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        public void ForEachEnemy(System.Action<Enemy> action)
        {
            if (action == null) return;
            foreach (var enemy in _enemies)
            {
                if (enemy != null)
                {
                    action(enemy);
                }
            }
        }

        /// <summary>
        /// 对所有单位执行操作
        /// </summary>
        /// <param name="action">要执行的操作</param>
        public void ForEachUnit(System.Action<MonoBehaviour> action)
        {
            if (action == null) return;
            
            foreach (var character in _playerCharacters)
            {
                if (character != null)
                {
                    action(character);
                }
            }
            
            foreach (var enemy in _enemies)
            {
                if (enemy != null)
                {
                    action(enemy);
                }
            }
        }

        #endregion

        #region 列表访问

        /// <summary>
        /// 获取所有玩家角色的副本列表
        /// </summary>
        /// <returns>玩家角色列表副本</returns>
        public List<Character> GetAllCharacters()
        {
            return new List<Character>(_playerCharacters);
        }

        /// <summary>
        /// 获取所有敌人的副本列表
        /// </summary>
        /// <returns>敌人列表副本</returns>
        public List<Enemy> GetAllEnemies()
        {
            return new List<Enemy>(_enemies);
        }

        #endregion
    }
}
