using System.Collections.Generic;
using System.Linq;
using UnityEditor;
using UnityEngine;

namespace Ashlight.EditorTools
{
    /// <summary>从正式战斗场景复制出隔离的节奏实验场景；运行时规则按场景名自动启用。</summary>
    public static class TempoPrototypeSceneGenerator
    {
        private const string SourceScene = "Assets/Scenes/BattleScene.unity";
        private const string PrototypeScene = "Assets/Scenes/BattleScene_TempoPrototype.unity";

        [MenuItem("Ashlight/Prototype/Create One-Card Tempo Battle Scene")]
        public static void CreateScene()
        {
            if (AssetDatabase.LoadAssetAtPath<SceneAsset>(PrototypeScene) == null)
            {
                if (!AssetDatabase.CopyAsset(SourceScene, PrototypeScene))
                {
                    throw new System.InvalidOperationException(
                        $"Failed to copy prototype scene from {SourceScene} to {PrototypeScene}.");
                }
            }

            var scenes = EditorBuildSettings.scenes.ToList();
            if (scenes.All(scene => scene.path != PrototypeScene))
            {
                scenes.Add(new EditorBuildSettingsScene(PrototypeScene, true));
                EditorBuildSettings.scenes = scenes.ToArray();
            }

            AssetDatabase.SaveAssets();
            AssetDatabase.Refresh();
            Debug.Log($"[TempoPrototype] Scene ready: {PrototypeScene}");
        }
    }
}
