using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using TMPro;
using cfg.Enemy;
using Ashlight.Battle.Core.Data;
using Ashlight.Config;
using Ashlight.Common.Utils;
using Spine.Unity;

namespace Scripts.UI
{
    /// <summary>
    /// Enemy的业务逻辑部分（手动编写）
    /// 敌人UI控制器，管理敌人的显示和状态更新
    /// </summary>
    public partial class Enemy : MonoBehaviour, IPointerEnterHandler, IPointerExitHandler
    {
        #region 私有字段

        private EnemyInfo _enemyInfo;
        private UnitState _unitState;
        private bool _isInitialized = false;
        private bool _isBlinking = false;
        private Coroutine _blinkCoroutine = null;
        private int _predictedHp = 0;

        // 静态敌人状态图（无 Spine 骨骼时用）
        private bool _isStaticSprite = false;
        private Sprite _idleSprite;
        private Sprite _hitSprite;
        private Sprite _attackSprite;
        private Sprite _castSprite;
        private Coroutine _staticStateCoroutine;

        // 初始基线，用于 sprite 换图后联动 IntentionView 位置
        private bool _baselineCaptured = false;
        private float _initialEnemyImageHeight;
        private Vector2 _initialIntentionAnchoredPos;

        // 敌人悬停名称：运行时创建，避免要求所有现有 EnemyPrefab 手工补绑定。
        private RectTransform _hoverNameRoot;
        private TextMeshProUGUI _hoverNameText;
        private const string HoverNameNodeName = "HoverName";
        private static readonly Vector2 HoverNameSize = new Vector2(190f, 30f);
        private const float HoverNameGap = 6f;

        #endregion

        #region Unity生命周期

        private void Awake()
        {
            // 初始化UI绑定
            InitUIBindings();

            // 兜底：UIBind 未配置时按组件类型抓 IntentionView
            if (IntentionView == null)
            {
                IntentionView = GetComponentInChildren<IntentionView>(true);
                if (IntentionView == null)
                {
                    Debug.LogWarning("[Enemy] 未找到 IntentionView 子节点：请将 UI_意图 prefab 拖入并挂 UIBind(BindName=IntentionView)，或确保是子节点");
                }
            }

            // 默认隐藏Indicator
            if (Indicator != null)
            {
                Indicator.alpha = 0f;
            }

            EnsureHoverName();
            SetHoverNameVisible(false);
            CaptureLayoutBaseline();
        }

        private void OnDisable()
        {
            SetHoverNameVisible(false);
        }

        private void CaptureLayoutBaseline()
        {
            if (_baselineCaptured) return;
            if (EnemyImage != null)
            {
                _initialEnemyImageHeight = EnemyImage.rectTransform.rect.height;
            }
            if (IntentionView != null)
            {
                var rt = IntentionView.transform as RectTransform;
                if (rt != null)
                {
                    _initialIntentionAnchoredPos = rt.anchoredPosition;
                }
            }
            _baselineCaptured = true;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 初始化敌人UI（使用配置信息）
        /// </summary>
        /// <param name="enemyInfo">敌人配置信息</param>
        public void Initialize(EnemyInfo enemyInfo)
        {
            if (enemyInfo == null)
            {
                Debug.LogError("[Enemy] 初始化失败：EnemyInfo为null");
                return;
            }

            _enemyInfo = enemyInfo;
            _isInitialized = true;

            EnsureHoverName();
            if (_hoverNameText != null)
            {
                _hoverNameText.text = enemyInfo.Name ?? string.Empty;
            }
            LoadSkeletonAnimation();
            UpdateDisplay();
            SetIntentionThinking();
            RebuildHitArea();

            Debug.Log($"[Enemy] 初始化完成: {enemyInfo.Name}");
        }

        /// <summary>
        /// 初始化敌人UI（使用战斗单位状态）
        /// </summary>
        /// <param name="unitState">战斗单位状态</param>
        public void Initialize(UnitState unitState)
        {
            if (unitState == null)
            {
                Debug.LogError("[Enemy] 初始化失败：UnitState为null");
                return;
            }

            _unitState = unitState;

            // 根据ConfigId获取敌人配置
            var enemyInfo = ConfigLoader.Tables.TbEnemyInfo.GetOrDefault(unitState.ConfigId);
            if (enemyInfo != null)
            {
                Initialize(enemyInfo);
                
                // 更新血量和护甲
                UpdateHp(unitState.CurrentHp, unitState.MaxHp);
                UpdateShield(unitState.Defense);
            }
            else
            {
                Debug.LogError($"[Enemy] 未找到敌人配置: {unitState.ConfigId}");
            }
        }

        /// <summary>
        /// 更新血量显示
        /// </summary>
        /// <param name="currentHp">当前血量</param>
        /// <param name="maxHp">最大血量</param>
        public void UpdateHp(int currentHp, int maxHp)
        {
            if (!_isInitialized)
            {
                Debug.LogWarning("[Enemy] 未初始化，无法更新血量");
                return;
            }

            // 更新血量文本
            if (Txt_Hp != null)
            {
                Txt_Hp.text = $"{currentHp}/{maxHp}";
            }

            // 更新血量条填充
            if (Fill_Hp != null)
            {
                float fillAmount = maxHp > 0 ? (float)currentHp / maxHp : 0f;
                Fill_Hp.fillAmount = fillAmount;
                
                // 保持白色
                Fill_Hp.color = Color.white;
            }

            // 同步更新UnitState
            if (_unitState != null)
            {
                _unitState.CurrentHp = currentHp;
            }
        }

        /// <summary>
        /// 更新护甲显示
        /// </summary>
        /// <param name="shieldValue">护甲值</param>
        public void UpdateShield(int shieldValue)
        {
            if (!_isInitialized) return;

            // 显示或隐藏护甲UI
            if (Shield != null)
            {
                Shield.gameObject.SetActive(shieldValue > 0);
            }

            // 更新护甲数值
            if (Txt_Shield != null)
            {
                Txt_Shield.text = shieldValue.ToString();
            }

            // 同步更新UnitState
            if (_unitState != null)
            {
                _unitState.Defense = shieldValue;
            }
        }

        /// <summary>
        /// 更新完整状态（从UnitState同步）
        /// </summary>
        public void UpdateFromUnitState()
        {
            if (_unitState == null)
            {
                Debug.LogWarning("[Enemy] UnitState为null，无法更新");
                return;
            }

            UpdateHp(_unitState.CurrentHp, _unitState.MaxHp);
            UpdateShield(_unitState.Defense);
            UpdateBuffs();
        }

        /// <summary>
        /// 更新Buff显示
        /// </summary>
        public void UpdateBuffs()
        {
            if (_unitState == null || BuffBase == null) return;

            // 清空现有 Buff 图标
            foreach (Transform child in BuffBase)
            {
                Destroy(child.gameObject);
            }

            if (_unitState.Buffs == null || _unitState.Buffs.Count == 0) return;

            var prefab = ResolveBuffPrefab();
            if (prefab == null)
            {
                Debug.LogWarning($"[Enemy] 找不到 UI_Buff prefab：Resources/{BuffPrefabResourcePath}");
                return;
            }

            foreach (var buff in _unitState.Buffs)
            {
                if (buff == null) continue;
                // Resources.Load<UI_Buff> + Instantiate 重载直接返回 UI_Buff，免 GetComponent
                // 第三个参数 worldPositionStays=false 对 UI 元素必须（沿用 prefab anchored 设定）
                var ui = Instantiate(prefab, BuffBase, false);
                ui.Initialize(buff);
            }
        }

        private const string BuffPrefabResourcePath = "UI/BattleScene/UI_Buff";
        private static UI_Buff _cachedBuffPrefab;

        private static UI_Buff ResolveBuffPrefab()
        {
            if (_cachedBuffPrefab == null)
            {
                _cachedBuffPrefab = Resources.Load<UI_Buff>(BuffPrefabResourcePath);
            }
            return _cachedBuffPrefab;
        }

        /// <summary>
        /// 设置规划轨意图显示：思考图标 + 坐标全灰
        /// </summary>
        public void SetIntentionThinking()
        {
            if (IntentionView != null)
            {
                IntentionView.ShowThinking();
            }
            // 兼容旧 Txt_Intention（如 prefab 尚未切换到 IntentionView）
            if (Txt_Intention != null)
                Txt_Intention.text = string.Empty;
        }

        /// <summary>
        /// 设置执行轨意图显示：根据技能效果决定 Melee/Remote/Shield/State 图标与数值。
        /// Coord 使用单体/AOE素材，并按目标区显示前排红、后排蓝。
        /// </summary>
        /// <param name="skillInfo">敌人技能配置</param>
        /// <param name="targetUnitId">当前锁定的目标 UnitId（供悬停抛物线指向；空区未锁人时为 null）</param>
        public void SetIntentionExecuting(cfg.Enemy.EnemySkillInfo skillInfo, string targetUnitId = null)
        {
            if (IntentionView != null)
            {
                IntentionView.ShowFromSkill(skillInfo, targetUnitId);
            }
            // 兼容旧 Txt_Intention
            if (Txt_Intention != null)
                Txt_Intention.text = string.Empty;
        }

        /// <summary>
        /// 清除意图显示
        /// </summary>
        public void ClearIntention()
        {
            if (IntentionView != null)
                IntentionView.Hide();
            if (Txt_Intention != null)
                Txt_Intention.text = string.Empty;
        }

        /// <summary>
        /// 播放受击动画
        /// </summary>
        public void PlayHitAnimation()
        {
            if (_isStaticSprite)
            {
                SetStaticSprite(_hitSprite, 0.5f);
                return;
            }
            if (Skeleton_Unit != null && Skeleton_Unit.AnimationState != null)
            {
                // 播放受击动画
                Skeleton_Unit.AnimationState.SetAnimation(0, "hit", false);
                // 动画结束后回到idle
                Skeleton_Unit.AnimationState.AddAnimation(0, "idle", true, 0f);
            }
        }

        /// <summary>
        /// 播放攻击动画
        /// </summary>
        public void PlayAttackAnimation()
        {
            if (_isStaticSprite)
            {
                SetStaticSprite(_attackSprite, 0.5f);
                return;
            }
            if (Skeleton_Unit != null && Skeleton_Unit.AnimationState != null)
            {
                Skeleton_Unit.AnimationState.SetAnimation(0, "attack1", false);
                // 延迟0.5秒后切换到idle，保持attack动画状态
                Skeleton_Unit.AnimationState.AddAnimation(0, "idle", true, 0.5f);
            }
        }

        /// <summary>
        /// 播放死亡动画
        /// </summary>
        public void PlayDeathAnimation()
        {
            if (_isStaticSprite)
            {
                // 静态敌人无死亡动画，保持当前 sprite
                return;
            }
            if (Skeleton_Unit != null && Skeleton_Unit.AnimationState != null)
            {
                Skeleton_Unit.AnimationState.SetAnimation(0, "death", false);
            }
        }

        /// <summary>
        /// 播放受击动画（shouji）
        /// </summary>
        public void PlayShoujiAnimation()
        {
            if (_isStaticSprite)
            {
                SetStaticSprite(_hitSprite, 0.5f);
                return;
            }
            if (Skeleton_Unit?.AnimationState == null)
            {
                Debug.LogWarning("[Enemy] Skeleton_Unit or AnimationState is null");
                return;
            }

            var skeletonData = Skeleton_Unit.AnimationState.Data?.SkeletonData;
            if (skeletonData == null)
            {
                Debug.LogWarning("[Enemy] SkeletonData is null");
                return;
            }

            var shoujiAnim = skeletonData.FindAnimation("shouji");
            if (shoujiAnim != null)
            {
                Skeleton_Unit.AnimationState.SetAnimation(0, "shouji", false);
                // 延迟0.5秒后切换到idle，保持shouji动画状态
                Skeleton_Unit.AnimationState.AddAnimation(0, "idle", true, 0.5f);
                Debug.Log("[Enemy] Playing 'shouji' animation");
            }
            else
            {
                Debug.LogWarning("[Enemy] 'shouji' animation not found, using 'hit'");
                var hitAnim = skeletonData.FindAnimation("hit");
                if (hitAnim != null)
                {
                    Skeleton_Unit.AnimationState.SetAnimation(0, "hit", false);
                    // 延迟0.5秒后切换到idle，保持hit动画状态
                    Skeleton_Unit.AnimationState.AddAnimation(0, "idle", true, 0.5f);
                    Debug.Log("[Enemy] Playing 'hit' animation");
                }
                else
                {
                    Debug.LogError("[Enemy] Neither 'shouji' nor 'hit' animation found!");
                    return;
                }
            }
        }

        /// <summary>
        /// 获取攻击动画持续时间（固定返回0.5秒，因为动画是静态图片）
        /// </summary>
        public float GetAttackAnimationDuration()
        {
            return 0.5f;
        }

        /// <summary>
        /// 获取shouji动画持续时间（固定返回0.5秒，因为动画是静态图片）
        /// </summary>
        public float GetShoujiAnimationDuration()
        {
            return 0.5f;
        }

        /// <summary>
        /// 获取敌人配置信息
        /// </summary>
        public EnemyInfo GetEnemyInfo()
        {
            return _enemyInfo;
        }

        /// <summary>
        /// 获取战斗单位状态
        /// </summary>
        public UnitState GetUnitState()
        {
            return _unitState;
        }

        /// <summary>
        /// 测试方法：模拟受到伤害
        /// </summary>
        /// <param name="damage">伤害值</param>
        public void TestTakeDamage(int damage)
        {
            if (_unitState == null)
            {
                Debug.LogWarning("[Enemy] UnitState为null，无法测试伤害");
                return;
            }

            int newHp = Mathf.Max(0, _unitState.CurrentHp - damage);
            Debug.Log($"[Enemy] 测试伤害: {_unitState.CurrentHp} -> {newHp} (-{damage})");
            
            UpdateHp(newHp, _unitState.MaxHp);
            PlayHitAnimation();

            if (newHp <= 0)
            {
                PlayDeathAnimation();
                Debug.Log($"[Enemy] 敌人死亡");
            }
        }

        /// <summary>
        /// 测试方法：模拟恢复生命
        /// </summary>
        /// <param name="heal">恢复值</param>
        public void TestHeal(int heal)
        {
            if (_unitState == null)
            {
                Debug.LogWarning("[Enemy] UnitState为null，无法测试治疗");
                return;
            }

            int newHp = Mathf.Min(_unitState.MaxHp, _unitState.CurrentHp + heal);
            Debug.Log($"[Enemy] 测试治疗: {_unitState.CurrentHp} -> {newHp} (+{heal})");

            UpdateHp(newHp, _unitState.MaxHp);
        }

        /// <summary>
        /// 显示指示器
        /// </summary>
        public void ShowIndicator()
        {
            if (Indicator != null)
            {
                Indicator.alpha = 1f;
            }
        }

        /// <summary>
        /// 显示带颜色的指示器
        /// </summary>
        /// <param name="color">指示器颜色</param>
        public void ShowIndicator(Color color)
        {
            if (Indicator != null)
            {
                Indicator.alpha = 1f;
                var image = Indicator.GetComponent<Image>();
                if (image != null)
                {
                    image.color = color;
                }
            }
        }

        /// <summary>
        /// 隐藏指示器
        /// </summary>
        public void HideIndicator()
        {
            if (Indicator != null)
            {
                Indicator.alpha = 0f;
            }
        }

        /// <summary>
        /// 设置Skeleton颜色（用于攻击演出效果）
        /// </summary>
        /// <param name="color">颜色</param>
        public void SetColor(Color color)
        {
            if (_isStaticSprite && EnemyImage != null)
            {
                EnemyImage.color = color;
                return;
            }
            if (Skeleton_Unit != null)
            {
                // 方法1: 直接设置 SkeletonGraphic 的颜色（推荐用于 UI）
                Skeleton_Unit.color = color;

                // 方法2: 同时设置 Skeleton 的 RGBA（确保完全生效）
                if (Skeleton_Unit.Skeleton != null)
                {
                    Skeleton_Unit.Skeleton.R = color.r;
                    Skeleton_Unit.Skeleton.G = color.g;
                    Skeleton_Unit.Skeleton.B = color.b;
                    Skeleton_Unit.Skeleton.A = color.a;
                }
            }
        }

        /// <summary>
        /// 设置缩放（用于攻击演出效果）
        /// </summary>
        /// <param name="scale">缩放比例</param>
        public void SetScale(float scale)
        {
            transform.localScale = Vector3.one * scale;
        }

        /// <summary>
        /// 开始血量预测闪烁
        /// </summary>
        /// <param name="predictedHp">预测的血量值</param>
        public void StartHpPredictionBlink(int predictedHp)
        {
            if (_unitState == null)
            {
                Debug.LogWarning("[Enemy] UnitState为null，无法开始预测闪烁");
                return;
            }

            // 停止之前的闪烁
            StopHpPredictionBlink();

            _predictedHp = predictedHp;
            _isBlinking = true;

            // 启动闪烁协程
            _blinkCoroutine = StartCoroutine(HpBlinkCoroutine());

            Debug.Log($"[Enemy] 开始血量预测闪烁: {_unitState.UnitId}, 当前={_unitState.CurrentHp}, 预测={predictedHp}");
        }

        /// <summary>
        /// 停止血量预测闪烁
        /// </summary>
        public void StopHpPredictionBlink()
        {
            if (!_isBlinking)
            {
                return;
            }

            _isBlinking = false;

            // 停止协程
            if (_blinkCoroutine != null)
            {
                StopCoroutine(_blinkCoroutine);
                _blinkCoroutine = null;
            }

            // 恢复显示当前血量
            if (_unitState != null)
            {
                UpdateHpDisplay(_unitState.CurrentHp, _unitState.MaxHp);
            }

            Debug.Log($"[Enemy] 停止血量预测闪烁: {_unitState?.UnitId}");
        }

        #endregion

        #region 私有方法

        /// <summary>
        /// 加载敌人视觉：优先 Spine 骨骼；若该敌人没有骨骼资源，则回退为静态 Sprite。
        /// </summary>
        private void LoadSkeletonAnimation()
        {
            if (_enemyInfo == null)
            {
                return;
            }

            // AlternativePath 决定资源文件夹（多个 EnemyInfo 可共用同一套美术）
            string artId = string.IsNullOrEmpty(_enemyInfo.AlternativePath) ? _enemyInfo.Id : _enemyInfo.AlternativePath;

            string skeletonPath = AssetPath.GetEnemySkeletonAssetPath(artId);
            var skeletonData = Resources.Load<SkeletonDataAsset>(skeletonPath);

            if (skeletonData != null && Skeleton_Unit != null)
            {
                Skeleton_Unit.gameObject.SetActive(true);
                Skeleton_Unit.skeletonDataAsset = skeletonData;
                Skeleton_Unit.Initialize(true);

                if (Skeleton_Unit.AnimationState != null)
                {
                    Skeleton_Unit.AnimationState.SetAnimation(0, "idle", true);
                }

                if (EnemyImage != null)
                {
                    EnemyImage.gameObject.SetActive(false);
                }

                CenterSkeletonVisual();

                Debug.Log($"[Enemy] Skeleton加载成功: {_enemyInfo.Name} ({artId})");
                return;
            }

            // 回退：用静态 Sprite。优先用 idle.png 作为站立图；缺失时退回主图。
            string spriteFolder = System.IO.Path.GetDirectoryName(AssetPath.GetEnemySpritesAssetPath(artId)).Replace('\\', '/');
            _idleSprite = Resources.Load<Sprite>($"{spriteFolder}/idle") ?? Resources.Load<Sprite>(AssetPath.GetEnemySpritesAssetPath(artId));
            _hitSprite = Resources.Load<Sprite>($"{spriteFolder}/hit");
            _attackSprite = Resources.Load<Sprite>($"{spriteFolder}/attack");
            _castSprite = Resources.Load<Sprite>($"{spriteFolder}/cast");

            if (_idleSprite != null && EnemyImage != null)
            {
                if (Skeleton_Unit != null)
                {
                    Skeleton_Unit.gameObject.SetActive(false);
                }

                _isStaticSprite = true;
                EnemyImage.gameObject.SetActive(true);
                ApplyStaticSprite(_idleSprite);

                Debug.Log($"[Enemy] 使用静态Sprite: {_enemyInfo.Name} ({spriteFolder})");
                return;
            }

            Debug.LogWarning($"[Enemy] 找不到骨骼或Sprite: skeleton={skeletonPath}, sprite folder={spriteFolder}");
        }

        private void SetStaticSprite(Sprite sprite, float revertAfter)
        {
            if (!_isStaticSprite || EnemyImage == null || sprite == null) return;

            if (_staticStateCoroutine != null)
            {
                StopCoroutine(_staticStateCoroutine);
                _staticStateCoroutine = null;
            }

            ApplyStaticSprite(sprite);

            if (revertAfter > 0f && _idleSprite != null)
            {
                _staticStateCoroutine = StartCoroutine(RevertToIdleAfter(revertAfter));
            }
        }

        private System.Collections.IEnumerator RevertToIdleAfter(float delay)
        {
            yield return new WaitForSeconds(delay);
            if (EnemyImage != null && _idleSprite != null)
            {
                ApplyStaticSprite(_idleSprite);
            }
            _staticStateCoroutine = null;
        }

        /// <summary>
        /// 给 EnemyImage 换 sprite + SetNativeSize，并联动 IntentionView 跟随顶部偏移。
        /// </summary>
        private void ApplyStaticSprite(Sprite sprite)
        {
            if (EnemyImage == null || sprite == null) return;

            EnemyImage.sprite = sprite;
            EnemyImage.preserveAspect = true;
            EnemyImage.SetNativeSize();

            UpdateIntentionFollowImage();
        }

        /// <summary>
        /// EnemyImage Pivot 为 (0.5, 0) 时，SetNativeSize 后图像底部不变、顶部上升 (newH - baseH)。
        /// 把这个 delta 加到 IntentionView 的初始 anchoredPosition 上即可保持"贴顶"。
        /// </summary>
        private void UpdateIntentionFollowImage()
        {
            if (IntentionView == null || EnemyImage == null) return;

            var rt = IntentionView.transform as RectTransform;
            if (rt == null) return;

            float deltaY = EnemyImage.rectTransform.rect.height - _initialEnemyImageHeight;
            rt.anchoredPosition = new Vector2(
                _initialIntentionAnchoredPos.x,
                _initialIntentionAnchoredPos.y + deltaY);
        }

        /// <summary>
        /// 用骨骼边界框把 Skeleton_Unit 的可视中心拉回到 RectTransform 锚点正下方/正中央，
        /// 解决"DogKnight 比其他人偏右"导致血条/Indicator 看着不居中的问题。
        /// </summary>
        private void CenterSkeletonVisual()
        {
            if (Skeleton_Unit == null || Skeleton_Unit.Skeleton == null) return;

            // 必须先更新世界变换，bounds 才有效
            Skeleton_Unit.Skeleton.UpdateWorldTransform();

            float[] vertexBuffer = null;
            Skeleton_Unit.Skeleton.GetBounds(out float minX, out float minY, out float maxX, out float maxY, ref vertexBuffer);

            if (maxX <= minX) return;

            var rt = Skeleton_Unit.rectTransform;
            float centerX = (minX + maxX) * 0.5f;
            float scaleX = rt.localScale.x;

            // 反向偏移 RectTransform，让骨骼视觉中心落到当前锚点 x 上
            rt.anchoredPosition = new Vector2(
                rt.anchoredPosition.x - centerX * scaleX,
                rt.anchoredPosition.y);
        }

        #region 点击命中区 (HitArea)

        [Header("点击命中区")]
        [Tooltip("命中区最大尺寸(像素)。防止超大图的透明边缘遮挡相邻敌人——宽度务必小于敌人间距(默认 200)")]
        [SerializeField] private Vector2 _hitAreaMaxSize = new Vector2(180f, 320f);
        [Tooltip("命中区最小尺寸，保证小体型敌人也点得到")]
        [SerializeField] private Vector2 _hitAreaMinSize = new Vector2(110f, 160f);
        [Tooltip("命中区底部相对视觉脚底的竖直偏移(正数上移)")]
        [SerializeField] private float _hitAreaBottomOffsetY = 0f;
        [Tooltip("调试：把命中区染成半透明红色以便观察")]
        [SerializeField] private bool _hitAreaDebugVisible = false;

        private RectTransform _hitArea;
        private Image _hitAreaImage;
        private const string HitAreaNodeName = "HitArea";

        /// <summary>
        /// 关掉大图视觉(Image/Skeleton/Indicator)的射线命中，改用一个尺寸受控的透明 HitArea
        /// 作为本敌人的唯一可点击区域。解决"占比过大的图，其透明矩形包围盒遮挡相邻敌人导致点不中"。
        /// HitArea 是 Enemy 子节点，TargetSelectionManager 的 GetComponentInParent&lt;Enemy&gt;() 会自动归属到本敌人。
        /// </summary>
        private void RebuildHitArea()
        {
            // 1) 关闭视觉自身的射线命中，避免大包围盒(含透明边缘)挡住别人
            if (EnemyImage != null) EnemyImage.raycastTarget = false;
            if (Skeleton_Unit != null) Skeleton_Unit.raycastTarget = false;
            if (Indicator != null)
            {
                var indImg = Indicator.GetComponent<Image>();
                if (indImg != null) indImg.raycastTarget = false;
            }

            // 2) 计算当前视觉的可见包围盒(已换算到根节点本地坐标)
            Rect visual = CalcVisualBounds();
            UpdateHoverNamePosition(visual);

            // 3) clamp 到受控尺寸，宽度不超过敌人间距，避免命中区互相重叠
            float w = Mathf.Clamp(visual.width, _hitAreaMinSize.x, _hitAreaMaxSize.x);
            float h = Mathf.Clamp(visual.height, _hitAreaMinSize.y, _hitAreaMaxSize.y);

            float centerX = visual.center.x;
            float bottomY = visual.yMin + _hitAreaBottomOffsetY; // 贴脚底
            float centerY = bottomY + h * 0.5f;

            EnsureHitAreaNode();
            _hitArea.sizeDelta = new Vector2(w, h);
            _hitArea.localPosition = new Vector3(centerX, centerY, 0f);

            if (_hitAreaImage != null)
            {
                _hitAreaImage.color = _hitAreaDebugVisible
                    ? new Color(1f, 0f, 0f, 0.25f)
                    : new Color(0f, 0f, 0f, 0f);
            }

            // HitArea 置顶后必须把 IntentionView 重新提到它之上：
            // 高个子敌人的命中区(≤320px)会一路铺到头顶的意图图标，UGUI 射线取最上层图形，
            // 意图的 hover(tooltip/抛物线)会被透明命中区整个吃掉。
            if (IntentionView != null)
            {
                IntentionView.transform.SetAsLastSibling();
            }
        }

        /// <summary>
        /// 中央战斗演出使用的静态视觉帧。只返回素材，不修改战场上的真实 EnemyImage。
        /// </summary>
        public Sprite GetBattlePresentationSprite(bool attack)
        {
            return attack
                ? (_attackSprite != null ? _attackSprite : _idleSprite)
                : (_hitSprite != null ? _hitSprite : _idleSprite);
        }

        /// <summary>
        /// 敌人命中区悬停时，在视觉脚底下方显示名称。
        /// </summary>
        public void OnPointerEnter(PointerEventData eventData)
        {
            if (!_isInitialized || _enemyInfo == null) return;

            EnsureHoverName();
            if (_hoverNameText != null)
            {
                _hoverNameText.text = _enemyInfo.Name ?? string.Empty;
            }
            SetHoverNameVisible(true);
        }

        public void OnPointerExit(PointerEventData eventData)
        {
            SetHoverNameVisible(false);
        }

        private void EnsureHoverName()
        {
            if (_hoverNameRoot != null) return;

            var existing = transform.Find(HoverNameNodeName) as RectTransform;
            if (existing != null)
            {
                _hoverNameRoot = existing;
                _hoverNameText = existing.GetComponentInChildren<TextMeshProUGUI>(true);
                return;
            }

            var rootGo = new GameObject(HoverNameNodeName, typeof(RectTransform), typeof(Image));
            _hoverNameRoot = rootGo.GetComponent<RectTransform>();
            _hoverNameRoot.SetParent(transform, false);
            _hoverNameRoot.anchorMin = _hoverNameRoot.anchorMax = new Vector2(0.5f, 0.5f);
            _hoverNameRoot.pivot = new Vector2(0.5f, 0.5f);
            _hoverNameRoot.sizeDelta = HoverNameSize;

            var background = rootGo.GetComponent<Image>();
            background.color = new Color(0.04f, 0.04f, 0.04f, 0.78f);
            background.raycastTarget = false;

            var textGo = new GameObject("Txt_Name", typeof(RectTransform), typeof(TextMeshProUGUI));
            var textRect = textGo.GetComponent<RectTransform>();
            textRect.SetParent(_hoverNameRoot, false);
            textRect.anchorMin = Vector2.zero;
            textRect.anchorMax = Vector2.one;
            textRect.offsetMin = new Vector2(8f, 1f);
            textRect.offsetMax = new Vector2(-8f, -1f);

            _hoverNameText = textGo.GetComponent<TextMeshProUGUI>();
            _hoverNameText.font = Txt_Hp != null ? Txt_Hp.font : null;
            _hoverNameText.fontSize = 21f;
            _hoverNameText.color = Color.white;
            _hoverNameText.alignment = TextAlignmentOptions.Center;
            _hoverNameText.overflowMode = TextOverflowModes.Ellipsis;
            _hoverNameText.raycastTarget = false;
            _hoverNameText.text = _enemyInfo?.Name ?? string.Empty;
        }

        private void UpdateHoverNamePosition(Rect visual)
        {
            EnsureHoverName();
            if (_hoverNameRoot == null) return;

            float centerY = visual.yMin - HoverNameGap - HoverNameSize.y * 0.5f;
            _hoverNameRoot.anchoredPosition = new Vector2(visual.center.x, centerY);
        }

        private void SetHoverNameVisible(bool visible)
        {
            if (_hoverNameRoot == null) return;

            _hoverNameRoot.gameObject.SetActive(visible);
            if (visible)
            {
                // 名称只负责显示，不参与射线；提到最上层避免被敌人图像或血条遮住。
                _hoverNameRoot.SetAsLastSibling();
            }
        }

        /// <summary>
        /// 复用或创建 HitArea 子节点(透明、可命中射线)。运行时创建，匹配敌人动态生成。
        /// </summary>
        private void EnsureHitAreaNode()
        {
            if (_hitArea == null)
            {
                var existing = transform.Find(HitAreaNodeName) as RectTransform;
                if (existing != null)
                {
                    _hitArea = existing;
                    _hitAreaImage = existing.GetComponent<Image>();
                }
                else
                {
                    var go = new GameObject(HitAreaNodeName, typeof(RectTransform), typeof(Image));
                    _hitArea = go.GetComponent<RectTransform>();
                    _hitArea.SetParent(transform, false);
                    _hitAreaImage = go.GetComponent<Image>();
                }
            }

            // anchorMin==anchorMax 时 sizeDelta 即真实尺寸；位置统一用 localPosition 设定
            _hitArea.anchorMin = _hitArea.anchorMax = _hitArea.pivot = new Vector2(0.5f, 0.5f);

            if (_hitAreaImage != null)
            {
                _hitAreaImage.sprite = null;
                _hitAreaImage.raycastTarget = true; // 透明 Image 依然命中射线(UGUI 默认不按 alpha 剔除)
                _hitAreaImage.color = new Color(0f, 0f, 0f, 0f);
            }

            // 置于最上层，保证在本敌人内部优先命中
            _hitArea.SetAsLastSibling();
        }

        /// <summary>
        /// 计算当前视觉(静态图或骨骼)的可见包围盒，换算到根节点本地坐标系。
        /// 用世界角点 + InverseTransformPoint，规避各视觉 pivot/scale 差异。
        /// </summary>
        private Rect CalcVisualBounds()
        {
            var root = transform as RectTransform;

            if (_isStaticSprite && EnemyImage != null && EnemyImage.gameObject.activeInHierarchy)
            {
                var corners = new Vector3[4];
                EnemyImage.rectTransform.GetWorldCorners(corners);
                return WorldCornersToLocalRect(root, corners);
            }

            if (Skeleton_Unit != null && Skeleton_Unit.gameObject.activeInHierarchy && Skeleton_Unit.Skeleton != null)
            {
                Skeleton_Unit.Skeleton.UpdateWorldTransform();
                float[] vb = null;
                Skeleton_Unit.Skeleton.GetBounds(out float minX, out float minY, out float maxX, out float maxY, ref vb);
                if (maxX > minX)
                {
                    var srt = Skeleton_Unit.rectTransform;
                    var corners = new Vector3[4];
                    corners[0] = srt.TransformPoint(new Vector3(minX, minY, 0f));
                    corners[1] = srt.TransformPoint(new Vector3(minX, maxY, 0f));
                    corners[2] = srt.TransformPoint(new Vector3(maxX, maxY, 0f));
                    corners[3] = srt.TransformPoint(new Vector3(maxX, minY, 0f));
                    return WorldCornersToLocalRect(root, corners);
                }
            }

            // 兜底：无视觉时给个最小框，脚底起算
            return Rect.MinMaxRect(-_hitAreaMinSize.x * 0.5f, 0f, _hitAreaMinSize.x * 0.5f, _hitAreaMinSize.y);
        }

        private static Rect WorldCornersToLocalRect(RectTransform root, Vector3[] worldCorners)
        {
            float minX = float.MaxValue, minY = float.MaxValue, maxX = float.MinValue, maxY = float.MinValue;
            foreach (var wc in worldCorners)
            {
                Vector3 l = root.InverseTransformPoint(wc);
                if (l.x < minX) minX = l.x;
                if (l.x > maxX) maxX = l.x;
                if (l.y < minY) minY = l.y;
                if (l.y > maxY) maxY = l.y;
            }
            return Rect.MinMaxRect(minX, minY, maxX, maxY);
        }

        #endregion

        /// <summary>
        /// 更新显示
        /// </summary>
        private void UpdateDisplay()
        {
            if (_enemyInfo == null) return;

            // 初始化血量显示
            if (_unitState != null)
            {
                UpdateHp(_unitState.CurrentHp, _unitState.MaxHp);
                UpdateShield(_unitState.Defense);
            }
            else
            {
                UpdateHp(_enemyInfo.Hp, _enemyInfo.Hp);
                UpdateShield(0);
            }
        }

        /// <summary>
        /// 血量闪烁协程（在当前血量和预测血量之间来回切换FillImage）
        /// </summary>
        private System.Collections.IEnumerator HpBlinkCoroutine()
        {
            int currentHp = _unitState.CurrentHp;
            int maxHp = _unitState.MaxHp;
            int predictedHp = _predictedHp;
            bool showPredicted = false;

            while (_isBlinking)
            {
                if (showPredicted)
                {
                    // 显示预测血量
                    UpdateHpDisplay(predictedHp, maxHp);
                }
                else
                {
                    // 显示当前血量
                    UpdateHpDisplay(currentHp, maxHp);
                }

                showPredicted = !showPredicted;
                yield return new WaitForSeconds(0.5f);
            }

            // 闪烁结束，恢复显示当前血量
            UpdateHpDisplay(currentHp, maxHp);
        }

        /// <summary>
        /// 更新血量显示（内部方法，不修改UnitState）
        /// </summary>
        /// <param name="hp">要显示的血量值</param>
        /// <param name="maxHp">最大血量</param>
        private void UpdateHpDisplay(int hp, int maxHp)
        {
            if (!_isInitialized)
            {
                return;
            }

            // 更新血量文本
            if (Txt_Hp != null)
            {
                Txt_Hp.text = $"{hp}/{maxHp}";
            }

            // 更新血量条填充
            if (Fill_Hp != null)
            {
                float fillAmount = maxHp > 0 ? (float)hp / maxHp : 0f;
                Fill_Hp.fillAmount = fillAmount;
                Fill_Hp.color = Color.white;
            }
        }

        #endregion
    }
}

