using System;

namespace WardShift
{
    /// <summary>강화 단계에서 파생되는 가격과 효과 수치. 씬 오브젝트를 참조하지 않는다.</summary>
    public static class DayUpgradeRules
    {
        public static int ClampLevel(int level) => Math.Max(0, Math.Min(2, level));

        public static int NextPrice(int level, int[] stagePrices)
        {
            level = ClampLevel(level);
            return level >= 2 ? 0 : Math.Max(0, stagePrices[level]);
        }

        public static float MovementMultiplier(int level) =>
            ClampLevel(level) == 2 ? 1.2f : ClampLevel(level) == 1 ? 1.1f : 1f;

        public static float ContaminationGainMultiplier(int level) =>
            ClampLevel(level) == 2 ? 0.65f : ClampLevel(level) == 1 ? 0.8f : 1f;

        public static float PotionRecovery(int level, float baseRecovery) =>
            ClampLevel(level) == 2 ? 60f : ClampLevel(level) == 1 ? 45f : baseRecovery;
    }
}
