using UnityEngine;

namespace WardShift
{
    /// <summary>강화 단계, 구매 가능 여부와 플레이어 효과 적용을 관리한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayUpgradeController : MonoBehaviour
    {
        private PlayerInventory inventory;
        private DayPlayerUpgradeEffects effects;
        private int mobilityLevel;
        private int resistanceLevel;
        private int purificationLevel;
        private bool purchasedThisClear;

        public int MobilityLevel => mobilityLevel;
        public int ResistanceLevel => resistanceLevel;
        public int PurificationLevel => purificationLevel;
        public bool PurchasedThisClear => purchasedThisClear;

        public void Configure(PlayerInventory playerInventory, DayPlayerUpgradeEffects playerEffects,
            int initialMobility, int initialResistance, int initialPurification)
        {
            inventory = playerInventory;
            effects = playerEffects;
            mobilityLevel = DayUpgradeRules.ClampLevel(initialMobility);
            resistanceLevel = DayUpgradeRules.ClampLevel(initialResistance);
            purificationLevel = DayUpgradeRules.ClampLevel(initialPurification);
        }

        public int GetLevel(DayUpgradeType type)
        {
            switch (type)
            {
                case DayUpgradeType.Mobility: return mobilityLevel;
                case DayUpgradeType.ContaminationResistance: return resistanceLevel;
                case DayUpgradeType.FieldPurification: return purificationLevel;
                default: return 0;
            }
        }

        public int GetNextPrice(DayUpgradeType type, int[] stagePrices)
        {
            return DayUpgradeRules.NextPrice(GetLevel(type), stagePrices);
        }

        public static string GetEffectText(DayUpgradeType type, int level, FloodCondition condition)
        {
            level = DayUpgradeRules.ClampLevel(level);
            switch (type)
            {
                case DayUpgradeType.Mobility:
                    return level == 0 ? "기본 이동속도" : $"이동속도 +{level * 10}%";
                case DayUpgradeType.ContaminationResistance:
                    return level == 0 ? "기본 오염 획득량" : $"오염도 획득량 -{(level == 1 ? 20 : 35)}%";
                case DayUpgradeType.FieldPurification:
                    float amount = FloodConditionRules.PotionRecoveryAmount(
                        DayUpgradeRules.PotionRecovery(level, PlayerInventory.PotionRecovery), condition);
                    return $"정화약 {amount:F0} 회복";
                default:
                    return string.Empty;
            }
        }

        public bool CanPurchase(DayUpgradeType type, DayState state, int[] stagePrices)
        {
            return state == DayState.DayClear && !purchasedThisClear
                && GetLevel(type) < 2 && inventory != null
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

        public float PotionRecoveryAmount(FloodCondition condition)
        {
            return FloodConditionRules.PotionRecoveryAmount(
                DayUpgradeRules.PotionRecovery(purificationLevel, PlayerInventory.PotionRecovery), condition);
        }

        public void ApplyEffects(float safeZoneRecoveryMultiplier, PlayerContamination player)
        {
            if (effects != null)
                effects.Apply(mobilityLevel, resistanceLevel, safeZoneRecoveryMultiplier, player);
        }

        public void ResetPurchaseForNewPhase() => purchasedThisClear = false;

        public UpgradeSnapshot CaptureState() =>
            new UpgradeSnapshot(mobilityLevel, resistanceLevel, purificationLevel, purchasedThisClear);

        public void RestoreState(UpgradeSnapshot snapshot)
        {
            if (snapshot == null) return;
            mobilityLevel = DayUpgradeRules.ClampLevel(snapshot.MobilityLevel);
            resistanceLevel = DayUpgradeRules.ClampLevel(snapshot.ResistanceLevel);
            purificationLevel = DayUpgradeRules.ClampLevel(snapshot.PurificationLevel);
            purchasedThisClear = snapshot.PurchasedThisClear;
        }

        private void SetLevel(DayUpgradeType type, int level)
        {
            level = DayUpgradeRules.ClampLevel(level);
            switch (type)
            {
                case DayUpgradeType.Mobility: mobilityLevel = level; break;
                case DayUpgradeType.ContaminationResistance: resistanceLevel = level; break;
                case DayUpgradeType.FieldPurification: purificationLevel = level; break;
            }
        }

        public sealed class UpgradeSnapshot
        {
            public readonly int MobilityLevel;
            public readonly int ResistanceLevel;
            public readonly int PurificationLevel;
            public readonly bool PurchasedThisClear;

            public UpgradeSnapshot(int mobilityLevel, int resistanceLevel,
                int purificationLevel, bool purchasedThisClear)
            {
                MobilityLevel = mobilityLevel;
                ResistanceLevel = resistanceLevel;
                PurificationLevel = purificationLevel;
                PurchasedThisClear = purchasedThisClear;
            }
        }
    }
}
