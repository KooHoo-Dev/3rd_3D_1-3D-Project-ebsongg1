using System;
using UnityEngine;

namespace WardShift
{
    /// <summary>하루에 적용되는 범람 조건입니다. 새 조건은 enum과 아래 규칙에 추가합니다.</summary>
    public enum FloodCondition
    {
        None,
        PurificationCycle,
        EarlySealBonus
    }

    /// <summary>범람 조건의 선택, 설명, 수치 조정을 한 곳에서 관리합니다.</summary>
    public static class FloodConditionRules
    {
        public const int FirstFloodConditionDay = 4;

        private static readonly FloodCondition[] AvailableConditions =
        {
            FloodCondition.PurificationCycle,
            FloodCondition.EarlySealBonus
        };

        public static FloodCondition SelectForDay(int day, int seed, FloodCondition previousCondition,
            bool useTestOverride = false, FloodCondition testOverride = FloodCondition.None)
        {
            if (day < FirstFloodConditionDay) return FloodCondition.None;
            if (useTestOverride && IsAvailableCondition(testOverride)) return testOverride;

            int index = new System.Random(seed).Next(AvailableConditions.Length);
            FloodCondition selected = AvailableConditions[index];
            if (selected == previousCondition)
                selected = AvailableConditions[(index + 1) % AvailableConditions.Length];
            return selected;
        }

        public static bool IsAvailableCondition(FloodCondition condition)
        {
            foreach (FloodCondition available in AvailableConditions)
                if (available == condition) return true;
            return false;
        }

        public static float PotionRecoveryMultiplier(FloodCondition condition)
        {
            return condition == FloodCondition.PurificationCycle ? 2f / 3f : 1f;
        }

        /// <summary>기본 회복량 또는 현장 정화 강화량에 조건 배수를 한 번 적용합니다.</summary>
        public static float PotionRecoveryAmount(float baseRecovery, FloodCondition condition)
        {
            return Mathf.Max(0f, baseRecovery)
                * PotionRecoveryMultiplier(condition);
        }

        /// <summary>단계 1 진압 보상만 올립니다. 자동 정리에는 호출하지 않습니다.</summary>
        public static int ResolutionReward(int baseReward, FloodCondition condition)
        {
            return condition == FloodCondition.EarlySealBonus && baseReward == 1 ? 2 : baseReward;
        }

        public static string GetName(FloodCondition condition)
        {
            switch (condition)
            {
                case FloodCondition.PurificationCycle: return "정화 순환 강화";
                case FloodCondition.EarlySealBonus: return "조기 봉합 보너스";
                default: return "기본 조건";
            }
        }

        public static string GetRuleDescription(FloodCondition condition)
        {
            switch (condition)
            {
                case FloodCondition.PurificationCycle:
                    return "정화약 회복량 2/3";
                case FloodCondition.EarlySealBonus:
                    return "1단계 이상현상 진압 보상 1개 → 2개 · 2·3단계 보상 유지";
                default:
                    return "특수 규칙 없음";
            }
        }

        /// <summary>HUD 설명에 현재 규칙의 실제 수치를 색으로 강조합니다.</summary>
        public static string GetHighlightedRuleDescription(FloodCondition condition)
        {
            switch (condition)
            {
                case FloodCondition.PurificationCycle:
                    float potionMultiplier = PotionRecoveryMultiplier(condition);
                    return "정화약 회복량 <color=#F5A067>"
                        + FormatRatio(potionMultiplier) + "</color>";
                case FloodCondition.EarlySealBonus:
                    int baseReward = 1;
                    int increasedReward = ResolutionReward(baseReward, condition);
                    return "1단계 이상현상 진압 보상 <color=#75E6A5>" + baseReward + "개 → "
                        + increasedReward + "개</color> · 2·3단계 보상 유지";
                default:
                    return GetRuleDescription(condition);
            }
        }

        public static string GetResponseHint(FloodCondition condition)
        {
            switch (condition)
            {
                case FloodCondition.PurificationCycle:
                    return "정화약의 회복량이 줄어듭니다.";
                case FloodCondition.EarlySealBonus:
                    return "이상현상을 1단계에서 빠르게 진압하면 보상을 더 얻습니다.";
                default:
                    return "시설 상태를 살피고 익숙한 방식으로 대응하세요.";
            }
        }

        public static string GetDisplayText(FloodCondition condition)
        {
            return $"{GetName(condition)}\n{GetRuleDescription(condition)}\n대응 힌트: {GetResponseHint(condition)}";
        }

        private static string FormatRatio(float value)
        {
            if (value >= 1f) return value.ToString("0.##") + "배";
            for (int denominator = 2; denominator <= 12; denominator++)
            {
                int numerator = Mathf.RoundToInt(value * denominator);
                if (numerator > 0 && Mathf.Abs(value - numerator / (float)denominator) < 0.005f)
                    return numerator + "/" + denominator;
            }
            return value.ToString("0.##");
        }
    }
}
