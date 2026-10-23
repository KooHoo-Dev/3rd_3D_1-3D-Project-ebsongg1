using System.Collections.Generic;
using UnityEngine;

namespace WardShift
{
    /// <summary>지도와 화면 핑이 공유하는 구역별 활성 이상현상 집계입니다.</summary>
    public sealed class ActiveAnomalyZoneSnapshot
    {
        public sealed class ZoneActivity
        {
            private readonly List<AnomalyBehaviour> anomalies = new List<AnomalyBehaviour>();

            public ZoneIdentity Zone { get; private set; }
            public int ActiveCount => anomalies.Count;
            public int HighestDangerStage { get; private set; }
            public IReadOnlyList<AnomalyBehaviour> Anomalies => anomalies;

            internal ZoneActivity(ZoneIdentity zone)
            {
                Zone = zone;
            }

            internal void Reset(ZoneIdentity zone)
            {
                Zone = zone;
                anomalies.Clear();
                HighestDangerStage = 0;
            }

            internal void Add(AnomalyBehaviour anomaly)
            {
                anomalies.Add(anomaly);
                HighestDangerStage = Mathf.Max(HighestDangerStage, anomaly.DangerStage);
            }
        }

        private readonly Dictionary<FacilityZoneId, ZoneActivity> byZone =
            new Dictionary<FacilityZoneId, ZoneActivity>();
        private readonly List<ZoneActivity> activities = new List<ZoneActivity>();
        private ZoneIdentity[] configuredZones;

        public IReadOnlyList<ZoneActivity> Activities => activities;

        public void Refresh(ZoneIdentity[] zones)
        {
            EnsureZones(zones);
            foreach (ZoneActivity activity in activities)
                activity.Reset(activity.Zone);

            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
            {
                if (anomaly == null || anomaly.IsEnded || !anomaly.isActiveAndEnabled
                    || !anomaly.IsHazardActive)
                    continue;

                ZoneIdentity zone = ResolveZone(anomaly, zones);
                if (zone == null || !byZone.TryGetValue(zone.Id, out ZoneActivity activity)) continue;
                activity.Add(anomaly);
            }
        }

        public void Clear()
        {
            foreach (ZoneActivity activity in activities)
                activity.Reset(activity.Zone);
        }

        public bool TryGetActivity(FacilityZoneId zone, out ZoneActivity activity)
        {
            return byZone.TryGetValue(zone, out activity);
        }

        private void EnsureZones(ZoneIdentity[] zones)
        {
            zones = zones ?? new ZoneIdentity[0];
            bool unchanged = configuredZones != null && configuredZones.Length == zones.Length;
            if (unchanged)
            {
                for (int i = 0; i < zones.Length; i++)
                    if (configuredZones[i] != zones[i]) { unchanged = false; break; }
            }
            if (unchanged) return;

            configuredZones = (ZoneIdentity[])zones.Clone();
            byZone.Clear();
            activities.Clear();
            foreach (ZoneIdentity zone in configuredZones)
            {
                if (zone == null || byZone.ContainsKey(zone.Id)) continue;
                ZoneActivity activity = new ZoneActivity(zone);
                byZone.Add(zone.Id, activity);
                activities.Add(activity);
            }
        }

        private ZoneIdentity ResolveZone(AnomalyBehaviour anomaly, ZoneIdentity[] zones)
        {
            if (anomaly.Zone != null
                && byZone.TryGetValue(anomaly.Zone.Id, out ZoneActivity direct)
                && direct.Zone == anomaly.Zone)
                return direct.Zone;

            ZoneIdentity nearest = ZoneIdentity.FindNearest(anomaly.transform.position, zones);
            return nearest != null && byZone.ContainsKey(nearest.Id) ? nearest : null;
        }
    }
}
