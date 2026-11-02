using UnityEngine;

namespace WardShift
{
    /// <summary>시설 내 구역의 공통 식별 정보입니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ZoneIdentity : MonoBehaviour
    {
        [SerializeField] private FacilityZoneId zoneId;
        [SerializeField] private string displayName;

        public FacilityZoneId Id => zoneId;
        public string DisplayName => string.IsNullOrWhiteSpace(displayName) ? zoneId.ToString() : displayName;

        public void Configure(FacilityZoneId id, string label)
        {
            zoneId = id;
            displayName = label;
        }

        /// <summary>높이 차이를 무시하고 가장 가까운 구역 앵커를 반환합니다.</summary>
        public static ZoneIdentity FindNearest(Vector3 worldPosition, ZoneIdentity[] candidates)
        {
            if (candidates == null) return null;

            ZoneIdentity nearest = null;
            float nearestDistance = float.PositiveInfinity;
            foreach (ZoneIdentity candidate in candidates)
            {
                if (candidate == null || !candidate.isActiveAndEnabled) continue;
                Vector3 offset = candidate.transform.position - worldPosition;
                offset.y = 0f;
                float distance = offset.sqrMagnitude;
                if (distance >= nearestDistance) continue;
                nearest = candidate;
                nearestDistance = distance;
            }
            return nearest;
        }
    }

    public enum FacilityZoneId
    {
        StartRoom,
        ControlHall,
        PurificationRoom,
        SealingRoom,
        ManaCoreRoom
    }
}
