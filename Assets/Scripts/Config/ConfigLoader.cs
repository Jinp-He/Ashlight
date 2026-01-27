using UnityEngine;
using Luban;
using Luban.SimpleJSON;
using cfg;
using System.IO;


namespace Ashlight.Config
{
    /// <summary>
    /// 配置加载器 - 负责加载 Luban 生成的配置数据
    /// 数据层：只读访问，禁止存储运行时状态
    /// </summary>
    public static class ConfigLoader
    {
        private static Tables _tables;
        private static bool _isLoaded = false;

        /// <summary>
        /// 获取配置表（只读）
        /// </summary>
        public static Tables Tables
        {
            get
            {
                if (!_isLoaded)
                {
                    Debug.LogError("[ConfigLoader] 配置尚未加载，请先调用 Load()");
                    Load();
                }
                return _tables;
            }
        }

        /// <summary>
        /// 加载所有配置表
        /// </summary>
        public static void Load()
        {
            if (_isLoaded)
            {
                Debug.LogWarning("[ConfigLoader] 配置已加载，跳过重复加载");
                return;
            }

            try
            {
                // 加载配置表，Lambda函数用于根据文件名加载对应的JSON文件
                _tables = new cfg.Tables(LoadJsonFile);
                _isLoaded = true;
                
                Debug.Log("[ConfigLoader] 配置加载完成");
            }
            catch (System.Exception e)
            {
                
                Debug.LogError($"[ConfigLoader] 配置加载失败: {e.Message}\n{e.StackTrace}");
                _isLoaded = false;
            }
        }

        /// <summary>
        /// 加载JSON配置文件
        /// </summary>
        /// <param name="fileName">文件名（不含扩展名）</param>
        /// <returns>解析后的JSON节点</returns>
        private static JSONNode LoadJsonFile(string fileName)
        {
            // 从Resources/Config目录加载配置文件
            string resourcePath = $"Config/{fileName}";
            
            TextAsset textAsset = Resources.Load<TextAsset>(resourcePath);
            
            if (textAsset == null)
            {
                Debug.LogError($"[ConfigLoader] 配置文件不存在: Resources/{resourcePath}.json");
                throw new System.IO.FileNotFoundException($"配置文件不存在: Resources/{resourcePath}.json");
            }

            Debug.Log($"[ConfigLoader] 正在加载配置文件: {fileName}.json");

            // 解析JSON文件
            return JSON.Parse(textAsset.text);
        }

        /// <summary>
        /// 重新加载配置（用于热更新）
        /// </summary>
        public static void Reload()
        {
            _isLoaded = false;
            _tables = null;
            Load();
        }
    }
}

