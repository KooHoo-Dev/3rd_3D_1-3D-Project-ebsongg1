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
        /// <summary>지도와 상태 HUD에 표시할 이상현상 이름입니다.</summary>
        public virtual string DisplayName => name.Replace("(Clone)", string.Empty).Trim();
        public virtual int DangerStage => 0;
        /// <summary>현재 단계 다음의 성장 또는 최종 파열까지 남은 초입니다. HUD는 이 공통 타이머만 읽습니다.</summary>
        public virtual float SecondsUntilNextStage => float.PositiveInfinity;
        /// <summary>UI에서 읽는 현재 단계 기준 파열까지 남은 초입니다.</summary>
        public virtual float SecondsUntilRupture => float.PositiveInfinity;
        public virtual bool HasOverloaded => false;
        /// <summary>Normal-resolution reward amount. A negative value uses the DAY fallback range.</summary>
        public virtual int ResolutionReward => -1;
        public virtual float AdvanceHazard(float deltaTime) => 0f;
        /// <summary>Returns continuous stability loss separately from rupture damage for facility upgrades.</summary>
        public virtual float AdvanceHazard(float deltaTime, out float immediateStabilityDamage)
        {
            immediateStabilityDamage = 0f;
            return AdvanceHazard(deltaTime);
        }
        public virtual float ContaminationRateAt(Vector3 position) => 0f;
        /// <summary>플레이어 발 접촉 등 대상의 점유 영역을 고려할 수 있는 오염 노출 조회.</summary>
        public virtual float ContaminationRateAt(PlayerContamination player) =>
            player != null ? ContaminationRateAt(player.transform.position) : 0f;
        public ZoneIdentity Zone { get; private set; }
        public string SpawnLocationLabel { get; private set; }

        public void SetZoneIdentity(ZoneIdentity zone)
        {
            Zone = zone;
        }

        public void SetSpawnLocationLabel(string label)
        {
            SpawnLocationLabel = string.IsNullOrWhiteSpace(label) ? name : label.Trim();
        }

        /// <summary>- 정상 진압 보상 처리를 위한 공통 이벤트</summary>
        public event Action<AnomalyBehaviour> ResolutionCompleted;

        /// <summary>- 종료 시 생성 지점과 활성 슬롯 반환 이벤트</summary>
        public event Action<AnomalyBehaviour, AnomalyEndReason> Ended;

        protected bool HasEnded => hasEnded;

        protected void PlayRuptureSfx(Vector3 position)
        {
            AudioService.Play3DAt(WardShiftSoundId.AnomalyRupture, position, GetInstanceID());
        }

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
