using System;

namespace Ashlight.State.Runtime
{
    /// <summary>
    /// 卡牌运行时状态 - 存储卡牌的动态数据
    /// 状态层：支持 Save/Load，纯 C# 数据结构
    /// </summary>
    [Serializable]
    public class CardRuntimeState
    {
        /// <summary>
        /// 卡牌ID（引用配置表 CardInfo）
        /// </summary>
        public string CardId;

        /// <summary>
        /// 卡牌实例唯一标识符（用于关联 UI 层的 CardViewController）
        /// 同一张卡牌（相同 CardId）的不同实例会有不同的 InstanceId
        /// </summary>
        public string InstanceId;

        /// <summary>
        /// 卡牌等级
        /// </summary>
        public int Level;

        /// <summary>
        /// 卡牌强化次数
        /// </summary>
        public int UpgradeCount;

        /// <summary>
        /// 扩展数据（预留，用于存储额外的自定义属性）
        /// 例如：特殊效果计数、临时增益等
        /// </summary>
        public int ExtensionData;

        /// <summary>
        /// 创建默认卡牌状态
        /// </summary>
        /// <param name="cardId">卡牌ID</param>
        /// <returns>默认初始状态的卡牌</returns>
        public static CardRuntimeState CreateDefault(string cardId)
        {
            return new CardRuntimeState
            {
                CardId = cardId,
                InstanceId = System.Guid.NewGuid().ToString(),
                Level = 1,
                UpgradeCount = 0,
                ExtensionData = 0
            };
        }

        /// <summary>
        /// 创建卡牌状态的副本
        /// </summary>
        /// <returns>卡牌状态的深拷贝</returns>
        public CardRuntimeState Clone()
        {
            return new CardRuntimeState
            {
                CardId = this.CardId,
                InstanceId = this.InstanceId, // 保持相同的实例 ID
                Level = this.Level,
                UpgradeCount = this.UpgradeCount,
                ExtensionData = this.ExtensionData
            };
        }
    }
}

