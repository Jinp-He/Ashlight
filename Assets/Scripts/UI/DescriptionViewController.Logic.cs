using System.Text.RegularExpressions;
using UnityEngine;
using cfg;
using cfg.Enemy;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;

namespace Scripts.UI
{
    /// <summary>
    /// DescriptionViewController的业务逻辑部分（手动编写）
    /// </summary>
    public partial class DescriptionViewController
    {
        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();
            
            // 默认隐藏
            gameObject.SetActive(false);
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 显示名词描述
        /// </summary>
        /// <param name="nounName">名词名称</param>
        /// <param name="buffValue">可选：来自卡牌效果的 buff 数值，用于替换描述中的 {V}</param>
        public void Show(string nounName, float? buffValue = null)
        {
            if (string.IsNullOrEmpty(nounName))
            {
                Hide();
                return;
            }

            // 从配置中获取名词描述
            var nounDict = GetNounDescription(nounName);
            if (nounDict == null)
            {
                Debug.LogWarning($"[DescriptionViewController] 未找到名词: {nounName}");
                Hide();
                return;
            }

            // 设置UI显示
            if (Txt_EntryName != null)
            {
                Txt_EntryName.text = nounDict.Name;
            }

            if (Txt_Entry != null)
            {
                // 查找对应 BuffInfo 以替换 {T}；用传入的 buffValue 替换 {V}
                var buffInfo = GetBuffInfoByName(nounName);
                string desc = ReplaceNounPlaceholders(nounDict.Desc, buffInfo, buffValue);
                Txt_Entry.text = desc;
            }

            // 显示面板
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 显示敌人技能描述
        /// </summary>
        /// <param name="enemySkillInfo">敌人技能配置信息</param>
        public void Show(EnemySkillInfo enemySkillInfo)
        {
            if (enemySkillInfo == null)
            {
                Debug.LogWarning("[DescriptionViewController] EnemySkillInfo为空");
                Hide();
                return;
            }

            // 设置技能名称
            if (Txt_EntryName != null)
            {
                Txt_EntryName.text = enemySkillInfo.Name;
            }

            // 解析技能描述
            if (Txt_Entry != null)
            {
                // 使用 EnemySkillDescriptionParser 解析描述
                string parsedDescription = EnemySkillDescriptionParser.Parse(enemySkillInfo, DescriptionMode.View);
                Txt_Entry.text = parsedDescription;
            }

            // 显示面板
            gameObject.SetActive(true);
        }

        /// <summary>
        /// 显示Buff描述：标题=Buff.Name，正文=Buff.Description，
        /// {T}/{V}/{S} 占位符按 BuffState 替换；无 BuffState 时使用配置默认值/留空。
        /// </summary>
        public void Show(BuffInfo buffInfo, BuffState buffState = null)
        {
            if (buffInfo == null)
            {
                Hide();
                return;
            }

            if (Txt_EntryName != null)
            {
                Txt_EntryName.text = buffInfo.Name;
            }

            if (Txt_Entry != null)
            {
                Txt_Entry.text = ReplaceBuffPlaceholders(buffInfo.Description, buffInfo, buffState);
            }

            gameObject.SetActive(true);
        }

        /// <summary>
        /// 隐藏名词描述
        /// </summary>
        public void Hide()
        {
            gameObject.SetActive(false);
        }

        /// <summary>
        /// 设置位置
        /// </summary>
        /// <param name="position">屏幕坐标</param>
        public void SetPosition(Vector3 position)
        {
            transform.position = position;
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 替换名词描述中的占位符：{T}=BuffInfo.DefaultDuration，{V}=传入的 buffValue
        /// </summary>
        private static string ReplaceNounPlaceholders(string text, cfg.BuffInfo buffInfo, float? buffValue)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return Regex.Replace(text, @"\{([A-Z])\}", match =>
            {
                string code = match.Groups[1].Value;
                string value = code switch
                {
                    "T" => buffInfo != null && buffInfo.DefaultDuration > 0
                        ? buffInfo.DefaultDuration.ToString()
                        : match.Value,
                    "V" => buffValue.HasValue
                        ? FormatBuffValue(buffValue.Value)
                        : match.Value,
                    _ => match.Value
                };
                return $"<color=#921303>{value}</color>";
            });
        }

        /// <summary>
        /// 通过名词名称查找对应的 BuffInfo（按 Name 字段匹配）
        /// </summary>
        private static cfg.BuffInfo GetBuffInfoByName(string nounName)
        {
            var tables = ConfigLoader.Tables;
            if (tables?.TbBuffInfo == null) return null;

            foreach (var buff in tables.TbBuffInfo.DataList)
            {
                if (buff.Name == nounName)
                    return buff;
            }
            return null;
        }

        /// <summary>
        /// 从配置中获取名词描述
        /// </summary>
        private NounDictionary GetNounDescription(string nounName)
        {
            var tables = ConfigLoader.Tables;
            if (tables == null || tables.TbNounDictionary == null)
            {
                Debug.LogError("[DescriptionViewController] 配置表未加载");
                return null;
            }

            return tables.TbNounDictionary.GetOrDefault(nounName);
        }

        /// <summary>
        /// 替换Buff描述中的占位符：{T}=剩余回合，{V}=数值，{S}=层数
        /// 数值用 #921303 染色，缺省时显示 "?"
        /// </summary>
        private static string ReplaceBuffPlaceholders(string text, BuffInfo info, BuffState state)
        {
            if (string.IsNullOrEmpty(text)) return string.Empty;

            return Regex.Replace(text, @"\{([A-Z])\}", match =>
            {
                string code = match.Groups[1].Value;
                string value = code switch
                {
                    "T" => state != null
                        ? state.RemainingDuration.ToString()
                        : (info != null && info.DefaultDuration > 0 ? info.DefaultDuration.ToString() : "?"),
                    "V" => state != null ? FormatBuffValue(state.Value) : "?",
                    "S" => state != null ? state.StackCount.ToString() : "1",
                    _ => match.Value
                };
                return $"<color=#921303>{value}</color>";
            });
        }

        private static string FormatBuffValue(float value)
        {
            // 整数直接显示，否则保留1位小数
            return value == Mathf.Floor(value)
                ? ((int)value).ToString()
                : value.ToString("0.#");
        }

        #endregion
    }
}

