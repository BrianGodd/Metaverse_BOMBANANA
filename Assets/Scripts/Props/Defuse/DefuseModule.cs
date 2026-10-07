using System;
using Bombanana.Timing;
using UnityEngine;
using UnityEngine.Events;

namespace Bombanana.Gameplay
{
    public enum DefuseModuleState { Inactive, Active, Completed, Failed }

    public abstract class DefuseModule : MonoBehaviour
    {
        [SerializeField] private UnityEvent _onCompleted = new UnityEvent();
        public DefuseModuleState State { get; private set; }
        public string FailureReason { get; private set; }
        public virtual CountdownTimer LocalTimer => null;
        protected BombController Owner { get; private set; }
        public event Action<DefuseModule> Completed;
        public event Action<DefuseModule, string> Failed;

        public abstract bool ValidateConfiguration(out string error);

        internal void Activate(BombController owner)
        {
            Owner = owner;
            State = DefuseModuleState.Active;
            OnActivate();
        }

        public bool ResetModule()
        {
            StopModule();
            State = DefuseModuleState.Inactive;
            Owner = null;
            FailureReason = null;
            return OnReset();
        }

        internal void StopModule()
        {
            if (State == DefuseModuleState.Active) State = DefuseModuleState.Inactive;
            OnStop();
        }

        protected bool CanAcceptInput() => State == DefuseModuleState.Active && Owner.CheckTimeRemaining();

        protected void Complete()
        {
            if (!CanAcceptInput()) return;
            State = DefuseModuleState.Completed;
            OnStop();
            _onCompleted.Invoke();
            Completed?.Invoke(this);
        }

        protected void Fail(string reason)
        {
            if (State != DefuseModuleState.Active) return;
            State = DefuseModuleState.Failed;
            FailureReason = reason;
            OnStop();
            Failed?.Invoke(this, reason);
        }

        protected virtual void OnDisable() => StopModule();
        protected abstract void OnActivate();
        protected abstract bool OnReset();
        protected abstract void OnStop();
    }
}
