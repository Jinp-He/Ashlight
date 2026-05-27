using UnityEngine;
using UnityEngine.UI;
using UnityEngine.EventSystems;
using cfg.Enemy;
using Ashlight.Config;


namespace Scripts.UI
{
    /// <summary>
    /// EnemyTimeSlot的业务逻辑部分（手动编写）
    /// 敌人技能时间槽控制器，用于显示敌人技能的引导时间、持续时间和后摇
    /// </summary>
    public partial class EnemyTimeSlot : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        #region 私有字段

        private CanvasGroup _canvasGroup;
        public EnemySkillInfo _enemySkillInfo;
        private Enemy _attacker;         // 攻击者（敌人）
        private Character _target;       // 被攻击者（角色）
        [SerializeField]
        [Header("起始槽位索引")]
        private int _slotIndex = -1;     // 起始槽位索引

        [Header("描述面板设置")]
        [SerializeField]
        [Tooltip("描述面板相对于EnemyTimeSlot的偏移（像素）。X正值向右，Y正值向上")]
        private Vector2 descriptionOffset = new Vector2(200f, 100f);

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 创建或获取CanvasGroup组件
            _canvasGroup = GetComponent<CanvasGroup>();
            if (_canvasGroup == null)
            {
                _canvasGroup = gameObject.AddComponent<CanvasGroup>();
            }

            // 默认隐藏（alpha = 0）
            _canvasGroup.alpha = 0f;
            
            // 确保可以接收鼠标事件（用于悬停检测）
            // 检查是否有 Image 组件，如果没有则添加一个
            Image image = GetComponent<Image>();
            if (image == null)
            {
                image = gameObject.AddComponent<Image>();
                image.color = new Color(1f, 1f, 1f, 0f); // 透明但可接收事件
            }
            image.raycastTarget = true; // 确保可以接收射线检测
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化并加载敌人技能数据
        /// 根据EnemySkillInfo调整三个Image的长度为 100 * 对应值
        /// </summary>
        /// <param name="enemySkillInfo">敌人技能配置信息</param>
        /// <param name="attacker">攻击者（敌人），可选</param>
        /// <param name="target">被攻击者（角色），可选</param>
        public void Init(EnemySkillInfo enemySkillInfo, Enemy attacker = null, Character target = null)
        {
            if (enemySkillInfo == null)
            {
                Debug.LogError("[EnemyTimeSlot] EnemySkillInfo为空");
                return;
            }

            // 保存技能信息
            _enemySkillInfo = enemySkillInfo;

            // 保存攻击者和被攻击者
            _attacker = attacker;
            _target = target;

            int executingCost = enemySkillInfo.ExecutingCost;
            int channeling = 0;
            int duration = executingCost;
            int recoil = 0;

            // 计算目标长度：每个单位 100 像素
            float channelingLength = 100f * channeling;
            float durationLength = 100f * duration;
            float recoilLength = 100f * recoil;

            // 调整三个Image的长度
            SetImageLength(Channeling, channelingLength);
            SetImageLength(Duration, durationLength);
            SetImageLength(Recoil, recoilLength);

            Debug.Log($"[EnemyTimeSlot] 初始化完成，技能ID: {enemySkillInfo.Id}, Channeling: {channeling}, Duration: {duration}, Recoil: {recoil}");
        }

        /// <summary>
        /// 显示时间槽（设置alpha为1）
        /// </summary>
        public void Show()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 1f;
            }
        }

        /// <summary>
        /// 隐藏时间槽（设置alpha为0）
        /// </summary>
        public void Hide()
        {
            if (_canvasGroup != null)
            {
                _canvasGroup.alpha = 0f;
            }
        }

        /// <summary>
        /// 获取敌人技能信息
        /// </summary>
        public EnemySkillInfo GetEnemySkillInfo()
        {
            return _enemySkillInfo;
        }

        /// <summary>
        /// 设置攻击者和被攻击者
        /// </summary>
        /// <param name="attacker">攻击者（敌人）</param>
        /// <param name="target">被攻击者（角色）</param>
        public void SetAttackerAndTarget(Enemy attacker, Character target)
        {
            _attacker = attacker;
            _target = target;
        }

        /// <summary>
        /// 设置槽位索引
        /// </summary>
        /// <param name="slotIndex">槽位索引</param>
        public void SetSlotIndex(int slotIndex)
        {
            _slotIndex = slotIndex;
        }

        /// <summary>
        /// 获取槽位索引
        /// </summary>
        /// <returns>槽位索引</returns>
        public int GetSlotIndex()
        {
            return _slotIndex;
        }

        #endregion
        
        #region 鼠标悬停处理
        
        /// <summary>
        /// 鼠标进入 EnemyTimeSlot
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (_enemySkillInfo == null)
            {
                return;
            }

            // 检查 DescriptionViewController 是否存在
            if (DescriptionViewController != null)
            {
                // 显示技能描述
                DescriptionViewController.Show(_enemySkillInfo);

                // 设置描述面板位置（相对于 EnemyTimeSlot 的偏移）
                Vector3 enemyTimeSlotPosition = transform.position;
                Vector3 descPosition = enemyTimeSlotPosition + new Vector3(descriptionOffset.x, descriptionOffset.y, 0f);

                Debug.Log($"[EnemyTimeSlot] 显示描述: EnemyTimeSlot位置={enemyTimeSlotPosition}, Offset={descriptionOffset}, 描述面板位置={descPosition}");

                DescriptionViewController.SetPosition(descPosition);
            }

            // 显示攻击者和被攻击者的 Indicator
            if (_attacker != null)
            {
                _attacker.ShowIndicator();
            }
            else{Debug.LogError("No Attacker");}

            if (_target != null)
            {
                _target.ShowIndicator();
            }
            else{Debug.LogError("No Target");}
        }
        
        /// <summary>
        /// 鼠标离开 EnemyTimeSlot
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 隐藏描述面板
            if (DescriptionViewController != null)
            {
                DescriptionViewController.Hide();
            }

            // 隐藏攻击者和被攻击者的 Indicator
            if (_attacker != null)
            {
                _attacker.HideIndicator();
            }

            if (_target != null)
            {
                _target.HideIndicator();
            }
        }
        
        #endregion

        #region 私有方法

        /// <summary>
        /// 设置Image的长度
        /// </summary>
        /// <param name="image">要设置的Image组件</param>
        /// <param name="length">目标长度</param>
        private void SetImageLength(Image image, float length)
        {
            if (image == null)
            {
                Debug.LogWarning("[EnemyTimeSlot] Image组件为空，跳过设置");
                return;
            }

            RectTransform rectTransform = image.rectTransform;
            if (rectTransform == null)
            {
                Debug.LogWarning("[EnemyTimeSlot] RectTransform为空，跳过设置");
                return;
            }

            // 设置宽度为指定长度，保持高度不变
            Vector2 currentSize = rectTransform.sizeDelta;
            rectTransform.sizeDelta = new Vector2(length, currentSize.y);
        }

        #endregion
    }
}



