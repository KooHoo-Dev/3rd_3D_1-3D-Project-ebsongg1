using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>실제 오염 증가를 화면과 소리로 표시합니다. 공격 판정과 무관합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ContaminationHitFeedback : MonoBehaviour
    {
        private readonly Image[] edges = new Image[4];
        private readonly Color flashColor = new Color(0.7f, 0.16f, 0.85f, 0.38f);
        private PlayerContamination player;
        private WardStatusHUD statusHUD;
        private AudioSource audioSource;
        private AudioClip hitSfx;
        private float flashUntil;
        private float pendingExposureAmount;
        private float nextExposureFeedbackAt;
        private float lastExposureAt;
        private const float FlashSeconds = 0.25f;
        private const float ExposureFeedbackInterval = 0.75f;

        public void Configure(WardShiftHUDLayout layout, AudioClip clip)
        {
            hitSfx = clip;
            if (layout == null || layout.HUDRoot == null) return;
            PlayerContamination next = FindFirstObjectByType<PlayerContamination>();
            if (player != next)
            {
                if (player != null)
                {
                    player.DirectDamageApplied -= OnDamage;
                    player.ExposureContaminationApplied -= OnExposure;
                }
                player = next;
                if (player != null)
                {
                    player.DirectDamageApplied += OnDamage;
                    player.ExposureContaminationApplied += OnExposure;
                }
            }
            statusHUD = GetComponentInChildren<WardStatusHUD>(true);
            if (edges[0] == null)
            {
                RectTransform root = layout.HUDRoot;
                edges[0] = MakeEdge(root, "HitFlashTop", new Vector2(0, 1),
                    new Vector2(1, 1), new Vector2(0, -40), Vector2.zero);
                edges[1] = MakeEdge(root, "HitFlashBottom", new Vector2(0, 0),
                    new Vector2(1, 0), Vector2.zero, new Vector2(0, 40));
                edges[2] = MakeEdge(root, "HitFlashLeft", new Vector2(0, 0),
                    new Vector2(0, 1), Vector2.zero, new Vector2(40, 0));
                edges[3] = MakeEdge(root, "HitFlashRight", new Vector2(1, 0),
                    new Vector2(1, 1), new Vector2(-40, 0), Vector2.zero);
            }
            if (audioSource == null)
            {
                audioSource = GetComponent<AudioSource>();
                if (audioSource == null) audioSource = gameObject.AddComponent<AudioSource>();
                audioSource.playOnAwake = false;
            }
        }

        private void OnEnable()
        {
            WardShiftHUDLayout layout = GetComponent<WardShiftHUDLayout>();
            if (layout != null) Configure(layout, hitSfx);
        }

        private static Image MakeEdge(RectTransform parent, string name,
            Vector2 min, Vector2 max, Vector2 offsetMin, Vector2 offsetMax)
        {
            GameObject edge = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            RectTransform rect = (RectTransform)edge.transform;
            rect.SetParent(parent, false);
            rect.anchorMin = min;
            rect.anchorMax = max;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            Image image = edge.GetComponent<Image>();
            image.raycastTarget = false;
            image.color = Color.clear;
            return image;
        }

        private void OnDamage(float amount)
            => PlayFeedback(amount, true);

        private void OnExposure(float amount)
        {
            float now = Time.unscaledTime;
            if (now - lastExposureAt > 0.3f)
            {
                pendingExposureAmount = 0f;
                nextExposureFeedbackAt = now;
            }
            lastExposureAt = now;
            pendingExposureAmount += amount;
            if (now < nextExposureFeedbackAt) return;
            nextExposureFeedbackAt = now + ExposureFeedbackInterval;
            PlayFeedback(pendingExposureAmount, false);
            pendingExposureAmount = 0f;
        }

        private void PlayFeedback(float amount, bool playSound)
        {
            flashUntil = Time.unscaledTime + FlashSeconds;
            if (statusHUD != null) statusHUD.ShowContaminationIncrease(amount);
            if (playSound && hitSfx != null && audioSource != null)
                audioSource.PlayOneShot(hitSfx);
        }

        private void LateUpdate()
        {
            if (edges[0] == null) return;
            float alpha = Mathf.Clamp01((flashUntil - Time.unscaledTime) / FlashSeconds);
            Color color = flashColor;
            color.a *= alpha;
            foreach (Image edge in edges)
                if (edge != null) edge.color = color;
        }

        private void OnDisable()
        {
            if (player != null)
            {
                player.DirectDamageApplied -= OnDamage;
                player.ExposureContaminationApplied -= OnExposure;
            }
            player = null;
            pendingExposureAmount = 0f;
        }
    }
}
