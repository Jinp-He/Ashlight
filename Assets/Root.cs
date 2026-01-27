using UnityEngine;
using UnityEngine.SceneManagement;
using Ashlight.Config;
using Ashlight.Systems.Core;
using Ashlight.Systems.Character;

/// <summary>
/// 游戏根节点 - 游戏启动入口
/// 负责初始化所有核心系统和加载主场景
/// </summary>
public class Root : MonoBehaviour
{
    [Header("场景配置")]
    [SerializeField]
    [Tooltip("游戏主场景名称")]
    private string mainSceneName = "MainScene";

    [Header("初始化设置")]
    [SerializeField]
    [Tooltip("是否自动开始游戏")]
    private bool autoStartGame = true;

    [SerializeField]
    [Tooltip("是否在编辑器模式下显示详细日志")]
    private bool verboseLogging = true;

    /// <summary>
    /// 游戏是否已初始化
    /// </summary>
    public static bool IsInitialized { get; private set; } = false;

    private void Awake()
    {
        // 设置日志级别
        if (verboseLogging)
        {
            Debug.Log("[Root] ========== 游戏启动 ==========");
        }

        // 确保Root对象不会被销毁
        DontDestroyOnLoad(gameObject);
    }

    private void Start()
    {
        if (autoStartGame)
        {
            StartGame();
        }
    }

    /// <summary>
    /// 开始游戏 - 初始化所有系统
    /// </summary>
    public void StartGame()
    {
        if (IsInitialized)
        {
            Debug.LogWarning("[Root] 游戏已经初始化，跳过重复初始化");
            return;
        }

        Debug.Log("[Root] ========== 开始初始化游戏系统 ==========");

        try
        {
            // 第一步：初始化GameManager（单例）
            InitializeGameManager();

            // 第二步：加载配置数据
            InitializeConfigSystem();

            // 第三步：初始化存档系统
            InitializeSaveSystem();

            // 第四步：初始化角色系统
            InitializeCharacterSystem();

            // 第五步：初始化其他游戏系统
            InitializeOtherSystems();

            // 第六步：应用游戏设置
            ApplyGameSettings();

            IsInitialized = true;
            Debug.Log("[Root] ========== 游戏初始化完成 ==========");

            // 第七步：加载主场景（可选）
            //if (!string.IsNullOrEmpty(mainSceneName))
            //{
            //    LoadMainScene();
            //}
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Root] 游戏初始化失败: {e.Message}\n{e.StackTrace}");
            IsInitialized = false;
        }
    }

    /// <summary>
    /// 初始化GameManager
    /// </summary>
    private void InitializeGameManager()
    {
        Debug.Log("[Root] 步骤1: 初始化GameManager");

        // 检查场景中是否已有GameManager
        var existingGameManager = FindObjectOfType<GameManager>();
        if (existingGameManager == null)
        {
            // 创建GameManager GameObject
            GameObject gameManagerObj = new GameObject("GameManager");
            gameManagerObj.AddComponent<GameManager>();
            DontDestroyOnLoad(gameManagerObj);
            Debug.Log("[Root] GameManager 已创建");
        }
        else
        {
            Debug.Log("[Root] GameManager 已存在");
        }
    }

    /// <summary>
    /// 初始化配置系统
    /// </summary>
    private void InitializeConfigSystem()
    {
        Debug.Log("[Root] 步骤2: 加载配置数据");

        // ConfigLoader会在GameManager.Initialize中被调用
        // 这里只是确认配置已加载
        if (ConfigLoader.Tables != null)
        {
            Debug.Log("[Root] 配置数据加载成功");
            
            if (verboseLogging)
            {
                // 输出配置统计信息
                Debug.Log($"[Root] - 角色配置数量: {ConfigLoader.Tables.TbCharaterInfo.DataList.Count}");
                Debug.Log($"[Root] - 卡牌配置数量: {ConfigLoader.Tables.TbCardInfo.DataList.Count}");
            }
        }
        else
        {
            Debug.LogWarning("[Root] 配置数据未加载");
        }
    }

    /// <summary>
    /// 初始化存档系统
    /// </summary>
    private void InitializeSaveSystem()
    {
        Debug.Log("[Root] 步骤3: 初始化存档系统");

        // 检查是否有存档
        bool hasSave = SaveManager.HasSave();
        Debug.Log($"[Root] 存档状态: {(hasSave ? "存在" : "不存在")}");

        // GameManager已经处理了存档的加载
        if (GameManager.Instance != null && GameManager.Instance.CurrentSave != null)
        {
            var saveData = GameManager.Instance.CurrentSave;
            Debug.Log($"[Root] 存档版本: {saveData.Version}");
            
            if (verboseLogging)
            {
                Debug.Log($"[Root] - 玩家等级: {saveData.PlayerState.Level}");
                Debug.Log($"[Root] - 玩家金币: {saveData.PlayerState.Gold}");
                Debug.Log($"[Root] - 角色数量: {saveData.Characters?.Count ?? 0}");
            }
        }
    }

    /// <summary>
    /// 初始化角色系统
    /// </summary>
    private void InitializeCharacterSystem()
    {
        Debug.Log("[Root] 步骤4: 初始化角色系统");

        // GameManager已经处理了角色的初始化
        var unlockedCharacters = CharacterSystem.GetUnlockedCharacters();
        Debug.Log($"[Root] 已解锁角色数量: {unlockedCharacters.Count}");

        if (verboseLogging && unlockedCharacters.Count > 0)
        {
            foreach (var character in unlockedCharacters)
            {
                Debug.Log($"[Root] - 角色 {character.CharacterId}: HP={character.CurrentHp}, Level={character.Level}, Deck={character.CurrentDeck?.Count ?? 0}张卡");
            }
        }
    }

    /// <summary>
    /// 初始化其他游戏系统
    /// </summary>
    private void InitializeOtherSystems()
    {
        Debug.Log("[Root] 步骤5: 初始化其他游戏系统");

        // TODO: 在这里添加其他系统的初始化
        // 例如：
        // - 音频系统
        // - 输入系统
        // - UI系统
        // - 战斗系统
        // - 任务系统
        // - 成就系统

        Debug.Log("[Root] 其他系统初始化完成（待扩展）");
    }

    /// <summary>
    /// 应用游戏设置
    /// </summary>
    private void ApplyGameSettings()
    {
        Debug.Log("[Root] 步骤6: 应用游戏设置");

        // 设置目标帧率
        Application.targetFrameRate = 60;

        // 设置屏幕常亮（移动平台）
        Screen.sleepTimeout = SleepTimeout.NeverSleep;

        // TODO: 加载和应用用户设置
        // - 音量设置
        // - 画质设置
        // - 语言设置

        Debug.Log("[Root] 游戏设置已应用");
    }

    /// <summary>
    /// 加载主场景
    /// </summary>
    private void LoadMainScene()
    {
        Debug.Log($"[Root] 步骤7: 加载主场景 [{mainSceneName}]");

        try
        {
            // 异步加载场景
            SceneManager.LoadSceneAsync(mainSceneName, LoadSceneMode.Single);
        }
        catch (System.Exception e)
        {
            Debug.LogError($"[Root] 加载主场景失败: {e.Message}");
        }
    }

    /// <summary>
    /// 重启游戏
    /// </summary>
    public void RestartGame()
    {
        Debug.Log("[Root] 重启游戏");
        
        IsInitialized = false;
        
        // 重新加载当前场景
        SceneManager.LoadScene(SceneManager.GetActiveScene().name);
    }

    /// <summary>
    /// 退出游戏
    /// </summary>
    public void QuitGame()
    {
        Debug.Log("[Root] 退出游戏");

        // 保存游戏
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SaveGame();
        }

#if UNITY_EDITOR
        UnityEditor.EditorApplication.isPlaying = false;
#else
        Application.Quit();
#endif
    }

    private void OnApplicationQuit()
    {
        Debug.Log("[Root] 应用程序退出，保存游戏数据");

        // 自动保存
        if (GameManager.Instance != null)
        {
            GameManager.Instance.SaveGame();
        }
    }

    private void OnApplicationPause(bool pauseStatus)
    {
        if (pauseStatus)
        {
            Debug.Log("[Root] 应用程序暂停，保存游戏数据");

            // 暂停时自动保存（移动平台）
            if (GameManager.Instance != null)
            {
                GameManager.Instance.SaveGame();
            }
        }
    }
}
