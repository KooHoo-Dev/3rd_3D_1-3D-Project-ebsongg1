using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>
    /// - DAY 중 관리되는 이상현상의 공통 수명주기 제공
    /// - 생성 표면, 위험·오염 상태 및 종료 이벤트 정의
    /// - 스포너·DAY 진행·시설 상태 관리에서 공통 계약 사용
    /// </summary>
    public abstract class AnomalyBehaviour : MonoBehaviour
    {
        private static readonly List<AnomalyBehaviour> activeAnomalies = new List<AnomalyBehaviour>();
        private bool hasEnded;

        public static IReadOnlyList<AnomalyBehaviour> ActiveAnomalies
        {
            get
            {
                activeAnomalies.RemoveAll(anomaly => anomaly == null || !anomaly.isActiveAndEnabled || anomaly.IsEnded);
                return activeAnomalies;
            }
        }

        public abstract AnomalySpawnPoint.SurfaceType SpawnSurface { get; }
        public abstract bool IsHazardActive { get; }
        public bool IsEnded => hasEnded;
        public virtual int DangerStage => 0;
        public virtual bool HasOverloaded => false;
        public virtual float AdvanceHazard(float deltaTime) => 0f;
        public virtual float ContaminationRateAt(Vector3 position) => 0f;

        /// <summary>- 정상 진압 보상 처리를 위한 공통 이벤트</summary>
        public event Action<AnomalyBehaviour> ResolutionCompleted;

        /// <summary>- 종료 시 생성 지점과 활성 슬롯 반환 이벤트</summary>
        public event Action<AnomalyBehaviour, AnomalyEndReason> Ended;

        protected bool HasEnded => hasEnded;

        protected virtual void OnEnable()
        {
            if (!hasEnded && !activeAnomalies.Contains(this)) activeAnomalies.Add(this);
        }

        protected virtual void OnDisable()
        {
            activeAnomalies.Remove(this);
        }

        protected virtual void OnDestroy()
        {
            activeAnomalies.Remove(this);
            if (hasEnded) return;
            hasEnded = true;
            Ended?.Invoke(this, AnomalyEndReason.Removed);
        }

        /// <summary>- 이상현상 자체 완료 효과와 진압 이벤트 발생 후 호출</summary>
        protected void NotifyResolved()
        {
            if (hasEnded) return;
            hasEnded = true;
            try
            {
                ResolutionCompleted?.Invoke(this);
            }
            finally
            {
                Ended?.Invoke(this, AnomalyEndReason.Resolved);
            }
        }

        /// <summary>- 파열로 종료되는 이상현상에서 호출</summary>
        protected void NotifyRuptured()
        {
            if (hasEnded) return;
            hasEnded = true;
            Ended?.Invoke(this, AnomalyEndReason.Ruptured);
        }
    }

    public enum AnomalyEndReason
    {
        Resolved,
        Ruptured,
        Removed
    }
}
