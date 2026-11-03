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
        private float firstPersonBaseSprintSpeed;
        private float thirdPersonBaseMoveSpeed;
        private float thirdPersonBaseSprintSpeed;
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
            PlayerContamination contamination)
        {
            CaptureBaseSpeeds();
            if (firstPersonController != null && firstPersonSpeedCaptured)
            {
                firstPersonController.MoveSpeed = firstPersonBaseMoveSpeed * Mathf.Max(0f, movementMultiplier);
                firstPersonController.SprintSpeed = firstPersonBaseSprintSpeed * Mathf.Max(0f, movementMultiplier);
            }
            if (thirdPersonController != null && thirdPersonSpeedCaptured)
            {
                thirdPersonController.MoveSpeed = thirdPersonBaseMoveSpeed * Mathf.Max(0f, movementMultiplier);
                thirdPersonController.SprintSpeed = thirdPersonBaseSprintSpeed * Mathf.Max(0f, movementMultiplier);
            }
            if (contamination == null) return;
            contamination.SetContaminationGainMultiplier(contaminationGainMultiplier);
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

        private void OnDisable() => RestoreBaseSpeeds();

        private void OnDestroy() => RestoreBaseSpeeds();

        private void RestoreBaseSpeeds()
        {
            if (firstPersonController != null && firstPersonSpeedCaptured)
            {
                firstPersonController.MoveSpeed = firstPersonBaseMoveSpeed;
                firstPersonController.SprintSpeed = firstPersonBaseSprintSpeed;
            }
            if (thirdPersonController != null && thirdPersonSpeedCaptured)
            {
                thirdPersonController.MoveSpeed = thirdPersonBaseMoveSpeed;
                thirdPersonController.SprintSpeed = thirdPersonBaseSprintSpeed;
            }
        }
    }
}
