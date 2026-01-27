using UnityEngine;
using cfg;
using cfg.Enemy;
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
        public void Show(string nounName)
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
                Txt_Entry.text = nounDict.Desc;
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

        #endregion
    }
}

