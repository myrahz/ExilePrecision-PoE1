using ExileCore;
using ExileCore.Shared.Nodes;
using ExilePrecision.Features.Input;
using ExilePrecision.Settings;
using System;
using System.Collections.Generic;
using System.Linq;

namespace ExilePrecision.Core.Combat.Skills
{
    public class SkillHandler
    {
        private readonly GameController _gameController;
        private readonly KeyHandler _keyHandler;
        private readonly Dictionary<string, ActiveSkill> _skills = new();

        public SkillHandler(GameController gameController)
        {
            _gameController = gameController;
            _keyHandler = new KeyHandler();
        }

        public void Initialize()
        {
            try
            {
                var existingSkills = new Dictionary<string, ActiveSkill>();
                foreach (var skill in ExilePrecision.Instance.Settings.Combat.Skills.Content)
                {
                    existingSkills[skill.Name] = skill;
                }

                _skills.Clear();
                var skillBar = _gameController.IngameState.IngameUi.SkillBar;
                var shortcuts = _gameController.IngameState.ShortcutSettings.Shortcuts;

                for (var i = 0; i < Math.Min(8, skillBar.Skills.Count); i++)
                {
                    try
                    {
                        var skill = skillBar.Skills[i]?.Skill;
                        if (skill == null || skill.Address == 0)
                            continue;

                        var shortcut = shortcuts.FirstOrDefault(x => x.Usage.ToString() == $"Skill{i + 1}");
                        if (!Enum.TryParse(shortcut.MainKey.ToString(), out System.Windows.Forms.Keys key))
                            continue;

                        ActiveSkill activeSkill;
                        if (existingSkills.TryGetValue(skill.Name, out var existingSkill))
                        {
                            activeSkill = existingSkill;
                            activeSkill.Skill = skill;
                            activeSkill.Id = skill.Id;
                            activeSkill.InternalId = skill.Id2;

                        }
                        else
                        {
                            activeSkill = new ActiveSkill
                            {
                                Skill = skill,
                                Key = new HotkeyNode(key),
                                Name = skill.Name,
                                Id = skill.Id,
                                InternalId = skill.Id2,
                                Enabled = new ToggleNode(true),
                                UseClick = new ToggleNode(false),
                                ExtraDelay = new RangeNode<int>(0, 0, 5000)
                            };
                        }

                        _skills[skill.Name] = activeSkill;
                    }
                    catch (Exception ex)
                    {
                        DebugWindow.LogError($"[SkillHandler] Error initializing skill {i}: {ex.Message}");
                    }
                }

                ExilePrecision.Instance.Settings.Combat.Skills.Content.Clear();
                foreach (var skill in _skills.Values)
                {
                    ExilePrecision.Instance.Settings.Combat.Skills.Content.Add(skill);
                }
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[SkillHandler] Error during initialization: {ex.Message}");
                if (ex.InnerException != null)
                {
                    DebugWindow.LogError($"[SkillHandler] Inner exception: {ex.InnerException.Message}");
                }
            }
        }

        public bool UseSkill(string skillName, bool force = false)
        {
            if (!_skills.TryGetValue(skillName, out var skill))
                return false;

            if (!force && !skill.CanUse)
                return false;

            try
            {
                if (skill.UseClick)
                    _keyHandler.SinglePress(skill.Key);
                else
                    _keyHandler.Hold(skill.Key);

                return true;
            }
            catch (Exception ex)
            {
                DebugWindow.LogError($"[SkillHandler] Error using skill {skillName}: {ex.Message}");
                return false;
            }
        }

        public void ReleaseSkill(string skillName)
        {
            if (_skills.TryGetValue(skillName, out var skill))
            {
                _keyHandler.Release(skill.Key);
            }
        }

        public ActiveSkill GetSkill(string skillName)
        {
            return _skills.TryGetValue(skillName, out var skill) ? skill : null;
        }

        public void ReleaseAllSkills()
        {
            _keyHandler.ReleaseAll();
        }

        public IReadOnlyCollection<ActiveSkill> GetAllSkills() => _skills.Values;

        public void Dispose()
        {
            ReleaseAllSkills();
            _keyHandler.Dispose();
            _skills.Clear();
        }
    }
}