using ExileCore;
using ExilePrecision.Core.Combat;
using ExilePrecision.Core.Combat.Skills;
using ExilePrecision.Core.Combat.State;
using ExilePrecision.Features.Targeting.EntityInformation;
using ExilePrecision.Settings;
using ExilePrecision.Features.Targeting;
using ExilePrecision.Features.Targeting.Priority;
using ExilePrecision.Routines.ConcLeveling.Strategy;
using ExilePrecision.Utils;
using ExilePrecision.Core.Events;
using ExilePrecision.Core.Events.Events;
using System;
using System.Numerics;
using System.Collections.Generic;

namespace ExilePrecision.Routines.ConcLeveling
{
    public class ConcLeveling : OrbWalkingRoutineBase
    {
        private readonly TargetSelector _targetSelector;
        private readonly SkillPriority _skillPriority;
        private readonly LineOfSight _lineOfSight;
        private readonly PriorityCalculator _priorityCalculator;

        public ConcLeveling(GameController gameController)
            : base("ConcLeveling", gameController)
        {
            _lineOfSight = new LineOfSight(gameController);

            var entityScanner = new EntityScanner(gameController, _lineOfSight);
            _priorityCalculator = new PriorityCalculator(gameController);

            _targetSelector = new TargetSelector(
                gameController,
                entityScanner,
                _priorityCalculator,
                _lineOfSight
            );

            _targetSelector.Configure();
            _skillPriority = new SkillPriority(gameController);

            var eventBus = EventBus.Instance;
            eventBus.Subscribe<RenderEvent>(HandleRender);
        }

        protected override void InitializeSkills()
        {
            try
            {
                SkillHandler.Initialize();
                StateCoordinator.SetState(RoutineState.Idle);
            }
            catch (Exception ex)
            {
                LogError($"Error initializing skills: {ex.Message}");
                StateCoordinator.SetError(ex);
            }
        }

        protected override (ActiveSkill skill, EntityInfo target) GetBestAction()
        {
            try
            {
                _targetSelector.Update(SkillHandler.GetAllSkills());
                return _skillPriority.GetBestAction(
                    SkillHandler.GetAllSkills(),
                    _targetSelector,
                    _priorityCalculator,
                    SkillMonitor
                );
            }
            catch (Exception ex)
            {
                LogError($"Error in GetBestAction: {ex.Message}");
                return (null, null);
            }
        }

        protected override void ExecuteCombatTick(ActiveSkill skill, EntityInfo target)
        {
            try
            {
                if (target == null || skill == null) return;

                var screenPos = target.ScreenPos;
                if (screenPos != Vector2.Zero)
                {
                    using (Input.InputManager.BlockUserMouseInput())
                    {
                        Input.InputManager.MoveMouse(screenPos);
                        //if (IsCursorOnTarget(target)) // doesnt really matter for ConcLeveling
                        {
                            SkillMonitor.TrackUse(skill);
                            SkillHandler.UseSkill(skill.Name);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in ExecuteCombatTick: {ex.Message}");
            }
        }

        private void HandleRender(RenderEvent evt)
        {
            if (!ExilePrecision.Instance.Settings.Render.EnableRendering) return;

            try
            {
                CombatRenderer.Render(evt.Graphics, CurrentTarget, StateCoordinator.CurrentState);
            }
            catch (Exception ex)
            {
                LogError($"Error in render: {ex.Message}");
            }
        }

        protected override void HandleAreaChange(AreaChangeEvent evt)
        {
            _targetSelector?.Clear();
            StateCoordinator.Reset();
            base.HandleAreaChange(evt);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                var eventBus = EventBus.Instance;
                eventBus.Unsubscribe<RenderEvent>(HandleRender);
                _targetSelector?.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}