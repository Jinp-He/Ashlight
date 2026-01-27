using UnityEngine;
using UnityEngine.EventSystems;
using cfg;
using Ashlight.Common.Events;

namespace Scripts.UI
{
    /// <summary>
    /// TeamButtonViewController的业务逻辑部分（手动编写）
    /// 队伍按钮控制器，用于管理角色队伍按钮和相关操作
    /// </summary>
    public partial class TeamButtonViewController : IPointerEnterHandler, IPointerExitHandler
    {
        #region 序列化字段

        [Header("角色设置")]
        [SerializeField]
        [Tooltip("角色类型（可从Inspector设置，也可通过Initialize方法设置）")]
        private CharacterEnum characterType = CharacterEnum.Irene;

        #endregion

        #region 私有字段

        private CharacterEnum _character;

        #endregion

        #region Unity生命周期

        /// <summary>
        /// 初始化
        /// </summary>
        private void Awake()
        {
            // 调用自动生成的UI绑定初始化方法
            InitUIBindings();

            // 从Inspector设置的值初始化角色
            _character = characterType;
            Debug.Log($"[TeamButtonViewController] 从Inspector初始化角色: {_character}");

            // 设置按钮事件
            SetupButtonListeners();

            // 初始隐藏垃圾桶按钮
            if (Btn_TrashCanBas != null)
            {
                Btn_TrashCanBas.gameObject.SetActive(false);
            }

            // 更新显示
            UpdateDisplay();
        }

        /// <summary>
        /// 销毁时清理
        /// </summary>
        private void OnDestroy()
        {
            // 移除按钮监听
            if (Btn_Slot01 != null)
            {
                Btn_Slot01.onClick.RemoveListener(OnSlotButtonClick);
            }

            if (Btn_TrashCanBas != null)
            {
                Btn_TrashCanBas.onClick.RemoveListener(OnTrashCanClick);
            }
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化队伍按钮（可覆盖Inspector设置的值）
        /// </summary>
        /// <param name="character">角色类型</param>
        public void Initialize(CharacterEnum character)
        {
            _character = character;
            characterType = character; // 同步更新序列化字段
            
            Debug.Log($"[TeamButtonViewController] 通过Initialize方法设置角色: {_character}");
            
            // 根据角色类型设置UI显示（图标、颜色等）
            UpdateDisplay();
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 设置按钮监听器
        /// </summary>
        private void SetupButtonListeners()
        {
            if (Btn_Slot01 != null)
            {
                Btn_Slot01.onClick.AddListener(OnSlotButtonClick);
            }

            if (Btn_TrashCanBas != null)
            {
                Btn_TrashCanBas.onClick.AddListener(OnTrashCanClick);
            }
        }

        /// <summary>
        /// 更新显示
        /// </summary>
        private void UpdateDisplay()
        {
            // TODO: 根据角色类型更新UI
            // 例如：设置图标、颜色、文本等
        }

        #endregion

        #region 按钮事件

        /// <summary>
        /// 槽位按钮点击事件
        /// </summary>
        private void OnSlotButtonClick()
        {
            Debug.Log($"[TeamButtonViewController] 点击队伍按钮: {_character}");

            // 发送事件，通知CardLibrary过滤卡牌
            GameEvent.Publish(new CardLibraryChangeByCharacterEvent
            {
                character = _character
            });
        }

        /// <summary>
        /// 垃圾桶按钮点击事件
        /// </summary>
        private void OnTrashCanClick()
        {
            Debug.Log($"[TeamButtonViewController] 点击垃圾桶按钮，清除角色卡牌: {_character}");

            // 发送事件，通知删除该角色的所有卡牌
            GameEvent.Publish(new ClearCharacterDeckEvent
            {
                character = _character
            });
        }

        #endregion

        #region 鼠标事件

        /// <summary>
        /// 鼠标进入
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            // 显示垃圾桶按钮
            if (Btn_TrashCanBas != null)
            {
                Btn_TrashCanBas.gameObject.SetActive(true);
            }

            // 显示指针图标（如果需要）
            if (Img_PointerIcon != null)
            {
                Img_PointerIcon.gameObject.SetActive(true);
            }

            Debug.Log($"[TeamButtonViewController] 鼠标悬停: {_character}");
        }

        /// <summary>
        /// 鼠标离开
        /// </summary>
        public void OnPointerExit(PointerEventData eventData)
        {
            // 隐藏垃圾桶按钮
            if (Btn_TrashCanBas != null)
            {
                Btn_TrashCanBas.gameObject.SetActive(false);
            }

            // 隐藏指针图标（如果需要）
            // if (Img_PointerIcon != null)
            // {
            //     Img_PointerIcon.gameObject.SetActive(false);
            // }

            Debug.Log($"[TeamButtonViewController] 鼠标离开: {_character}");
        }

        #endregion
    }
}

