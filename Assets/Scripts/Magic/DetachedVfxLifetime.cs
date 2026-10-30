using UnityEngine;

namespace WardShift
{
    /// <summary>일시 정지 중에도 분리된 완료 효과를 지정한 시간 뒤 정리합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class DetachedVfxLifetime : MonoBehaviour
    {
        private float remainingSeconds;

        public void Begin(float lifetime)
        {
            remainingSeconds = Mathf.Max(0.1f, lifetime);
        }

        private void Update()
        {
            remainingSeconds -= Time.unscaledDeltaTime;
            if (remainingSeconds <= 0f) Destroy(gameObject);
        }
    }
}
