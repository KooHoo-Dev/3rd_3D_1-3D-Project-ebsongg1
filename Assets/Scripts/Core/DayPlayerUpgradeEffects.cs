using StarterAssets;
using UnityEngine;

namespace WardShift
{
    /// <summary>영구 강화 배수를 플레이어 이동과 오염 획득에 적용한다.</summary>
    [DisallowMultipleComponent]
    public sealed class DayPlayerUpgradeEffects : MonoBehaviour
    {
        private Transform playerRoot;
        private FirstPersonController firstPersonController;
        private ThirdPersonController thirdPersonController;
        private float firstPersonBaseMoveSpeed;
        private float thirdPersonBaseMoveSpeed;
        private bool firstPersonSpeedCaptured;
        private bool thirdPersonSpeedCaptured;

        public void Configure(Transform player)
        {
            RestoreBaseSpeeds();
            if (playerRoot != player)
            {
                firstPersonController = null;
                thirdPersonController = null;
                firstPersonSpeedCaptured = false;
                thirdPersonSpeedCaptured = false;
            }
            playerRoot = player;
        }

        public void Apply(float movementMultiplier, float contaminationGainMultiplier,
            float safeZoneRecoveryMultiplier, PlayerContamination contamination)
        {
            CaptureBaseSpeeds();
            if (firstPersonController != null && firstPersonSpeedCaptured)
                firstPersonController.MoveSpeed = firstPersonBaseMoveSpeed * Mathf.Max(0f, movementMultiplier);
            if (thirdPersonController != null && thirdPersonSpeedCaptured)
                thirdPersonController.MoveSpeed = thirdPersonBaseMoveSpeed * Mathf.Max(0f, movementMultiplier);
            if (contamination == null) return;
            contamination.SetContaminationGainMultiplier(contaminationGainMultiplier);
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
                firstPersonSpeedCaptured = true;
            }
            if (thirdPersonController != null && !thirdPersonSpeedCaptured)
            {
                thirdPersonBaseMoveSpeed = thirdPersonController.MoveSpeed;
                thirdPersonSpeedCaptured = true;
            }
        }

        private void OnDisable() => RestoreBaseSpeeds();

        private void OnDestroy() => RestoreBaseSpeeds();

        private void RestoreBaseSpeeds()
        {
            if (firstPersonController != null && firstPersonSpeedCaptured)
                firstPersonController.MoveSpeed = firstPersonBaseMoveSpeed;
            if (thirdPersonController != null && thirdPersonSpeedCaptured)
                thirdPersonController.MoveSpeed = thirdPersonBaseMoveSpeed;
        }
    }
}
