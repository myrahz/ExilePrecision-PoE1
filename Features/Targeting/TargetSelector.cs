using System;
using System.Linq;
using System.Numerics;
using ExileCore;
using ExileCore.PoEMemory.MemoryObjects;
using ExilePrecision.Core.Events;
using ExilePrecision.Core.Events.Events;
using ExilePrecision.Features.Targeting.EntityInformation;
using ExilePrecision.Features.Targeting.Priority;
using ExilePrecision.Features.Targeting.Density;
using ExilePrecision.Utils;
using ExilePrecision.Settings;

namespace ExilePrecision.Features.Targeting
{
    public class TargetSelector
    {
        private readonly GameController _gameController;
        private readonly EntityScanner _entityScanner;
        private readonly PriorityCalculator _priorityCalculator;
        private readonly DensityAnalyzer _densityAnalyzer;
        private readonly LineOfSight _lineOfSight;

        private Entity _currentTarget;
        private DateTime _lastSelectionTime;
        private float _targetSwitchCooldown = 0.5f;
        private float _minWeightDifferenceForSwitch = 0.5f;
        private float _maxTargetDistance = 100f;

        private float _baseClusterBonus = 0.1f;
        private float _maxClusterBonus = 2.0f;

        public TargetSelector(
            GameController gameController,
            EntityScanner entityScanner,
            PriorityCalculator priorityCalculator,
            LineOfSight lineOfSight)
        {
            _gameController = gameController;
            _entityScanner = entityScanner;
            _priorityCalculator = priorityCalculator;
            _densityAnalyzer = new DensityAnalyzer(gameController, lineOfSight);
            _lineOfSight = lineOfSight;
        }

        public void Configure()
        {
            _targetSwitchCooldown = ExilePrecision.Instance.Settings.Targeting.TargetSwitchThreshold;
            _minWeightDifferenceForSwitch = ExilePrecision.Instance.Settings.Targeting.TargetSwitchThreshold;
            _maxTargetDistance = ExilePrecision.Instance.Settings.Targeting.MaxTargetRange;

            var densitySettings = ExilePrecision.Instance.Settings.Targeting.Density;
            _baseClusterBonus = densitySettings.BaseClusterBonus;
            _maxClusterBonus = densitySettings.MaxClusterBonus;

            _entityScanner.SetScanRange(ExilePrecision.Instance.Settings.Targeting.ScanRadius);
            _densityAnalyzer.Configure(
                densitySettings.ClusterRadius,
                densitySettings.MinClusterSize,
                ExilePrecision.Instance.Settings.Targeting.LineOfSight.RequireLineOfSight
            );

            var priorities = ExilePrecision.Instance.Settings.Targeting.Priorities;
            _priorityCalculator.Configure(
                distanceWeight: priorities.DistanceWeight,
                healthWeight: priorities.Health.HealthWeight,
                rarityWeight: priorities.Rarity.ConsiderRarity ? 1.0f : 0f,
                maxTargetDistance: ExilePrecision.Instance.Settings.Targeting.MaxTargetRange,
                preferHigherHealth: priorities.Health.PreferHigherHealth
            );
        }

        public void Update()
        {
            if (_gameController?.Player == null) return;

            try
            {
                var playerPos = _gameController.Player.GridPosNum;
                _entityScanner.Scan();

                var entities = _entityScanner.GetEntitiesInRange(_maxTargetDistance);
                _priorityCalculator.UpdatePriorities(entities);
                _densityAnalyzer.Update(entities);

                UpdateTargetSelection(playerPos);
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[TargetSelector] Error during update: {ex.Message}");
            }
        }

        private void UpdateTargetSelection(Vector2 playerPos)
        {
            var currentTime = DateTime.UtcNow;
            var timeSinceLastSelection = (currentTime - _lastSelectionTime).TotalSeconds;

            if (_currentTarget != null)
            {
                if (!IsTargetValid(_currentTarget))
                {
                    HandleTargetLost();
                    _currentTarget = null;
                }
                else
                {
                    var distance = Vector2.Distance(playerPos, _currentTarget.GridPosNum);
                    if (distance > _maxTargetDistance)
                    {
                        EventBus.Instance.Publish(new TargetOutOfRangeEvent(
                            _currentTarget,
                            distance,
                            _maxTargetDistance));
                        _currentTarget = null;
                    }
                }
            }

            if (_currentTarget == null || timeSinceLastSelection >= _targetSwitchCooldown)
            {
                SelectNewTarget(playerPos);
            }
        }

        private void SelectNewTarget(Vector2 playerPos)
        {
            var entities = _entityScanner.GetEntitiesInRange(_maxTargetDistance).ToList();
            if (!entities.Any())
            {
                if (_currentTarget != null)
                {
                    HandleTargetLost();
                }
                return;
            }

            var bestTarget = FindBestTarget(entities, playerPos);
            if (bestTarget == null) return;

            var shouldSwitch = ShouldSwitchTarget(bestTarget);
            if (shouldSwitch)
            {
                var oldTarget = _currentTarget;
                _currentTarget = bestTarget;
                _lastSelectionTime = DateTime.UtcNow;

                var weight = CalculateFinalWeight(bestTarget);
                EventBus.Instance.Publish(new TargetAcquiredEvent(
                    bestTarget,
                    Vector2.Distance(playerPos, bestTarget.GridPosNum),
                    bestTarget.GridPosNum,
                    weight));

                if (oldTarget != null)
                {
                    EventBus.Instance.Publish(new TargetLostEvent(
                        oldTarget,
                        "Switched to higher priority target",
                        oldTarget.GridPosNum));
                }
            }
        }

        private Entity FindBestTarget(System.Collections.Generic.IEnumerable<Entity> entities, Vector2 playerPos)
        {
            Entity bestTarget = null;
            float bestWeight = float.MinValue;

            foreach (var entity in entities)
            {
                if (!IsTargetValid(entity)) continue;

                var weight = CalculateFinalWeight(entity);
                if (weight > bestWeight)
                {
                    bestWeight = weight;
                    bestTarget = entity;
                }
            }

            return bestTarget;
        }

        private float CalculateFinalWeight(Entity entity)
        {
            var baseWeight = _priorityCalculator.GetEntityWeight(entity);
            if (!baseWeight.HasValue) return float.MinValue;

            var finalWeight = baseWeight.Value;

            if (ExilePrecision.Instance.Settings.Targeting.Density.EnableClustering)
            {
                var density = _densityAnalyzer.GetDensityAtPosition(entity.GridPosNum);
                var densitySettings = ExilePrecision.Instance.Settings.Targeting.Density;

                if (density != null)
                {
                    var densityBonus = CalculateDensityBonus(density);
                    finalWeight *= (1 + densityBonus);

                    if (densitySettings.EnableCoreBonus)
                    {
                        var distanceFromCenter = Vector2.Distance(density.Center, entity.GridPosNum);
                        if (distanceFromCenter <= density.Radius * densitySettings.CoreRadiusPercent)
                        {
                            finalWeight *= densitySettings.CoreBonusMultiplier;
                        }
                    }
                }
                else if (densitySettings.EnableIsolationPenalty && !IsEntityInAnyClusters(entity))
                {
                    finalWeight *= densitySettings.IsolationPenaltyMultiplier;
                }
            }

            return finalWeight;
        }

        private bool IsEntityInAnyClusters(Entity entity)
        {
            foreach (var cluster in _densityAnalyzer.GetAllDensities())
            {
                if (cluster.ContainsEntity(entity))
                {
                    return true;
                }
            }
            return false;
        }

        private float CalculateDensityBonus(DensityInfo density)
        {
            if (density == null || density.Entities.Count < 3) return 0f;

            var normalizedScore = Math.Min(density.DensityScore, 1.0f);
            var bonus = _baseClusterBonus + (normalizedScore * (_maxClusterBonus - _baseClusterBonus));

            return Math.Min(bonus, _maxClusterBonus);
        }

        private bool ShouldSwitchTarget(Entity newTarget)
        {
            if (_currentTarget == null) return true;
            if (_currentTarget == newTarget) return false;

            var currentWeight = CalculateFinalWeight(_currentTarget);
            var newWeight = CalculateFinalWeight(newTarget);

            return newWeight > currentWeight + _minWeightDifferenceForSwitch;
        }

        private void HandleTargetLost()
        {
            if (_currentTarget == null) return;

            EventBus.Instance.Publish(new TargetLostEvent(
                _currentTarget,
                "Target no longer valid",
                _currentTarget.GridPosNum));

            _currentTarget = null;
        }

        private bool IsTargetValid(Entity entity)
        {
            if (entity == null) return false;
            if (!entity.IsValid) return false;
            if (!entity.IsAlive) return false;
            if (entity.IsDead) return false;
            if (!entity.IsTargetable) return false;
            if (entity.IsHidden) return false;

            try
            {
                var playerPos = _gameController.Player.GridPosNum;
                var targetPos = entity.GridPosNum;

                return _lineOfSight.HasLineOfSight(playerPos, targetPos);
            }
            catch
            {
                return false;
            }
        }

        public Entity GetCurrentTarget()
        {
            return _currentTarget;
        }

        public void Clear()
        {
            var oldTarget = _currentTarget;
            _currentTarget = null;
            _lastSelectionTime = DateTime.MinValue;

            if (oldTarget != null)
            {
                EventBus.Instance.Publish(new TargetLostEvent(
                    oldTarget,
                    "Target selector cleared",
                    oldTarget.GridPosNum));
            }

            _entityScanner.Clear();
            _priorityCalculator.Clear();
            _densityAnalyzer.Clear();
            _lineOfSight.Clear();
        }
    }
}