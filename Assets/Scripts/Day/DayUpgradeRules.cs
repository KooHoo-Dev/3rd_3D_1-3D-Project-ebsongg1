using System;

namespace WardShift
{
    /// <summary>공통 강화 단계 범위와 단계별 가격 인덱스를 제공한다.</summary>
    public static class DayUpgradeRules
    {
        public const int MaximumLevel = 3;

        public static int ClampLevel(int level) => Math.Max(0, Math.Min(MaximumLevel, level));

        public static int NextPrice(int level, int[] stagePrices)
        {
            level = ClampLevel(level);
            return stagePrices == null || level >= MaximumLevel || level >= stagePrices.Length
                ? 0 : Math.Max(0, stagePrices[level]);
        }
    }
}
