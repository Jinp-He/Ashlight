using System;
using System.Collections.Generic;
using Ashlight.State.Runtime;
using UnityEngine;

namespace Ashlight.Systems.Map
{
    [Serializable]
    public sealed class MapTileContentWeightRow
    {
        public string Shape;
        public int EmptyRoadWeight;
        public int BattleWeight;
        public int EventWeight;
        public int EliteWeight;
        public string BattleEncounterId;
        public string EliteEncounterId;

        public int TotalWeight => EmptyRoadWeight + BattleWeight + EventWeight + EliteWeight;
    }

    [Serializable]
    internal sealed class MapTileContentWeightFile
    {
        public MapTileContentWeightRow[] Rows;
    }

    /// <summary>Runtime view of DataTables/Datas/Map/#MapTileContentWeight.xlsx.</summary>
    public sealed class MapTileContentWeightTable
    {
        private const string ResourcePath = "Config/map_tile_content_weights";
        private readonly Dictionary<MapTileShape, MapTileContentWeightRow> _rows;

        private MapTileContentWeightTable(Dictionary<MapTileShape, MapTileContentWeightRow> rows)
        {
            _rows = rows;
        }

        public static MapTileContentWeightTable LoadFromResources()
        {
            TextAsset asset = Resources.Load<TextAsset>(ResourcePath);
            if (asset == null) throw new InvalidOperationException($"Missing map tile content configuration at Resources/{ResourcePath}.json.");

            MapTileContentWeightFile file = JsonUtility.FromJson<MapTileContentWeightFile>(asset.text);
            if (file?.Rows == null) throw new InvalidOperationException("Map tile content configuration has no Rows array.");

            var rows = new Dictionary<MapTileShape, MapTileContentWeightRow>();
            foreach (MapTileContentWeightRow row in file.Rows)
            {
                if (row == null || !Enum.TryParse(row.Shape, true, out MapTileShape shape))
                    throw new InvalidOperationException("Map tile content configuration contains an invalid Shape.");
                if (row.EmptyRoadWeight < 0 || row.BattleWeight < 0 || row.EventWeight < 0 || row.EliteWeight < 0 || row.TotalWeight <= 0)
                    throw new InvalidOperationException($"Map tile content weights for {shape} must be non-negative and total above zero.");
                if (row.BattleWeight > 0 && string.IsNullOrWhiteSpace(row.BattleEncounterId))
                    throw new InvalidOperationException($"Map tile content configuration requires BattleEncounterId for {shape}.");
                if (row.EliteWeight > 0 && string.IsNullOrWhiteSpace(row.EliteEncounterId))
                    throw new InvalidOperationException($"Map tile content configuration requires EliteEncounterId for {shape}.");
                rows.Add(shape, row);
            }

            foreach (MapTileShape shape in (MapTileShape[])Enum.GetValues(typeof(MapTileShape)))
            {
                if (!rows.ContainsKey(shape)) throw new InvalidOperationException($"Map tile content configuration is missing {shape}.");
            }
            return new MapTileContentWeightTable(rows);
        }

        public MapTileContentWeightRow Get(MapTileShape shape) => _rows[shape];
    }
}
