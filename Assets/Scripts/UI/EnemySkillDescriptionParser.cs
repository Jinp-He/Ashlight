using System.Collections.Generic;
using System.Text.RegularExpressions;
using UnityEngine;
using cfg;
using cfg.Enemy;

namespace Scripts.UI
{
    /// <summary>
    /// 敌人技能描述解析器
    /// 负责解析EnemySkillInfo的Description和Effects，将标签替换为实际数值
    /// 复用CardDescriptionParser的逻辑，因为EnemySkillInfo和CardInfo结构相似
    /// </summary>
    public static class EnemySkillDescriptionParser
    {
        #region 颜色常量

        /// <summary>
        /// 数值颜色（用于{}标签内容） - 橙色
        /// </summary>
        private const string AmountColor = "#921303";

        /// <summary>
        /// 标签颜色（用于[]标签内容） - 绿色
        /// </summary>
        private const string TagColor = "#9c660a";

        #endregion

        #region 公共方法

        /// <summary>
        /// 解析敌人技能描述
        /// </summary>
        /// <param name="enemySkillInfo">敌人技能配置数据</param>
        /// <param name="mode">解析模式（阅览/战斗）</param>
        /// <returns>解析后的完整描述文本</returns>
        public static string Parse(EnemySkillInfo enemySkillInfo, DescriptionMode mode)
        {
            if (enemySkillInfo == null)
            {
                Debug.LogError("[EnemySkillDescriptionParser] EnemySkillInfo为空");
                return string.Empty;
            }

            if (string.IsNullOrEmpty(enemySkillInfo.Description))
            {
                return string.Empty;
            }

            // 获取原始描述
            string description = enemySkillInfo.Description;

            // 替换 {} 标签（效果数值）
            description = ReplaceEffectTags(description, enemySkillInfo.Effects, mode);

            // 替换 [] 标签（其他属性）
            description = ReplaceBracketTags(description, enemySkillInfo);

            return description;
        }

        #endregion

        #region 私有方法 - {} 标签替换

        /// <summary>
        /// 替换 {} 标签为Effect的实际数值
        /// 支持格式：{A}, {D}, {H}, {T}, {B}, {A:0}, {A:1} 等带索引的标签
        /// </summary>
        private static string ReplaceEffectTags(string text, List<Effect> effects, DescriptionMode mode)
        {
            if (effects == null || effects.Count == 0)
            {
                return text;
            }

            // 匹配 {字母} 或 {字母:索引} 格式
            // 例如：{A}, {D}, {A:0}, {A:1}
            Regex regex = new Regex(@"\{([A-Z])(?::(\d+))?\}");

            return regex.Replace(text, match =>
            {
                string effectTypeCode = match.Groups[1].Value; // A, D, H, T, B, N
                string indexStr = match.Groups[2].Value; // 可选的索引
                int index = string.IsNullOrEmpty(indexStr) ? 0 : int.Parse(indexStr);

                // 根据类型代码获取对应的EffectEnum
                EffectEnum effectType = GetEffectTypeFromCode(effectTypeCode);

                // 查找对应类型和索引的Effect
                int currentIndex = 0;
                foreach (Effect effect in effects)
                {
                    if (effect.EffectType == effectType)
                    {
                        if (currentIndex == index)
                        {
                            // 找到目标Effect，提取数值并添加颜色标签
                            string value = GetEffectValue(effect, mode);
                            return WrapWithColor(value, AmountColor);
                        }
                        currentIndex++;
                    }
                }

                // 未找到对应的Effect，返回原标签
                Debug.LogWarning($"[EnemySkillDescriptionParser] 未找到匹配的Effect: {match.Value}");
                return match.Value;
            });
        }

        /// <summary>
        /// 根据类型代码转换为EffectEnum
        /// </summary>
        private static EffectEnum GetEffectTypeFromCode(string code)
        {
            switch (code)
            {
                case "A": return EffectEnum.Attack;
                case "D": return EffectEnum.Defense;
                case "T": return EffectEnum.TimeSlot;
                case "H": return EffectEnum.Heal;
                case "B": return EffectEnum.Buff;
                case "N": return EffectEnum.Null;
                default:
                    Debug.LogWarning($"[EnemySkillDescriptionParser] 未知的效果类型代码: {code}");
                    return EffectEnum.Null;
            }
        }

        /// <summary>
        /// 从Effect中提取数值
        /// </summary>
        private static string GetEffectValue(Effect effect, DescriptionMode mode)
        {
            if (mode == DescriptionMode.Battle)
            {
                // 战斗模式：获取运行时数值（受buff影响）
                return GetRuntimeEffectValue(effect);
            }
            else
            {
                // 阅览模式：获取静态配置数值
                return GetStaticEffectValue(effect);
            }
        }

        /// <summary>
        /// 获取静态配置数值
        /// </summary>
        private static string GetStaticEffectValue(Effect effect)
        {
            // 根据具体的Effect类型提取对应字段
            switch (effect)
            {
                case AttackEffect attackEffect:
                    return attackEffect.Damage.ToString();

                case AttackExtraEffect attackExtraEffect:
                    return attackExtraEffect.Damage.ToString();

                case AttackConditionalEffect attackConditionalEffect:
                    return attackConditionalEffect.BonusDamage.ToString();

                case DefenseEffect defenseEffect:
                    return defenseEffect.Value.ToString();

                case InterceptEffect interceptEffect:
                    return interceptEffect.ShieldValue.ToString();

                case HealEffect healEffect:
                    return healEffect.Value.ToString();

                case PushCollisionEffect pushCollisionEffect:
                    return pushCollisionEffect.ShiftValue.ToString();

                case TimeShiftAllEffect timeShiftAllEffect:
                    return timeShiftAllEffect.ShiftValue.ToString();

                case BuffEffect buffEffect:
                    return buffEffect.Value.ToString("F1"); // Buff值保留一位小数

                default:
                    Debug.LogWarning($"[EnemySkillDescriptionParser] 未处理的Effect类型: {effect.GetType().Name}");
                    return "?";
            }
        }

        /// <summary>
        /// 获取运行时动态数值（战斗模式）
        /// </summary>
        private static string GetRuntimeEffectValue(Effect effect)
        {
            // TODO: 调用 BattleSystem 获取受 buff/装备影响后的运行时数值
            // 当前BattleSystem未实装，暂时返回静态值
            Debug.LogWarning("[EnemySkillDescriptionParser] BattleSystem未实装，战斗模式暂时使用静态数值");
            return GetStaticEffectValue(effect);
        }

        #endregion

        #region 私有方法 - [] 标签替换

        /// <summary>
        /// 替换 [] 标签为敌人技能属性
        /// 支持格式：[Channeling], [Duration], [Recoil], [TargetType] 等
        /// </summary>
        private static string ReplaceBracketTags(string text, EnemySkillInfo enemySkillInfo)
        {
            // 匹配 [属性名] 格式
            Regex regex = new Regex(@"\[(\w+)\]");

            return regex.Replace(text, match =>
            {
                string propertyName = match.Groups[1].Value;
                string value;

                switch (propertyName)
                {
                    case "ExecutingCost":
                        value = enemySkillInfo.ExecutingCost.ToString();
                        break;

                    case "TargetType":
                        value = GetTargetTypeText(enemySkillInfo.TargetType);
                        break;

                    case "Name":
                        value = enemySkillInfo.Name;
                        break;

                    default:
                        // 对于未定义的标签，保留原始内容（去掉[]括号）
                        // 例如：[僵直] -> 僵直，[引导] -> 引导
                        value = propertyName;
                        // 为未定义标签添加link，用于检测鼠标悬停并显示描述
                        return WrapWithColorAndLink(value, propertyName, TagColor);
                }

                // 为预定义属性添加绿色（不需要link）
                return WrapWithColor(value, TagColor);
            });
        }

        /// <summary>
        /// 获取目标类型的中文文本
        /// </summary>
        private static string GetTargetTypeText(TargetTypeEnum targetType)
        {
            switch (targetType)
            {
                case TargetTypeEnum.SingleAlly:
                    return "单个队友";
                case TargetTypeEnum.AllAlly:
                    return "全体队友";
                case TargetTypeEnum.Self:
                    return "自己";
                case TargetTypeEnum.SingleEnemy:
                    return "单个敌人";
                case TargetTypeEnum.AllEnemy:
                    return "全体敌人";
                case TargetTypeEnum.TimeSlot:
                    return "时间轴";
                default:
                    return targetType.ToString();
            }
        }

        #endregion

        #region 私有方法 - 工具方法

        /// <summary>
        /// 用TextMeshPro颜色标签包裹文本
        /// </summary>
        /// <param name="text">要包裹的文本</param>
        /// <param name="color">颜色（十六进制格式，如 #FF0000）</param>
        /// <returns>包裹后的文本</returns>
        private static string WrapWithColor(string text, string color)
        {
            return $"<color={color}>{text}</color>";
        }

        /// <summary>
        /// 用TextMeshPro link标签和颜色标签包裹文本（用于可交互的关键词）
        /// </summary>
        /// <param name="text">要包裹的文本</param>
        /// <param name="linkId">link的ID（用于识别）</param>
        /// <param name="color">颜色（十六进制格式）</param>
        /// <returns>包裹后的文本</returns>
        private static string WrapWithColorAndLink(string text, string linkId, string color)
        {
            return $"<link=\"{linkId}\"><color={color}>{text}</color></link>";
        }

        #endregion
    }
}



