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

        public void Advance(float deltaTime, ManaRift[] rifts, SafeZone[] zones)
        {
            InSafeZone = false;
            foreach (SafeZone zone in zones)
                if (zone != null && zone.Contains(transform.position)) { InSafeZone = true; break; }
            ExposurePerSecond = 0f;
            if (!InSafeZone)
                foreach (ManaRift rift in rifts)
                    if (rift != null) ExposurePerSecond += rift.ContaminationRateAt(transform.position);
            float rate = InSafeZone ? -Mathf.Max(0f, recoveryPerSecond) : ExposurePerSecond;
            contamination = Mathf.Clamp(contamination + rate * Mathf.Max(0f, deltaTime), 0f, 100f);
        }
    }
}
