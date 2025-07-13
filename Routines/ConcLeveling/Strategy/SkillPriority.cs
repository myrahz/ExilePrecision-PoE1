using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExilePrecision.Core.Combat.Skills;
using ExilePrecision.Features.Targeting.EntityInformation;
using System;
using System.Collections.Generic;
using System.Linq;
using ExilePrecision.Features.Targeting;
using ExilePrecision.Features.Targeting.Priority;

namespace ExilePrecision.Routines.ConcLeveling.Strategy
{
    public class SkillPriority
    {
        private readonly GameController _gameController;
        private readonly HashSet<string> _trackedSkills = new()
        {
            "ExplosiveConcoction",
            "PoisonousConcoction",
            "SpectralThrow",
        };

        public SkillPriority(GameController gameController)
        {
            _gameController = gameController;
        }

        public (ActiveSkill skill, EntityInfo target) GetBestAction(
            IReadOnlyCollection<ActiveSkill> availableSkills,
            TargetSelector targetSelector,
            PriorityCalculator priorityCalculator,
            SkillMonitor skillMonitor)
        {
            (ActiveSkill skill, EntityInfo target) bestAction = (null, null);
            float maxWeight = float.MinValue;

            var usableSkills = availableSkills.Where(s => skillMonitor.CanUseSkill(s) && _trackedSkills.Contains(s.Name));

            foreach (var skill in usableSkills)
            {
                if (!skill.Enabled) continue;

                var validTargets = targetSelector.GetValidTargets(skill);
                if (!validTargets.Any()) continue;

                foreach (var target in validTargets)
                {
                    var weight = priorityCalculator.GetEntityWeight(target);
                    if (weight.HasValue && weight.Value > maxWeight)
                    {
                        maxWeight = weight.Value;
                        bestAction = (skill, new EntityInfo(target, _gameController));
                    }
                }
            }

            return bestAction;
        }
    }
}