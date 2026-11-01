using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>플레이어 위치를 기준으로 오염 계산. 진행 순서는 WardStatus가 관리.</summary>
    [DisallowMultipleComponent]
    public sealed class PlayerContamination : MonoBehaviour
    {
        [SerializeField, Range(0f, 100f)] private float contamination;
        private float contaminationGainMultiplier = 1f;
        public float Value => contamination;
        public float ExposurePerSecond { get; private set; }
        /// <summary>실제로 증가한 직접 피격 오염량. UI/SFX는 이 이벤트만 구독합니다.</summary>
        public event Action<float> DirectDamageApplied;
        /// <summary>위험 영역에 머무르는 동안 실제로 증가한 오염량.</summary>
        public event Action<float> ExposureContaminationApplied;

        /// <summary>강화 효과를 절대 배수로 설정합니다. 호출할 때마다 중첩되지 않습니다.</summary>
        public void SetContaminationGainMultiplier(float multiplier)
        {
            contaminationGainMultiplier = Mathf.Clamp01(multiplier);
        }

        /// <summary>회복 아이템 사용에 성공하면 오염도를 0 아래로 내리지 않고 줄임.</summary>
        public bool TryReduceContamination(float amount)
        {
            if (contamination <= 0f || amount <= 0f) return false;
            contamination = Mathf.Clamp(contamination - amount, 0f, 100f);
            return true;
        }

        /// <summary>장치 방출 등 한 번의 직접 피격으로 오염도를 올립니다.</summary>
        public float AddContamination(float amount)
        {
            if (amount <= 0f) return 0f;
            float previous = contamination;
            contamination = Mathf.Clamp(contamination + amount * contaminationGainMultiplier, 0f, 100f);
            float applied = contamination - previous;
            if (applied > 0f) DirectDamageApplied?.Invoke(applied);
            return applied;
        }

        /// <summary>새 게임 초기화에만 사용.</summary>
        public void ResetForNewRun()
        {
            contamination = 0f;
            contaminationGainMultiplier = 1f;
            ExposurePerSecond = 0f;
        }

        /// <summary>새 DAY 또는 같은 DAY 재도전 때 오염도를 초기화합니다.</summary>
        public void ResetForNewDay() => ResetForNewRun();

        /// <summary>DAY 전환 때 현재 오염량을 유지하면서 노출 표시만 초기화.</summary>
        public void ResetExposureDisplay()
        {
            ExposurePerSecond = 0f;
        }

        public void Advance(float deltaTime, IReadOnlyList<AnomalyBehaviour> anomalies)
        {
            ExposurePerSecond = 0f;
            float overlappingCurseRate = 0f;
            foreach (AnomalyBehaviour anomaly in anomalies)
            {
                if (anomaly == null) continue;
                float anomalyRate = anomaly.ContaminationRateAt(this);
                if (anomaly is CurseAnomaly)
                    overlappingCurseRate = Mathf.Max(overlappingCurseRate, anomalyRate);
                else
                    ExposurePerSecond += anomalyRate;
            }
            // 겹친 저주 오염 패치는 가장 높은 접촉 피해율 하나만 적용합니다.
            ExposurePerSecond = (ExposurePerSecond + overlappingCurseRate) * contaminationGainMultiplier;
            float previous = contamination;
            contamination = Mathf.Clamp(contamination + ExposurePerSecond * Mathf.Max(0f, deltaTime), 0f, 100f);
            float applied = contamination - previous;
            if (applied > 0f) ExposureContaminationApplied?.Invoke(applied);
        }
    }
}
