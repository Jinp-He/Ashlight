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

        private Coroutine _thinkingCoroutine = null;

        #endregion

        #region Unity生命周期

        private void Awake()
        {
            // 初始化UI绑定
            InitUIBindings();

            // 默认隐藏Indicator
            if (Indicator != null)
            {
                Indicator.alpha = 0f;
            }
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

            // TODO: 实现Buff图标的显示
            // 清空现有Buff
            foreach (Transform child in BuffBase)
            {
                Destroy(child.gameObject);
            }

            // 添加新的Buff图标
            if (_unitState.Buffs != null)
            {
                foreach (var buff in _unitState.Buffs)
                {
                    // TODO: 实例化Buff图标预制体
                    //Debug.Log($"[Enemy] Buff: {buff.BuffId}, 持续: {buff.Duration}回合");
                }
            }
        }

        /// <summary>
        /// 设置规划轨意图显示："思考中..."（省略号滚动动画）
        /// </summary>
        public void SetIntentionThinking()
        {
            StopThinkingAnimation();
            _thinkingCoroutine = StartCoroutine(ThinkingAnimationCoroutine());
        }

        /// <summary>
        /// 设置执行轨意图显示：攻击类显示"目标名+伤害"，非攻击类显示"技能"
        /// </summary>
        public void SetIntentionExecuting(cfg.Enemy.EnemySkillInfo skillInfo, string targetName)
        {
            StopThinkingAnimation();

            if (Txt_Intention == null) return;

            int totalDamage = 0;
            bool hasAttack = false;

            if (skillInfo?.Effects != null)
            {
                foreach (var effect in skillInfo.Effects)
                {
                    if (effect is cfg.AttackEffect atk)
                    {
                        totalDamage += atk.Damage;
                        hasAttack = true;
                    }
                    else if (effect is cfg.AttackExtraEffect atkEx)
                    {
                        totalDamage += atkEx.Damage;
                        hasAttack = true;
                    }
                }
            }

            if (hasAttack && !string.IsNullOrEmpty(targetName))
            {
                Txt_Intention.text = $"<color=#FF6B6B>{targetName} -{totalDamage}</color>";
            }
            else
            {
                Txt_Intention.text = "<color=#7EC8E3>技能</color>";
            }
        }

        /// <summary>
        /// 清除意图显示
        /// </summary>
        public void ClearIntention()
        {
            StopThinkingAnimation();
            if (Txt_Intention != null)
                Txt_Intention.text = string.Empty;
        }

        private void StopThinkingAnimation()
        {
            if (_thinkingCoroutine != null)
            {
                StopCoroutine(_thinkingCoroutine);
                _thinkingCoroutine = null;
            }
        }

        private System.Collections.IEnumerator ThinkingAnimationCoroutine()
        {
            string[] frames = { "思考中", "思考中.", "思考中..", "思考中..." };
            int index = 0;

            while (true)
            {
                if (Txt_Intention != null)
                    Txt_Intention.text = frames[index];

                index = (index + 1) % frames.Length;
                yield return new WaitForSeconds(0.4f);
            }
        }

        /// <summary>
        /// 播放受击动画
        /// </summary>
        public void PlayHitAnimation()
        {
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
        /// 加载Spine骨骼动画
        /// </summary>
        private void LoadSkeletonAnimation()
        {
            if (_enemyInfo == null || Skeleton_Unit == null)
            {
                return;
            }

            // 使用AssetPath工具类生成Skeleton资源路径
            string skeletonPath = AssetPath.GetEnemySkeletonAssetPath(_enemyInfo.Id);
            
            Debug.Log($"[Enemy] 尝试加载Skeleton: {skeletonPath}");

            // 从Resources加载Spine骨骼数据
            var skeletonData = Resources.Load<SkeletonDataAsset>(skeletonPath);
            
            if (skeletonData != null)
            {
                Skeleton_Unit.skeletonDataAsset = skeletonData;
                Skeleton_Unit.Initialize(true);
                
                // 播放默认idle动画
                if (Skeleton_Unit.AnimationState != null)
                {
                    Skeleton_Unit.AnimationState.SetAnimation(0, "idle", true);
                }
                
                Debug.Log($"[Enemy] Skeleton加载成功: {_enemyInfo.Name}");
            }
            else
            {
                Debug.LogWarning($"[Enemy] 未找到Skeleton资源: {skeletonPath}");
                Debug.LogWarning($"[Enemy] 请确保资源位于 Resources/{skeletonPath} 路径下");
            }
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

