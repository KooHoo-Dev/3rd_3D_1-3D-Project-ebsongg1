using StarterAssets;
using UnityEngine;

namespace WardShift
{
    /// <summary>순수 강화 수치를 플레이어 컴포넌트에 적용한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayPlayerUpgradeEffects : MonoBehaviour
    {
        private Transform playerRoot;
        private FirstPersonController firstPersonController;
        private ThirdPersonController thirdPersonController;
        private float firstPersonBaseMoveSpeed;
        private float firstPersonBaseSprintSpeed;
        private float thirdPersonBaseMoveSpeed;
        private float thirdPersonBaseSprintSpeed;
        private bool firstPersonSpeedCaptured;
        private bool thirdPersonSpeedCaptured;

        public void Configure(Transform player) => playerRoot = player;

        public void Apply(int mobilityLevel, int resistanceLevel, float safeZoneRecoveryMultiplier,
            PlayerContamination contamination)
        {
            CaptureBaseSpeeds();
            float multiplier = DayUpgradeRules.MovementMultiplier(mobilityLevel);
            if (firstPersonController != null && firstPersonSpeedCaptured)
            {
                firstPersonController.MoveSpeed = firstPersonBaseMoveSpeed * multiplier;
                firstPersonController.SprintSpeed = firstPersonBaseSprintSpeed * multiplier;
            }
            if (thirdPersonController != null && thirdPersonSpeedCaptured)
            {
                thirdPersonController.MoveSpeed = thirdPersonBaseMoveSpeed * multiplier;
                thirdPersonController.SprintSpeed = thirdPersonBaseSprintSpeed * multiplier;
            }
            if (contamination == null) return;
            contamination.SetContaminationGainMultiplier(
                DayUpgradeRules.ContaminationGainMultiplier(resistanceLevel));
            contamination.SetSafeZoneRecoveryMultiplier(safeZoneRecoveryMultiplier);
        }

        public void CaptureBaseSpeeds()
        {
            if (playerRoot == null) return;
            if (firstPersonController == null)
                firstPersonController = playerRoot.GetComponentInChildren<FirstPersonController>(true);
            if (thirdPersonController == null)
                thirdPersonController = playerRoot.GetComponentInChildren<ThirdPersonController>(true);
            if (firstPersonController != null && !firstPersonSpeedCaptured)
            {
                firstPersonBaseMoveSpeed = firstPersonController.MoveSpeed;
                firstPersonBaseSprintSpeed = firstPersonController.SprintSpeed;
                firstPersonSpeedCaptured = true;
            }
            if (thirdPersonController != null && !thirdPersonSpeedCaptured)
            {
                thirdPersonBaseMoveSpeed = thirdPersonController.MoveSpeed;
                thirdPersonBaseSprintSpeed = thirdPersonController.SprintSpeed;
                thirdPersonSpeedCaptured = true;
            }
        }
    }
}
