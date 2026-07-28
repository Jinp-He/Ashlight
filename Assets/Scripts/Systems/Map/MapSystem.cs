using System;
using System.Collections.Generic;
using Ashlight.Common.Events;
using Ashlight.State.Runtime;

namespace Ashlight.Systems.Map
{
    /// <summary>
    /// 地图规则层。负责拼图合法性、有限拼图池、固定检查点与黑雾追击；不依赖任何 UI 或场景。
    /// </summary>
    public sealed class MapSystem
    {
        private MapRunDefinition _definition;
        private MapTileDealer _tileDealer;

        public MapRuntimeState State { get; private set; }

        public void StartRun(MapRunDefinition definition)
        {
            if (definition == null) throw new ArgumentNullException(nameof(definition));
            if (definition.Width <= 0 || definition.Height <= 0)
                throw new ArgumentException("地图宽高必须大于零。", nameof(definition));
            if (!definition.IsFootprintInside(definition.StartPosition) ||
                !definition.IsFootprintInside(definition.AncientRuinsPosition) ||
                !definition.IsFootprintInside(definition.FinalPosition))
                throw new ArgumentException("固定建筑的完整 2×2 占格必须位于地图范围内。", nameof(definition));

            _definition = definition;
            _tileDealer = new MapTileDealer();
            State = new MapRuntimeState
            {
                Width = definition.Width,
                Height = definition.Height,
                StartPosition = definition.StartPosition,
                AncientRuinsPosition = definition.AncientRuinsPosition,
                FinalPosition = definition.FinalPosition,
                Stage = MapRunStage.ExploringBeforeRuins
            };

            for (int i = 0; i < definition.TileBudget; i++)
                State.TileDrawPile.Add(_tileDealer.Draw());

            State.RemainingTileBudget = State.TileDrawPile.Count;

            DrawToHand(definition.InitialHandSize);
            PublishStateChanged();
        }

        public bool TryPlaceTile(string tileId, MapGridPosition position, int clockwiseQuarterTurns, out string failureReason)
        {
            failureReason = null;
            if (State == null)
            {
                failureReason = "地图尚未开始。";
                return false;
            }
            if (!IsExploring())
            {
                failureReason = "当前正在结算遭遇或本局已结束。";
                return false;
            }

            MapTileDefinition tile = FindTileInHand(tileId);
            if (tile == null)
            {
                failureReason = "所选拼图不在当前手牌中。";
                return false;
            }
            if (!CanPlaceAt(position, out failureReason)) return false;

            MapDirection rotatedConnections = Rotate(tile.Connections, clockwiseQuarterTurns);
            if (rotatedConnections == MapDirection.None)
            {
                failureReason = "拼图必须至少带有一个道路接口。";
                return false;
            }
            if (!HasConnectionToReachableRoad(position, rotatedConnections))
            {
                failureReason = "拼图必须连接到当前可达道路。";
                return false;
            }

            var placed = new MapPlacedTileState
            {
                TileId = tile.Id,
                Shape = tile.Shape,
                Position = position,
                ClockwiseQuarterTurns = NormalizeQuarterTurns(clockwiseQuarterTurns),
                Connections = rotatedConnections,
                Content = tile.Content,
                EncounterId = tile.EncounterId
            };
            State.PlacedTiles.Add(placed);
            State.TileHand.Remove(tile);
            State.RemainingTileBudget--;
            DrawToHand(1);

            GameEvent.Publish(new MapTilePlacedEvent { Tile = placed });

            if (TryReachFixedLocation(placed))
            {
                PublishStateChanged();
                return true;
            }

            if (TryRequestTileEncounter(placed))
            {
                PublishStateChanged();
                return true;
            }

            GameEvent.Publish(new MapContentResolvedEvent { Content = placed.Content, Position = placed.Position });
            TriggerChaseBossIfBudgetExhausted();
            PublishStateChanged();
            return true;
        }

        /// <summary>由战斗流程在 BattleEndedEvent 后调用，推进地图状态。</summary>
        public void ResolvePendingBattle(bool isPlayerVictory)
        {
            if (State == null || !IsAwaitingBattle()) return;

            MapRunStage previousStage = State.Stage;
            if (!isPlayerVictory)
            {
                State.Stage = MapRunStage.Failed;
                PublishStateChanged();
                return;
            }

            switch (previousStage)
            {
                case MapRunStage.AwaitingTileEncounter:
                    State.Stage = State.StageBeforeTileEncounter;
                    TriggerChaseBossIfBudgetExhausted();
                    break;
                case MapRunStage.AwaitingAncientRuinsBattle:
                    State.AncientRuinsCompleted = true;
                    State.Stage = MapRunStage.ExploringAfterRuins;
                    TriggerChaseBossIfBudgetExhausted();
                    break;
                case MapRunStage.AwaitingFinalBoss:
                case MapRunStage.AwaitingChaseBoss:
                    State.Stage = MapRunStage.Completed;
                    break;
            }
            PublishStateChanged();
        }

        private bool CanPlaceAt(MapGridPosition position, out string failureReason)
        {
            failureReason = null;
            if (!_definition.IsInside(position))
            {
                failureReason = "目标格超出地图范围。";
                return false;
            }
            if (IsFixedLocation(position))
            {
                failureReason = "固定地点不能被拼图覆盖。";
                return false;
            }
            if (FindPlacedTile(position) != null)
            {
                failureReason = "目标格已有拼图。";
                return false;
            }
            if (!State.AncientRuinsCompleted && ContainsPosition(_definition.BlockedBeforeRuins, position))
            {
                failureReason = "必须先完成古国遗迹，才能进入该区域。";
                return false;
            }
            return true;
        }

        private bool TryReachFixedLocation(MapPlacedTileState placed)
        {
            if (!State.AncientRuinsCompleted && IsConnectedTo(placed, State.AncientRuinsPosition))
            {
                State.Stage = MapRunStage.AwaitingAncientRuinsBattle;
                RequestBattle(MapBattleKind.AncientRuins, _definition.AncientRuinsEncounterId);
                return true;
            }

            if (State.AncientRuinsCompleted && IsConnectedTo(placed, State.FinalPosition))
            {
                State.Stage = MapRunStage.AwaitingFinalBoss;
                RequestBattle(MapBattleKind.FinalBoss, _definition.FinalBossEncounterId);
                return true;
            }
            return false;
        }

        private bool TryRequestTileEncounter(MapPlacedTileState placed)
        {
            if (placed.Content != MapTileContent.Battle || string.IsNullOrEmpty(placed.EncounterId)) return false;

            State.StageBeforeTileEncounter = State.Stage;
            State.Stage = MapRunStage.AwaitingTileEncounter;
            RequestBattle(MapBattleKind.TileEncounter, placed.EncounterId);
            return true;
        }

        private void TriggerChaseBossIfBudgetExhausted()
        {
            if (State.RemainingTileBudget > 0 || !IsExploring()) return;

            State.Stage = MapRunStage.AwaitingChaseBoss;
            RequestBattle(MapBattleKind.ChaseBoss, _definition.ChaseBossEncounterId);
        }

        private void RequestBattle(MapBattleKind kind, string encounterId)
        {
            GameEvent.Publish(new MapBattleRequestedEvent
            {
                Kind = kind,
                EncounterId = encounterId
            });
        }

        private bool HasConnectionToReachableRoad(MapGridPosition position, MapDirection connections)
        {
            foreach (MapDirection direction in CardinalDirections)
            {
                if (!HasDirection(connections, direction)) continue;
                MapGridPosition neighbor = Offset(position, direction);
                MapDirection opposite = Opposite(direction);

                if (IsInsideFixedLocation(neighbor, State.StartPosition) ||
                    (State.AncientRuinsCompleted && IsInsideFixedLocation(neighbor, State.AncientRuinsPosition)))
                {
                    return true;
                }

                MapPlacedTileState neighborTile = FindPlacedTile(neighbor);
                if (neighborTile != null && HasDirection(neighborTile.Connections, opposite)) return true;
            }
            return false;
        }

        private bool IsConnectedTo(MapPlacedTileState placed, MapGridPosition target)
        {
            foreach (MapDirection direction in CardinalDirections)
            {
                if (HasDirection(placed.Connections, direction) && IsInsideFixedLocation(Offset(placed.Position, direction), target))
                    return true;
            }
            return false;
        }

        private void DrawToHand(int count)
        {
            while (count > 0 && State.TileDrawPile.Count > 0)
            {
                MapTileDefinition tile = State.TileDrawPile[0];
                State.TileDrawPile.RemoveAt(0);
                State.TileHand.Add(tile);
                count--;
            }
        }

        private MapTileDefinition FindTileInHand(string tileId)
        {
            if (string.IsNullOrEmpty(tileId)) return null;
            foreach (MapTileDefinition tile in State.TileHand)
            {
                if (tile != null && tile.Id == tileId) return tile;
            }
            return null;
        }

        private MapPlacedTileState FindPlacedTile(MapGridPosition position)
        {
            foreach (MapPlacedTileState tile in State.PlacedTiles)
            {
                if (tile.Position.Equals(position)) return tile;
            }
            return null;
        }

        private bool IsFixedLocation(MapGridPosition position)
        {
            return IsInsideFixedLocation(position, State.StartPosition) ||
                   IsInsideFixedLocation(position, State.AncientRuinsPosition) ||
                   IsInsideFixedLocation(position, State.FinalPosition);
        }

        private static bool IsInsideFixedLocation(MapGridPosition position, MapGridPosition bottomLeft)
        {
            int size = MapRuntimeState.FixedLocationSize;
            return position.Column >= bottomLeft.Column && position.Column < bottomLeft.Column + size &&
                   position.Row >= bottomLeft.Row && position.Row < bottomLeft.Row + size;
        }

        private bool IsExploring()
        {
            return State.Stage == MapRunStage.ExploringBeforeRuins || State.Stage == MapRunStage.ExploringAfterRuins;
        }

        private bool IsAwaitingBattle()
        {
            return State.Stage == MapRunStage.AwaitingTileEncounter ||
                   State.Stage == MapRunStage.AwaitingAncientRuinsBattle ||
                   State.Stage == MapRunStage.AwaitingFinalBoss ||
                   State.Stage == MapRunStage.AwaitingChaseBoss;
        }

        private void PublishStateChanged()
        {
            GameEvent.Publish(new MapRunStateChangedEvent { State = State });
        }

        private static MapTileDefinition CloneTile(MapTileDefinition source)
        {
            return new MapTileDefinition
            {
                Id = source.Id,
                Shape = source.Shape,
                Connections = source.Connections,
                Content = source.Content,
                EncounterId = source.EncounterId
            };
        }

        private static readonly MapDirection[] CardinalDirections =
        {
            MapDirection.North, MapDirection.East, MapDirection.South, MapDirection.West
        };

        private static bool ContainsPosition(List<MapGridPosition> positions, MapGridPosition target)
        {
            if (positions == null) return false;
            foreach (MapGridPosition position in positions)
            {
                if (position.Equals(target)) return true;
            }
            return false;
        }

        private static bool HasDirection(MapDirection directions, MapDirection direction) => (directions & direction) != 0;

        private static MapDirection Rotate(MapDirection directions, int clockwiseQuarterTurns)
        {
            int turns = NormalizeQuarterTurns(clockwiseQuarterTurns);
            for (int i = 0; i < turns; i++)
            {
                MapDirection rotated = MapDirection.None;
                if (HasDirection(directions, MapDirection.North)) rotated |= MapDirection.East;
                if (HasDirection(directions, MapDirection.East)) rotated |= MapDirection.South;
                if (HasDirection(directions, MapDirection.South)) rotated |= MapDirection.West;
                if (HasDirection(directions, MapDirection.West)) rotated |= MapDirection.North;
                directions = rotated;
            }
            return directions;
        }

        private static int NormalizeQuarterTurns(int clockwiseQuarterTurns)
        {
            int turns = clockwiseQuarterTurns % 4;
            return turns < 0 ? turns + 4 : turns;
        }

        private static MapDirection Opposite(MapDirection direction)
        {
            switch (direction)
            {
                case MapDirection.North: return MapDirection.South;
                case MapDirection.East: return MapDirection.West;
                case MapDirection.South: return MapDirection.North;
                case MapDirection.West: return MapDirection.East;
                default: return MapDirection.None;
            }
        }

        private static MapGridPosition Offset(MapGridPosition position, MapDirection direction)
        {
            switch (direction)
            {
                case MapDirection.North: return new MapGridPosition(position.Column, position.Row + 1);
                case MapDirection.East: return new MapGridPosition(position.Column + 1, position.Row);
                case MapDirection.South: return new MapGridPosition(position.Column, position.Row - 1);
                case MapDirection.West: return new MapGridPosition(position.Column - 1, position.Row);
                default: return position;
            }
        }
    }
}
