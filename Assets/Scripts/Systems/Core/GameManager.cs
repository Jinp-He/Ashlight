using UnityEngine;
using Ashlight.Config;
using Ashlight.State.Save;
using Ashlight.Systems.Character;

namespace Ashlight.Systems.Core
{
    /// <summary>
    /// 游戏管理器 - 游戏主循环入口
    /// 系统层：驱动规则，协调各系统
    /// </summary>
    public class GameManager : MonoBehaviour
    {
        public static GameManager Instance { get; private set; }

        /// <summary>
        /// 当前存档数据
        /// </summary>
        public SaveData CurrentSave { get; private set; }

        private void Awake()
        {
            if (Instance != null && Instance != this)
            {
                Destroy(gameObject);
                return;
            }

            Instance = this;
            DontDestroyOnLoad(gameObject);

            Initialize();
        }

        /// <summary>
        /// 初始化游戏
        /// </summary>
        private void Initialize()
        {
            // 1. 加载配置
            ConfigLoader.Load();

            // 2. 加载或创建存档
            bool isNewSave = !SaveManager.HasSave();
            CurrentSave = SaveManager.LoadOrCreateNew();

            // 3. 如果是新存档，初始化角色数据
            if (isNewSave || CurrentSave.Characters == null || CurrentSave.Characters.Count == 0)
            {
                CharacterSystem.InitializeCharacters(unlockFirst: true);
                Debug.Log("[GameManager] 新存档：已初始化角色数据");
            }

            // 4. 初始化各系统
            // TODO: 初始化其他系统

            Debug.Log("[GameManager] 游戏初始化完成");
        }

        private void Update()
        {
            // 游戏主循环
            // TODO: 驱动各系统的 Tick
        }

        /// <summary>
        /// 保存游戏
        /// </summary>
        public void SaveGame()
        {
            SaveManager.Save(CurrentSave);
        }
    }
}

