using System;
using System.Collections.Generic;

namespace Ashlight.State.Runtime
{
    [Flags]
    public enum MapDirection
    {
        None = 0,
        North = 1 << 0,
        East = 1 << 1,
        South = 1 << 2,
        West = 1 << 3
    }

    public enum MapTileContent
    {
        EmptyRoad,
        Battle,
        Elite,
        Event,
        Reward,
        Shop,
        Rest
    }

    public enum MapTileShape
    {
        End,
        Cross,
        TShaped,
        Straight,
        Turn
    }

    public enum MapRunStage
    {
        ExploringBeforeRuins,
        AwaitingTileEncounter,
        AwaitingAncientRuinsBattle,
        ExploringAfterRuins,
        AwaitingFinalBoss,
        AwaitingChaseBoss,
        Completed,
        Failed
    }

    public enum MapBattleKind
    {
        TileEncounter,
        AncientRuins,
        FinalBoss,
        ChaseBoss
    }

    public enum MapPublicLocationType
    {
        Shop,
        Rest
    }

    public enum MapRegionId
    {
        MageTower,
        SirenTown
    }

    [Serializable]
    public struct MapGridPosition : IEquatable<MapGridPosition>
    {
        public int Column;
        public int Row;

        public MapGridPosition(int column, int row)
        {
            Column = column;
            Row = row;
        }

        public bool Equals(MapGridPosition other) => Column == other.Column && Row == other.Row;
        public override bool Equals(object obj) => obj is MapGridPosition other && Equals(other);
        public override int GetHashCode() => (Column * 397) ^ Row;
        public override string ToString() => $"({Column}, {Row})";
    }

    [Serializable]
    public struct MapGridSize
    {
        public int Width;
        public int Height;

        public MapGridSize(int width, int height)
        {
            Width = width;
            Height = height;
        }
    }

    [Serializable]
    public class MapTileDefinition
    {
        public string Id;
        public MapTileShape Shape;
        public MapDirection Connections;
        public MapTileContent Content;
        public string EncounterId;
    }

    [Serializable]
    public class MapPlacedTileState
    {
        public string TileId;
        public MapTileShape Shape;
        public MapGridPosition Position;
        /// <summary>Placed orientation, clockwise from the tile's default artwork direction.</summary>
        public int ClockwiseQuarterTurns;
        public MapDirection Connections;
        public MapTileContent Content;
        public string EncounterId;
    }

    [Serializable]
    public class MapPublicLocationState
    {
        public MapPublicLocationType Type;
        public MapGridPosition Position;
        public bool Resolved;
    }

    [Serializable]
    public class MapRegionExplorationState
    {
        public MapRegionId RegionId;
        public MapGridPosition Position;
        public MapGridSize Size;
        public List<MapGridPosition> ExploredCells = new List<MapGridPosition>();
        public bool BossSpawned;
        public MapGridPosition BossPosition;
    }

    /// <summary>
    /// 一局地图的可存档运行时状态。UI 不直接修改本对象，必须经 MapSystem 发出命令。
    /// </summary>
    [Serializable]
    public class MapRuntimeState
    {
        public int Width;
        public int Height;
        public MapGridPosition StartPosition;
        public MapGridSize StartSize;
        public MapGridPosition AncientRuinsPosition;
        public MapGridSize AncientRuinsSize;
        public MapGridPosition FinalPosition;
        public MapGridSize FinalSize;
        public MapGridPosition MageTowerPosition;
        public MapGridSize MageTowerSize;
        public MapGridPosition SirenTownPosition;
        public MapGridSize SirenTownSize;
        public int RemainingTileBudget;
        public bool AncientRuinsCompleted;
        public MapRunStage Stage;
        public MapRunStage StageBeforeTileEncounter;
        public List<MapPlacedTileState> PlacedTiles = new List<MapPlacedTileState>();
        public List<MapPublicLocationState> PublicLocations = new List<MapPublicLocationState>();
        public List<MapRegionExplorationState> RegionExplorations = new List<MapRegionExplorationState>();
        public List<MapTileDefinition> TileHand = new List<MapTileDefinition>();
        public List<MapTileDefinition> TileDrawPile = new List<MapTileDefinition>();
    }
}
