using UnityEngine;

namespace WardShift
{
    /// <summary>영구 강화의 단계, Inspector 설정, 구매 가능 여부와 효과 적용을 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayUpgradeController : MonoBehaviour
    {
        [Header("기동력 단계별 증가율")]
        [SerializeField, Range(0f, 1f), InspectorName("1단계 이동속도 증가율"),
         Tooltip("0.05 = 5%. 각 값은 해당 단계의 누적 총 증가율입니다.")] private float mobilityStageOneBonus = 0.05f;
        [SerializeField, Range(0f, 1f), InspectorName("2단계 이동속도 증가율"),
         Tooltip("0.08 = 8%. 각 값은 해당 단계의 누적 총 증가율입니다.")] private float mobilityStageTwoBonus = 0.08f;
        [SerializeField, Range(0f, 1f), InspectorName("3단계 이동속도 증가율"),
         Tooltip("0.12 = 12%. 각 값은 해당 단계의 누적 총 증가율입니다.")] private float mobilityStageThreeBonus = 0.12f;

        [Header("시설 방호 단계별 지속 피해 감소율")]
        [SerializeField, Range(0f, 1f), InspectorName("1단계 지속 안정도 피해 감소율"),
         Tooltip("0.05 = 5%. 균열 파열 등 즉시 피해는 제외됩니다.")] private float facilityDefenseStageOneReduction = 0.05f;
        [SerializeField, Range(0f, 1f), InspectorName("2단계 지속 안정도 피해 감소율"),
         Tooltip("0.10 = 10%. 균열 파열 등 즉시 피해는 제외됩니다.")] private float facilityDefenseStageTwoReduction = 0.10f;
        [SerializeField, Range(0f, 1f), InspectorName("3단계 지속 안정도 피해 감소율"),
         Tooltip("0.15 = 15%. 균열 파열 등 즉시 피해는 제외됩니다.")] private float facilityDefenseStageThreeReduction = 0.15f;

        [Header("오염 저항 단계별 획득량 감소율")]
        [SerializeField, Range(0f, 1f), InspectorName("1단계 오염 획득량 감소율"),
         Tooltip("0.08 = 8%. 회복과 초기화에는 영향을 주지 않습니다.")] private float contaminationResistanceStageOneReduction = 0.08f;
        [SerializeField, Range(0f, 1f), InspectorName("2단계 오염 획득량 감소율"),
         Tooltip("0.15 = 15%. 회복과 초기화에는 영향을 주지 않습니다.")] private float contaminationResistanceStageTwoReduction = 0.15f;
        [SerializeField, Range(0f, 1f), InspectorName("3단계 오염 획득량 감소율"),
         Tooltip("0.22 = 22%. 회복과 초기화에는 영향을 주지 않습니다.")] private float contaminationResistanceStageThreeReduction = 0.22f;

        private PlayerInventory inventory;
        private DayPlayerUpgradeEffects effects;
        private int mobilityLevel;
        private int facilityDefenseLevel;
        private int resistanceLevel;
        private bool purchasedThisClear;

        public int MobilityLevel => mobilityLevel;
        public int FacilityDefenseLevel => facilityDefenseLevel;
        public int ResistanceLevel => resistanceLevel;
        public bool PurchasedThisClear => purchasedThisClear;

        public void Configure(PlayerInventory playerInventory, DayPlayerUpgradeEffects playerEffects,
            int initialMobility, int initialFacilityDefense, int initialResistance)
        {
            inventory = playerInventory;
            effects = playerEffects;
            mobilityLevel = DayUpgradeRules.ClampLevel(initialMobility);
            facilityDefenseLevel = DayUpgradeRules.ClampLevel(initialFacilityDefense);
            resistanceLevel = DayUpgradeRules.ClampLevel(initialResistance);
        }

        public int GetLevel(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return mobilityLevel;
                case DayUpgradeType.FacilityDefense: return facilityDefenseLevel;
                case DayUpgradeType.ContaminationResistance: return resistanceLevel;
                default: return 0;
            }
        }

        public int GetNextPrice(DayUpgradeType type, int[] stagePrices)
        {
            return DayUpgradeRules.NextPrice(GetLevel(type), stagePrices);
        }

        public string GetEffectText(DayUpgradeType type, int level)
        {
            float percentage = GetStageEffect(type, level) * 100f;
            string value = percentage.ToString("0.##");
            switch (type)
            {
                case DayUpgradeType.Mobility:
                    return level <= 0 ? "이동속도 보너스 없음" : $"이동속도 +{value}%";
                case DayUpgradeType.FacilityDefense:
                    return level <= 0 ? "지속 안정도 피해 감소 없음" : $"지속 안정도 피해 -{value}%";
                case DayUpgradeType.ContaminationResistance:
                    return level <= 0 ? "오염도 획득량 감소 없음" : $"오염도 획득량 -{value}%";
                default:
                    return string.Empty;
            }
        }

        public bool CanPurchase(DayUpgradeType type, DayState state, int[] stagePrices)
        {
            return state == DayState.DayClear && !purchasedThisClear
                && GetLevel(type) < DayUpgradeRules.MaximumLevel && inventory != null
                && inventory.ManaCrystals >= GetNextPrice(type, stagePrices);
        }

        public bool TryPurchase(DayUpgradeType type, DayState state, int[] stagePrices)
        {
            if (!CanPurchase(type, state, stagePrices)) return false;
            int price = GetNextPrice(type, stagePrices);
            if (!inventory.TrySpendManaCrystals(price)) return false;
            SetLevel(type, GetLevel(type) + 1);
            purchasedThisClear = true;
            return true;
        }

        public void ApplyEffects(float safeZoneRecoveryMultiplier, PlayerContamination player,
            WardStatus wardStatus)
        {
            if (effects != null)
                effects.Apply(1f + GetStageEffect(DayUpgradeType.Mobility, mobilityLevel),
                    1f - GetStageEffect(DayUpgradeType.ContaminationResistance, resistanceLevel),
                    safeZoneRecoveryMultiplier, player);
            if (wardStatus != null)
                wardStatus.SetPersistentStabilityDamageMultiplier(
                    1f - GetStageEffect(DayUpgradeType.FacilityDefense, facilityDefenseLevel));
        }

        public void ResetPurchaseForNewPhase() => purchasedThisClear = false;

        public UpgradeSnapshot CaptureState() =>
            new UpgradeSnapshot(mobilityLevel, facilityDefenseLevel, resistanceLevel, purchasedThisClear);

        public void RestoreState(UpgradeSnapshot snapshot)
        {
            if (snapshot == null) return;
            mobilityLevel = DayUpgradeRules.ClampLevel(snapshot.MobilityLevel);
            facilityDefenseLevel = DayUpgradeRules.ClampLevel(snapshot.FacilityDefenseLevel);
            resistanceLevel = DayUpgradeRules.ClampLevel(snapshot.ResistanceLevel);
            purchasedThisClear = snapshot.PurchasedThisClear;
        }

        private float GetStageEffect(DayUpgradeType type, int level)
        {
            level = DayUpgradeRules.ClampLevel(level);
            if (level == 0) return 0f;
            float value;
            switch (type)
            {
                case DayUpgradeType.Mobility:
                    value = level == 1 ? mobilityStageOneBonus
                        : level == 2 ? mobilityStageTwoBonus : mobilityStageThreeBonus;
                    break;
                case DayUpgradeType.FacilityDefense:
                    value = level == 1 ? facilityDefenseStageOneReduction
                        : level == 2 ? facilityDefenseStageTwoReduction : facilityDefenseStageThreeReduction;
                    break;
                case DayUpgradeType.ContaminationResistance:
                    value = level == 1 ? contaminationResistanceStageOneReduction
                        : level == 2 ? contaminationResistanceStageTwoReduction : contaminationResistanceStageThreeReduction;
                    break;
                default:
                    return 0f;
            }
            return Mathf.Clamp01(value);
        }

        private void SetLevel(DayUpgradeType type, int level)
        {
            level = DayUpgradeRules.ClampLevel(level);
            switch (type)
            {
                case DayUpgradeType.Mobility: mobilityLevel = level; break;
                case DayUpgradeType.FacilityDefense: facilityDefenseLevel = level; break;
                case DayUpgradeType.ContaminationResistance: resistanceLevel = level; break;
            }
        }

        public sealed class UpgradeSnapshot
        {
            public readonly int MobilityLevel;
            public readonly int FacilityDefenseLevel;
            public readonly int ResistanceLevel;
            public readonly bool PurchasedThisClear;

            public UpgradeSnapshot(int mobilityLevel, int facilityDefenseLevel,
                int resistanceLevel, bool purchasedThisClear)
            {
                MobilityLevel = mobilityLevel;
                FacilityDefenseLevel = facilityDefenseLevel;
                ResistanceLevel = resistanceLevel;
                PurchasedThisClear = purchasedThisClear;
            }
        }
    }
}
