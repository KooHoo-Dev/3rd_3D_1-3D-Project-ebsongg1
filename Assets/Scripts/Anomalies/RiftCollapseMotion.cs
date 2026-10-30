using UnityEngine;

namespace WardShift
{
    /// <summary>균열 완료 시 분리한 구체와 지속 VFX를 같은 중심으로 수축합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class RiftCollapseMotion : MonoBehaviour
    {
        private Transform[] visuals;
        private Vector3[] startPositions;
        private Vector3[] startScales;
        private float duration;
        private float finalScale;
        private float elapsed;

        public void Begin(Transform[] detachedVisuals, float collapseDuration, float collapsedScale)
        {
            visuals = detachedVisuals;
            duration = Mathf.Max(0.01f, collapseDuration);
            finalScale = Mathf.Clamp(collapsedScale, 0f, 1f);
            startPositions = new Vector3[visuals != null ? visuals.Length : 0];
            startScales = new Vector3[startPositions.Length];
            for (int i = 0; i < startPositions.Length; i++)
            {
                if (visuals[i] == null) continue;
                startPositions[i] = visuals[i].localPosition;
                startScales[i] = visuals[i].localScale;
            }
        }

        private void Update()
        {
            if (visuals == null || visuals.Length == 0)
            {
                Destroy(gameObject);
                return;
            }

            elapsed += Time.unscaledDeltaTime;
            float progress = Mathf.Clamp01(elapsed / duration);
            float eased = 1f - Mathf.Pow(1f - progress, 3f);
            for (int i = 0; i < visuals.Length; i++)
            {
                Transform visual = visuals[i];
                if (visual == null) continue;
                visual.localPosition = Vector3.Lerp(startPositions[i], Vector3.zero, eased);
                visual.localScale = Vector3.Lerp(startScales[i], startScales[i] * finalScale, eased);
            }

            if (progress >= 1f) Destroy(gameObject);
        }
    }
}
