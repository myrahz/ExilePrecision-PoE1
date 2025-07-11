using ExileCore;
using ExilePrecision.Core.Combat;
using ExilePrecision.Core.Combat.Skills;
using ExilePrecision.Core.Combat.State;
using ExilePrecision.Features.Targeting.EntityInformation;
using ExilePrecision.Settings;
using ExilePrecision.Features.Targeting;
using ExilePrecision.Features.Targeting.Priority;
using ExilePrecision.Routines.EConcRoutine.Strategy;
using ExilePrecision.Utils;
using ExilePrecision.Core.Events;
using ExilePrecision.Core.Events.Events;
using System;
using System.Numerics;

namespace ExilePrecision.Routines.EConcRoutine
{
    public class EConcRoutine : OrbWalkingRoutineBase
    {
        private readonly TargetSelector _targetSelector;
        private readonly SkillPriority _skillPriority;
        private readonly LineOfSight _lineOfSight;

        public EConcRoutine(GameController gameController)
            : base("EConc", gameController)
        {
            _lineOfSight = new LineOfSight(gameController);

            var entityScanner = new EntityScanner(gameController, _lineOfSight);
            var priorityCalculator = new PriorityCalculator(gameController);

            _targetSelector = new TargetSelector(
                gameController,
                entityScanner,
                priorityCalculator,
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

        protected override EntityInfo GetTarget()
        {
            _targetSelector.Update();
            var target = _targetSelector.GetCurrentTarget();
            return target != null ? new EntityInfo(target, GameController) : null;
        }

        protected override void ExecuteCombatTick()
        {
            if (CurrentTarget == null) return;

            var nextSkill = _skillPriority.GetNextSkill(
                CurrentTarget,
                SkillHandler.GetAllSkills(),
                SkillMonitor);

            if (nextSkill != null)
            {
                var screenPos = CurrentTarget.ScreenPos;
                if (screenPos != Vector2.Zero)
                {
                    using (Input.InputManager.BlockUserMouseInput())
                    {
                        //Input.InputManager.MoveMouse(screenPos);
                        ExileCore.Input.SetCursorPos(screenPos);
                        {
                            //ExileCore.Input.SetCursorPos(screenPos);

                            //if (IsCursorOnTarget(CurrentTarget))
                            {
                                SkillMonitor.TrackUse(nextSkill);
                                SkillHandler.UseSkill(nextSkill.Name);
                            }
                        }
                    }
                }
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
                _targetSelector?.Clear();
            }
            base.Dispose(disposing);
        }
    }
}