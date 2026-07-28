using System;
using System.Collections.Generic;
using Ashlight.State.Runtime;
using UnityEngine;

namespace Ashlight.Systems.Map
{
    /// <summary>
    /// 地图 Demo 的静态定义。数值、坐标、牌池和遭遇 ID 都由 Inspector 配置，不硬编码在规则层。
    /// </summary>
    [CreateAssetMenu(fileName = "MapRunDefinition", menuName = "Ashlight/Map Run Definition")]
    public class MapRunDefinition : ScriptableObject
    {
        [Header("Grid")]
        public int Width;
        public int Height;
        [Tooltip("固定建筑左下角坐标；每个固定建筑统一占据 2×2 格。")]
        public MapGridPosition StartPosition;
        [Tooltip("固定建筑左下角坐标；每个固定建筑统一占据 2×2 格。")]
        public MapGridPosition AncientRuinsPosition;
        [Tooltip("固定建筑左下角坐标；每个固定建筑统一占据 2×2 格。")]
        public MapGridPosition FinalPosition;

        [Header("Progression")]
        [Tooltip("初始展示在 MapPanel 中、可被放置的拼图数量。")]
        public int InitialHandSize;
        [Tooltip("古国遗迹完成前禁止放置的区域；可留空。")]
        public List<MapGridPosition> BlockedBeforeRuins = new List<MapGridPosition>();

        [Header("Battles")]
        public string AncientRuinsEncounterId;
        public string FinalBossEncounterId;
        public string ChaseBossEncounterId;

        [Header("Finite tile budget")]
        [Tooltip("本局可放置的拼图总数；形状由运行时从 End / Cross / T-shaped / Straight / Turn 随机抽取。")]
        public int TileBudget;

        public bool IsInside(MapGridPosition position)
        {
            return position.Column >= 0 && position.Column < Width &&
                   position.Row >= 0 && position.Row < Height;
        }

        public bool IsFootprintInside(MapGridPosition bottomLeft)
        {
            int size = MapRuntimeState.FixedLocationSize;
            return IsInside(bottomLeft) && IsInside(new MapGridPosition(bottomLeft.Column + size - 1, bottomLeft.Row + size - 1));
        }
    }
}
