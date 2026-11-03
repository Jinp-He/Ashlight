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
        [Tooltip("坎特伯雷左下角坐标。")]
        public MapGridPosition StartPosition;
        public MapGridSize StartSize = new MapGridSize(2, 2);
        [Tooltip("古国遗迹左下角坐标。")]
        public MapGridPosition AncientRuinsPosition;
        public MapGridSize AncientRuinsSize = new MapGridSize(4, 2);
        [Tooltip("灰雾深处左下角坐标。")]
        public MapGridPosition FinalPosition;
        public MapGridSize FinalSize = new MapGridSize(3, 3);
        [Tooltip("法师塔左下角坐标。")]
        public MapGridPosition MageTowerPosition;
        public MapGridSize MageTowerSize = new MapGridSize(3, 3);
        [Tooltip("塞壬镇左下角坐标。")]
        public MapGridPosition SirenTownPosition;
        public MapGridSize SirenTownSize = new MapGridSize(3, 3);

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

        public bool IsFootprintInside(MapGridPosition bottomLeft, MapGridSize size)
        {
            return size.Width > 0 && size.Height > 0 &&
                   IsInside(bottomLeft) &&
                   IsInside(new MapGridPosition(bottomLeft.Column + size.Width - 1, bottomLeft.Row + size.Height - 1));
        }
    }
}
