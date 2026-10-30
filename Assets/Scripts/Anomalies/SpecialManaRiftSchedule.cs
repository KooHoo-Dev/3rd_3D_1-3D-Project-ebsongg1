using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>DAY별 특별 마력 균열 일정과 현재 DAY의 1회 출현 여부를 관리합니다.</summary>
    [Serializable]
    public sealed class SpecialManaRiftSchedule
    {
        [Serializable]
        public sealed class DayRule
        {
            [SerializeField, Min(1)] private int day = 1;
            [SerializeField] private bool enabled;
            [SerializeField, Min(0f)] private float spawnAfterSeconds;
            [SerializeField, Range(2, 3)] private int initialDangerStage = 2;

            public int Day => Mathf.Max(1, day);
            public bool Enabled => enabled;
            public float SpawnAfterSeconds => Mathf.Max(0f, spawnAfterSeconds);
            public int InitialDangerStage => Mathf.Clamp(initialDangerStage, 2, 3);

            public DayRule() { }

            public DayRule(int day, bool enabled, float spawnAfterSeconds, int initialDangerStage)
            {
                this.day = day;
                this.enabled = enabled;
                this.spawnAfterSeconds = spawnAfterSeconds;
                this.initialDangerStage = initialDangerStage;
            }
        }

        [SerializeField] private DayRule[] dayRules = CreateDefaultRules();

        [NonSerialized] private int configuredDay = -1;
        [NonSerialized] private bool completedForDay;

        public void ConfigureForDay(int day)
        {
            configuredDay = Mathf.Max(1, day);
            completedForDay = false;
        }

        public bool IsPendingForDay(int day)
        {
            DayRule rule = FindRule(day);
            return configuredDay == Mathf.Max(1, day) && !completedForDay
                && rule != null && rule.Enabled;
        }

        public bool TryGetDueStage(int day, float gameTimeSeconds, out int initialDangerStage)
        {
            initialDangerStage = 1;
            if (!IsPendingForDay(day)) return false;

            DayRule rule = FindRule(day);
            if (gameTimeSeconds < rule.SpawnAfterSeconds) return false;
            initialDangerStage = rule.InitialDangerStage;
            return true;
        }

        public void MarkSpawned(int day)
        {
            if (configuredDay == Mathf.Max(1, day)) completedForDay = true;
        }

        public void MarkMissed(int day)
        {
            if (configuredDay == Mathf.Max(1, day)) completedForDay = true;
        }

        private DayRule FindRule(int day)
        {
            if (dayRules == null) return null;
            int targetDay = Mathf.Max(1, day);
            foreach (DayRule rule in dayRules)
                if (rule != null && rule.Day == targetDay) return rule;
            return null;
        }

        private static DayRule[] CreateDefaultRules()
        {
            return new[]
            {
                new DayRule(1, false, 0f, 2),
                new DayRule(2, false, 0f, 2),
                new DayRule(3, false, 0f, 2),
                new DayRule(4, false, 0f, 2),
                new DayRule(5, true, 75f, 2),
                new DayRule(6, true, 90f, 2),
                new DayRule(7, true, 100f, 3)
            };
        }
    }
}
