using System;
using System.Numerics;
using ExileCore;
using ExilePrecision.Core.Combat.State;
using ExilePrecision.Core.Events;
using ExilePrecision.Core.Events.Events;
using ExilePrecision.Features.Targeting.EntityInformation;

namespace ExilePrecision.Core.Combat
{
    public abstract class OrbWalkingRoutineBase : RoutineBase
    {
        private const string MOVE_SKILL_NAME = "Move";
        private Vector2 _preTargetMousePosition;
        private bool _inCombat;

        protected OrbWalkingRoutineBase(string name, GameController gameController)
            : base(name, gameController)
        {
        }

        protected override void HandleTick(TickEvent evt)
        {
            if (!CanExecute || IsDisposed) return;

            try
            {
                if (evt.IsActive)
                {
                    OnTickActive();
                }
                else
                {
                    Stop();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error handling tick: {ex.Message}");
                Stop();
                StateCoordinator.SetError(ex);
            }
        }

        protected override void OnTickActive()
        {
            if (!CanExecute)
            {
                Stop();
                return;
            }

            try
            {
                var target = GetTarget();

                if (target?.Entity?.Address != CurrentTarget?.Entity?.Address)
                {
                    EventBus.Instance.Publish(new TargetChangedEvent
                    {
                        OldTarget = CurrentTarget,
                        NewTarget = target
                    });

                    CurrentTarget = target;

                    SkillHandler.ReleaseAllSkills();

                    if (CurrentTarget != null && !_inCombat)
                    {
                        BeginCombat();
                    }
                    else if (CurrentTarget == null && _inCombat)
                    {
                        EndCombat();
                    }
                }

                if (_inCombat && CurrentTarget != null)
                {
                    StateCoordinator.SetState(RoutineState.Active);
                    ExecuteCombatTick();
                }
                else
                {
                    StateCoordinator.SetState(RoutineState.Orbwalking);
                    Orbwalk();
                }
            }
            catch (Exception ex)
            {
                LogError($"Error in OnTickActive: {ex.Message}");
                Stop();
                StateCoordinator.SetError(ex);
            }
        }

        protected abstract EntityInfo GetTarget();
        protected abstract void ExecuteCombatTick();

        private void BeginCombat()
        {
            if (!_inCombat)
            {
                //_preTargetMousePosition = ExileCore.Input.MousePosition;
                _inCombat = true;
                SkillHandler.ReleaseAllSkills();
            }
        }

        private void EndCombat()
        {
            if (_inCombat)
            {
                //ExileCore.Input.SetCursorPos(_preTargetMousePosition);
                _inCombat = false;
                SkillHandler.ReleaseAllSkills();
            }
        }

        private void Orbwalk()
        {
            if (!_inCombat)
            {
                SkillHandler.UseSkill(MOVE_SKILL_NAME, true);
            }
        }

        public override void Stop()
        {
            if (_inCombat)
            {
                EndCombat();
            }
            SkillHandler.ReleaseAllSkills();
            base.Stop();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Stop();
            }
            base.Dispose(disposing);
        }
    }
}