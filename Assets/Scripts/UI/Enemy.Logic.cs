using UnityEngine;
using UnityEngine.UI;
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
    public partial class Enemy : MonoBehaviour
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

            CaptureLayoutBaseline();
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

            LoadSkeletonAnimation();
            UpdateDisplay();
            SetIntentionThinking();

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
        /// 设置执行轨意图显示：根据技能效果决定 Attack/Shield/State 图标 + 数值。
        /// Coord 按"暗黑地牢式"展示目标位置：单体亮目标格、AOE 全亮。
        /// </summary>
        /// <param name="skillInfo">敌人技能配置</param>
        /// <param name="targetSlot">目标在我方队伍中的位置索引（0 起；AOE 时可传 -1）</param>
        /// <param name="totalSlots">我方队伍总人数（决定 Coord 总格数）</param>
        /// <param name="isAoe">是否 AOE（全体技能）</param>
        public void SetIntentionExecuting(cfg.Enemy.EnemySkillInfo skillInfo, int targetSlot, int totalSlots, bool isAoe)
        {
            if (IntentionView != null)
            {
                IntentionView.ShowFromSkill(skillInfo, targetSlot, totalSlots, isAoe);
            }
            // 兼容旧 Txt_Intention
            if (Txt_Intention != null)
                Txt_Intention.text = string.Empty;
        }

        /// <summary>
        /// 旧签名兜底：没有目标位置信息时调用，Coord 全灰。
        /// </summary>
        public void SetIntentionExecuting(cfg.Enemy.EnemySkillInfo skillInfo, string targetName)
        {
            SetIntentionExecuting(skillInfo, targetSlot: -1, totalSlots: -1, isAoe: false);
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

