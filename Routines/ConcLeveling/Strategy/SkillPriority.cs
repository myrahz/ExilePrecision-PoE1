using ExileCore;
using ExileCore.PoEMemory.Components;
using ExileCore.PoEMemory.MemoryObjects;
using ExileCore.Shared.Enums;
using ExilePrecision.Core.Combat.Skills;
using ExilePrecision.Features.Targeting.EntityInformation;
using System;
using System.Collections.Generic;
using System.Linq;

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

        public ActiveSkill GetNextSkill(
            EntityInfo target,
            IReadOnlyCollection<ActiveSkill> availableSkills,
            SkillMonitor skillMonitor)
        {
            var skills = availableSkills.Where(s => _trackedSkills.Contains(s.Name)).ToList();
            if (!skills.Any() || target == null)
                return null;

            if (target.Rarity is MonsterRarity.Unique or MonsterRarity.Rare)
                return DetermineEliteMonsterSkill(target, skills, skillMonitor);

            return DetermineNormalMonsterSkill(target, skills, skillMonitor);
        }

        private ActiveSkill DetermineEliteMonsterSkill(
            EntityInfo target,
            List<ActiveSkill> availableSkills,
            SkillMonitor skillMonitor)
        {

            var eConc = FindSkill(availableSkills, "ExplosiveConcoction");
            if (eConc != null && skillMonitor.CanUseSkill(eConc))
                return eConc;

            var pConc = FindSkill(availableSkills, "PoisonousConcoction");
            if (pConc != null && skillMonitor.CanUseSkill(pConc))
                return pConc;

            var spectral = FindSkill(availableSkills, "SpectralThrow");
            if (spectral != null && skillMonitor.CanUseSkill(spectral))
                return spectral;

            return null;
        }

        private ActiveSkill DetermineNormalMonsterSkill(
            EntityInfo target,
            List<ActiveSkill> availableSkills,
            SkillMonitor skillMonitor)
        {
            var eConc = FindSkill(availableSkills, "ExplosiveConcoction");
            if (eConc != null && skillMonitor.CanUseSkill(eConc))
                return eConc;

            var pConc = FindSkill(availableSkills, "PoisonousConcoction");
            if (pConc != null && skillMonitor.CanUseSkill(pConc))
                return pConc;

            var spectral = FindSkill(availableSkills, "SpectralThrow");
            if (spectral != null && skillMonitor.CanUseSkill(spectral))
                return spectral;

            return null;
        }

        private ActiveSkill FindSkill(List<ActiveSkill> skills, string skillName)
        {
            return skills.FirstOrDefault(x => x.Name == skillName);
        }
    }
}