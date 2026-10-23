using System;
using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>가장 가까운 시설 구역과 이번 실행에서 방문한 구역을 추적합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class FacilityZoneTracker : MonoBehaviour
    {
        [SerializeField] private ZoneIdentity[] zones = new ZoneIdentity[0];
        [SerializeField] private DayFlowManager dayFlow;

        private readonly HashSet<FacilityZoneId> visitedZones = new HashSet<FacilityZoneId>();

        public ZoneIdentity CurrentZone { get; private set; }
        public event Action<ZoneIdentity> CurrentZoneChanged;
        public event Action<FacilityZoneId> ZoneVisited;

        private void Awake()
        {
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (zones == null || zones.Length == 0)
                zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
        }

        private void Update()
        {
            if (Time.timeScale <= 0f || (dayFlow != null && dayFlow.State != DayState.Playing)) return;

            ZoneIdentity next = ZoneIdentity.FindNearest(transform.position, zones);
            if (next == CurrentZone) return;

            CurrentZone = next;
            CurrentZoneChanged?.Invoke(CurrentZone);
            if (CurrentZone != null && visitedZones.Add(CurrentZone.Id))
                ZoneVisited?.Invoke(CurrentZone.Id);
        }

        public bool HasVisited(FacilityZoneId zone) => visitedZones.Contains(zone);

        /// <summary>새 런을 시작할 때만 호출합니다. DAY 전환에서는 방문 기록을 유지합니다.</summary>
        public void ResetVisitedZones()
        {
            visitedZones.Clear();
            CurrentZone = null;
            CurrentZoneChanged?.Invoke(null);
        }

        public void Configure(ZoneIdentity[] configuredZones, DayFlowManager manager)
        {
            zones = configuredZones ?? new ZoneIdentity[0];
            dayFlow = manager;
        }
    }
}
