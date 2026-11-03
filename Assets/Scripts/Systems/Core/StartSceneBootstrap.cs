using System.Collections;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace Ashlight.Systems.Core
{
    /// <summary>
    /// StartScene 的启动入口。GameManager 在同一对象的 Awake 中完成配置与存档初始化后，
    /// 本组件再切入地图场景。
    /// </summary>
    public sealed class StartSceneBootstrap : MonoBehaviour
    {
        [SerializeField] private string firstGameplayScene = "MapScene";

        private IEnumerator Start()
        {
            // Ensure every Awake has completed before changing scenes.
            yield return null;
            SceneManager.LoadScene(firstGameplayScene);
        }
    }
}
