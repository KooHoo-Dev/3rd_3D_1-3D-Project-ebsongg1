using StarterAssets;
using UnityEngine;

namespace WardShift
{
    /// <summary>실제 장치 피격의 이동·화면 반응. 오염도 판정에는 관여하지 않습니다.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(CharacterController))]
    [DefaultExecutionOrder(-100)]
    public sealed class DeviceHitReaction : MonoBehaviour
    {
        private CharacterController characterController;
        private FirstPersonController firstPerson;
        private Transform cameraTarget;
        private Vector3 appliedShakeOffset;
        private Vector3 knockbackDirection;
        private float knockbackDistance;
        private float knockbackDuration;
        private float knockbackElapsed;
        private float shakeIntensity;
        private float shakeDuration;
        private float shakeElapsed;

        private void Awake()
        {
            characterController = GetComponent<CharacterController>();
            firstPerson = GetComponent<FirstPersonController>();
            if (firstPerson != null && firstPerson.CinemachineCameraTarget != null)
                cameraTarget = firstPerson.CinemachineCameraTarget.transform;
        }

        public void Play(Vector3 impactOrigin, float distance, float duration,
            float intensity, float cameraDuration)
        {
            Vector3 away = transform.position - impactOrigin;
            away.y = 0f;
            knockbackDirection = away.sqrMagnitude > 0.0001f
                ? away.normalized : -transform.forward;
            knockbackDirection.y = 0f;
            knockbackDirection.Normalize();
            knockbackDistance = Mathf.Max(0f, distance);
            knockbackDuration = Mathf.Max(0.01f, duration);
            knockbackElapsed = 0f;
            shakeIntensity = Mathf.Max(0f, intensity);
            shakeDuration = Mathf.Max(0.01f, cameraDuration);
            shakeElapsed = 0f;
        }

        private void Update()
        {
            // Starter Assets의 이동 Update 전에 외부 이동량을 전달합니다.
            // 최종 이동은 기존 CharacterController.Move 한 번으로 처리됩니다.
            if (knockbackElapsed >= knockbackDuration || knockbackDistance <= 0f) return;
            if (Time.timeScale <= 0f || characterController == null || !characterController.enabled
                || firstPerson != null && !firstPerson.isActiveAndEnabled)
            {
                knockbackElapsed = knockbackDuration;
                if (firstPerson != null) firstPerson.ClearExternalDisplacement();
                return;
            }

            float previous = Mathf.Clamp01(knockbackElapsed / knockbackDuration);
            knockbackElapsed = Mathf.Min(knockbackDuration, knockbackElapsed + Time.deltaTime);
            float current = Mathf.Clamp01(knockbackElapsed / knockbackDuration);
            // 시작은 빠르고 끝은 부드러운 이동. 충돌로 막힌 거리는 나중에 보충하지 않습니다.
            float portion = (1f - (1f - current) * (1f - current))
                - (1f - (1f - previous) * (1f - previous));
            Vector3 displacement = knockbackDirection * (knockbackDistance * portion);
            if (firstPerson != null) firstPerson.QueueExternalDisplacement(displacement);
            else characterController.Move(displacement);
        }

        private void LateUpdate()
        {
            if (cameraTarget == null) return;
            cameraTarget.localPosition -= appliedShakeOffset;
            appliedShakeOffset = Vector3.zero;
            if (shakeElapsed >= shakeDuration || shakeIntensity <= 0f) return;

            shakeElapsed = Mathf.Min(shakeDuration, shakeElapsed + Time.unscaledDeltaTime);
            float fade = 1f - shakeElapsed / shakeDuration;
            float phase = Time.unscaledTime;
            appliedShakeOffset = new Vector3(Mathf.Sin(phase * 82f),
                Mathf.Sin(phase * 113f), 0f) * (shakeIntensity * fade);
            cameraTarget.localPosition += appliedShakeOffset;
        }

        private void OnDisable()
        {
            if (cameraTarget != null) cameraTarget.localPosition -= appliedShakeOffset;
            appliedShakeOffset = Vector3.zero;
            knockbackElapsed = knockbackDuration;
            shakeElapsed = shakeDuration;
            if (firstPerson != null) firstPerson.ClearExternalDisplacement();
        }
    }
}
