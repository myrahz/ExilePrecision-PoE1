using ExilePrecision.Core.Combat.State;
using System;

namespace ExilePrecision.Core.Combat
{
    public interface IRoutine : IDisposable
    {
        string Name { get; }
        bool CanExecute { get; }
        RoutineState State { get; }
        bool Initialize();
        void Stop();
    }
}