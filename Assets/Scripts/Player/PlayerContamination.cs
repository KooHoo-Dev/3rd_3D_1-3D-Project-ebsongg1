using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>플레이어 위치를 기준으로 오염 계산. 진행 순서는 WardStatus가 관리.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerContamination : MonoBehaviour
    {
        [SerializeField, Min(0f)] private float recoveryPerSecond = 10f;
        [SerializeField, Range(0f, 100f)] private float contamination;
        public float Value => contamination;
        public bool InSafeZone { get; private set; }
        public float ExposurePerSecond { get; private set; }

        /// <summary>회복 아이템 사용에 성공하면 오염도를 0 아래로 내리지 않고 줄임.</summary>
        public bool TryReduceContamination(float amount)
        {
            if (contamination <= 0f || amount <= 0f) return false;
            contamination = Mathf.Clamp(contamination - amount, 0f, 100f);
            return true;
        }

        /// <summary>장치 방출 등 한 번의 직접 피격으로 오염도를 올립니다.</summary>
        public void AddContamination(float amount)
        {
            if (amount <= 0f) return;
            contamination = Mathf.Clamp(contamination + amount, 0f, 100f);
        }

        /// <summary>새 게임 초기화에만 사용.</summary>
        public void ResetForNewRun()
        {
            contamination = 0f;
            InSafeZone = false;
            ExposurePerSecond = 0f;
        }

        /// <summary>DAY 전환 때 현재 오염량을 유지하면서 안전 구역 표시만 초기화.</summary>
        public void ResetExposureDisplay()
        {
            InSafeZone = false;
            ExposurePerSecond = 0f;
        }

        public void Advance(float deltaTime, IReadOnlyList<AnomalyBehaviour> anomalies, SafeZone[] zones)
        {
            InSafeZone = false;
            foreach (SafeZone zone in zones)
                if (zone != null && zone.Contains(transform.position)) { InSafeZone = true; break; }
            ExposurePerSecond = 0f;
            if (!InSafeZone)
                foreach (AnomalyBehaviour anomaly in anomalies)
                    if (anomaly != null) ExposurePerSecond += anomaly.ContaminationRateAt(transform.position);
            float rate = InSafeZone ? -Mathf.Max(0f, recoveryPerSecond) : ExposurePerSecond;
            contamination = Mathf.Clamp(contamination + rate * Mathf.Max(0f, deltaTime), 0f, 100f);
        }
    }
}
