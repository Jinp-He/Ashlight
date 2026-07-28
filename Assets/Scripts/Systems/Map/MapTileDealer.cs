using System;
using Ashlight.State.Runtime;

namespace Ashlight.Systems.Map
{
    /// <summary>
    /// Demo 拼图发牌器。形状不从 ScriptableObject 读取；每次运行随机从五种基础形状中抽取。
    /// 地块内容与权重会在后续独立接入，当前保持空白道路以验证铺路主循环。
    /// </summary>
    public sealed class MapTileDealer
    {
        // Low-frequency shapes keep routes readable; the three directional shapes form most draws.
        private const int EndWeight = 1;
        private const int CrossWeight = 1;
        private const int TShapedWeight = 3;
        private const int StraightWeight = 3;
        private const int TurnWeight = 3;

        private readonly Random _random = new Random();
        private int _nextTileSerial;

        public MapTileDefinition Draw()
        {
            MapTileShape shape = DrawShape();
            _nextTileSerial++;
            return new MapTileDefinition
            {
                Id = $"map_tile_{_nextTileSerial}",
                Shape = shape,
                Connections = GetDefaultConnections(shape),
                Content = MapTileContent.EmptyRoad
            };
        }

        private MapTileShape DrawShape()
        {
            int roll = _random.Next(EndWeight + CrossWeight + TShapedWeight + StraightWeight + TurnWeight);
            if ((roll -= EndWeight) < 0) return MapTileShape.End;
            if ((roll -= CrossWeight) < 0) return MapTileShape.Cross;
            if ((roll -= TShapedWeight) < 0) return MapTileShape.TShaped;
            if ((roll -= StraightWeight) < 0) return MapTileShape.Straight;
            return MapTileShape.Turn;
        }

        public static MapDirection GetDefaultConnections(MapTileShape shape)
        {
            switch (shape)
            {
                case MapTileShape.End:
                    return MapDirection.South;
                case MapTileShape.Cross:
                    return MapDirection.North | MapDirection.East | MapDirection.South | MapDirection.West;
                case MapTileShape.TShaped:
                    return MapDirection.North | MapDirection.South | MapDirection.West;
                case MapTileShape.Straight:
                    return MapDirection.East | MapDirection.West;
                case MapTileShape.Turn:
                    return MapDirection.South | MapDirection.East;
                default:
                    return MapDirection.None;
            }
        }
    }
}
