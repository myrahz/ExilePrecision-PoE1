﻿using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using System.Threading.Tasks;
using ExileCore;
using ExileCore.Shared.Enums;
using ExileCore.Shared.Helpers;
using ExilePrecision.Core.Events;
using ExilePrecision.Core.Events.Events;
using Graphics = ExileCore.Graphics;

namespace ExilePrecision.Utils
{
    public enum LineOfSightDataType
    {
        Terrain,
        Walkable
    }

    public class LineOfSight
    {
        private readonly GameController _gameController;
        private int[][] _terrainData;
        private int[][] _walkableData;
        private Vector2 _areaDimensions;
        private const int TARGET_LAYER_VALUE = 4;

        private readonly List<(Vector2 Pos, int Value)> _debugPoints = new();
        private readonly List<(Vector2 Start, Vector2 End, bool IsVisible)> _debugRays = new();
        private readonly HashSet<Vector2> _debugVisiblePoints = new();
        private float _lastObserverZ;
        private Vector2 _lastDebugGridCenter;

        public LineOfSight(GameController gameController)
        {
            _gameController = gameController;

            var eventBus = EventBus.Instance;
            eventBus.Subscribe<AreaChangeEvent>(HandleAreaChange);
            eventBus.Subscribe<RenderEvent>(HandleRender);
        }

        private void HandleRender(RenderEvent evt)
        {
            if (!ExilePrecision.Instance.Settings.Render.EnableRendering) return;
            if (!ExilePrecision.Instance.Settings.Render.ShowTerrainDebug && !ExilePrecision.Instance.Settings.Render.ShowWalkableDebug) return;

            if (_terrainData == null)
            {
                return;
            }

            var playerGridPos = _gameController.Player.GridPosNum;
            if (Vector2.DistanceSquared(playerGridPos, _lastDebugGridCenter) >= 20) // dont update too frequently
            {
                var losType = ExilePrecision.Instance.Settings.Render.ShowWalkableDebug ? LineOfSightDataType.Walkable : LineOfSightDataType.Terrain;
                UpdateDebugGrid(playerGridPos, losType);
            }

            foreach (var (pos, value) in _debugPoints)
            {
                var worldPos = new Vector3(pos.GridToWorld(), _lastObserverZ);
                var screenPos = _gameController.IngameState.Camera.WorldToScreen(worldPos);

                SharpDX.Color color; // Change type to SharpDX.Color
                if (_debugVisiblePoints.Contains(pos))
                {
                    color = SharpDX.Color.Yellow; // Use SharpDX.Color
                }
                else
                {
                    color = value switch
                    {
                        0 => new SharpDX.Color(0, 128, 0, 128),    // Walkable
                        1 => new SharpDX.Color(0, 0, 128, 128),    // Low obstacle
                        2 => new SharpDX.Color(255, 165, 0, 128),  // Medium obstacle
                        3 => new SharpDX.Color(255, 0, 0, 128),    // High obstacle
                        4 => new SharpDX.Color(128, 0, 128, 128),  // Blocking
                        5 => new SharpDX.Color(0, 0, 0, 128),      // Special
                        _ => new SharpDX.Color(128, 128, 128, 128) // Unknown
                    };
                }

                evt.Graphics.DrawText(
                    value.ToString(),
                    screenPos,
                    color,
                    FontAlign.Center
                );
            }
        }
        
        private void HandleAreaChange(AreaChangeEvent evt)
        {
            _areaDimensions = _gameController.IngameState.Data.AreaDimensions;
            var rawTargetingData = _gameController.IngameState.Data.RawTerrainTargetingData;
            var rawWalkableData = _gameController.IngameState.Data.RawPathfindingData;

            _terrainData = new int[rawTargetingData.Length][];
            Parallel.For(0, rawTargetingData.Length, y =>
            {
                _terrainData[y] = new int[rawTargetingData[y].Length];
                Array.Copy(rawTargetingData[y], _terrainData[y], rawTargetingData[y].Length);
            });

            _walkableData = new int[rawWalkableData.Length][];
            Parallel.For(0, rawWalkableData.Length, y =>
            {
                _walkableData[y] = new int[rawWalkableData[y].Length];
                Array.Copy(rawWalkableData[y], _walkableData[y], rawWalkableData[y].Length);
            });

            UpdateDebugGrid(_gameController.Player.GridPosNum, LineOfSightDataType.Terrain);
        }

        private void UpdateDebugGrid(Vector2 center, LineOfSightDataType losType)
        {
            _debugPoints.Clear();
            _lastDebugGridCenter = center;
            const int size = 200;

            for (var y = -size; y <= size; y++)
                for (var x = -size; x <= size; x++)
                {
                    if (x * x + y * y > size * size) continue;

                    var pos = new Vector2(center.X + x, center.Y + y);
                    var value = GetValue(pos, losType);
                    if (value >= 0) _debugPoints.Add((pos, value));
                }

            _lastObserverZ = _gameController.IngameState.Data.GetTerrainHeightAt(center);
        }

        public bool HasLineOfSight(Vector2 start, Vector2 end)
        {
            return HasLineOfSight(start, end, LineOfSightDataType.Terrain);
        }

        public bool HasLineOfSight(Vector2 start, Vector2 end, LineOfSightDataType losType)
        {
            if (_terrainData == null) return false;

            // Update debug visualization
            _debugVisiblePoints.Clear();
            UpdateDebugGrid(start, losType);

            var isVisible = HasLineOfSightInternal(start, end, losType);
            _debugRays.Add((start, end, isVisible));

            return isVisible;
        }

        private bool HasLineOfSightInternal(Vector2 start, Vector2 end, LineOfSightDataType losType)
        {
            var startX = (int)start.X;
            var startY = (int)start.Y;
            var endX = (int)end.X;
            var endY = (int)end.Y;

            if (!IsInBounds(startX, startY) || !IsInBounds(endX, endY))
                return false;

            var dx = Math.Abs(endX - startX);
            var dy = Math.Abs(endY - startY);

            if (dx == 0)
                return CheckVerticalLine(startX, startY, endY, losType);
            if (dy == 0)
                return CheckHorizontalLine(startY, startX, endX, losType);

            return CheckDiagonalLine(start, end, dx, dy, losType);
        }

        private bool CheckVerticalLine(int x, int startY, int endY, LineOfSightDataType losType)
        {
            var step = Math.Sign(endY - startY);
            var y = startY;

            while (y != endY)
            {
                y += step;
                var pos = new Vector2(x, y);
                var terrainValue = GetValue(pos, losType);
                _debugVisiblePoints.Add(pos);

                if (terrainValue < TARGET_LAYER_VALUE) continue;
                if (terrainValue <= TARGET_LAYER_VALUE) return false;
            }

            return true;
        }

        private bool CheckHorizontalLine(int y, int startX, int endX, LineOfSightDataType losType)
        {
            var step = Math.Sign(endX - startX);
            var x = startX;

            while (x != endX)
            {
                x += step;
                var pos = new Vector2(x, y);
                var terrainValue = GetValue(pos, losType);
                _debugVisiblePoints.Add(pos);

                if (terrainValue < TARGET_LAYER_VALUE) continue;
                if (terrainValue <= TARGET_LAYER_VALUE) return false;
            }

            return true;
        }

        private bool CheckDiagonalLine(Vector2 start, Vector2 end, float dx, float dy, LineOfSightDataType losType)
        {
            var x = (int)start.X;
            var y = (int)start.Y;
            var stepX = Math.Sign(end.X - start.X);
            var stepY = Math.Sign(end.Y - start.Y);

            if (dx >= dy)
            {
                var deltaError = dy / dx;
                var error = 0.0f;

                while (x != (int)end.X)
                {
                    x += stepX;
                    error += deltaError;

                    if (error >= 0.5f)
                    {
                        y += stepY;
                        error -= 1.0f;
                    }

                    var pos = new Vector2(x, y);
                    var terrainValue = GetValue(pos, losType);
                    _debugVisiblePoints.Add(pos);

                    if (terrainValue < TARGET_LAYER_VALUE) continue;
                    if (terrainValue <= TARGET_LAYER_VALUE) return false;
                }
            }
            else
            {
                var deltaError = dx / dy;
                var error = 0.0f;

                while (y != (int)end.Y)
                {
                    y += stepY;
                    error += deltaError;

                    if (error >= 0.5f)
                    {
                        x += stepX;
                        error -= 1.0f;
                    }

                    var pos = new Vector2(x, y);
                    var terrainValue = GetValue(pos, losType);
                    _debugVisiblePoints.Add(pos);

                    if (terrainValue < TARGET_LAYER_VALUE) continue;
                    if (terrainValue <= TARGET_LAYER_VALUE) return false;
                }
            }

            return true;
        }

        private bool IsInBounds(int x, int y)
        {
            return x >= 0 && x < _areaDimensions.X && y >= 0 && y < _areaDimensions.Y;
        }

        private int GetValue(Vector2 position, LineOfSightDataType losType)
        {
            var x = (int)position.X;
            var y = (int)position.Y;

            if (!IsInBounds(x, y)) return -1;

            return losType switch
            {
                LineOfSightDataType.Terrain => _terrainData[y][x],
                LineOfSightDataType.Walkable => _walkableData[y][x],
                _ => -1
            };
        }

        public static LineOfSightDataType Parse(string type)
        {
            return type switch
            {
                "Walkable" => LineOfSightDataType.Walkable,
                _ => LineOfSightDataType.Terrain
            };
        }

        public void Clear()
        {
            _terrainData = null;
            _walkableData = null;
            _debugPoints.Clear();
            _debugRays.Clear();
            _debugVisiblePoints.Clear();
        }
    }
}