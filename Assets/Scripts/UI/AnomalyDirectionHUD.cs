using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>Q 감응 중 활성 이상현상 하나의 방향만 기존 HUD에 표시합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10012)]
    public sealed class AnomalyDirectionHUD : MonoBehaviour
    {
        [Header("감응 참조")]
        [SerializeField] private MagicDetector detector;
        [SerializeField] private Camera senseCamera;
        [SerializeField] private WardShiftHUDLayout hudLayout;
        [SerializeField] private DayFlowManager dayFlow;

        [Header("방향 화살표")]
        [SerializeField] private Color arrowColor = new Color(0.32f, 0.9f, 1f, 1f);
        [SerializeField] private Color glowColor = new Color(0.12f, 0.72f, 1f, 0.22f);
        [SerializeField, Min(12f)] private float arrowSize = 44f;
        [Tooltip("화살표 중심과 조준점 사이의 거리입니다. 조준점 주변에서 크기와 함께 조절하세요.")]
        [SerializeField, Min(12f)] private float arrowDistanceFromCenter = 82f;
        [Tooltip("전방/후방 방향을 상하 화살표 위치에 반영하는 가중치입니다.")]
        [SerializeField, Min(0f)] private float frontBackBias = 1.1f;

        [Header("감지 및 안내")]
        [Tooltip("주변 대상이 없을 때 목록을 다시 확인하는 간격입니다. Q 감지 거리 자체는 MagicDetector 값을 사용합니다.")]
        [SerializeField, Min(0.02f)] private float targetSearchInterval = 0.12f;
        [SerializeField, Min(0f)] private float noResponseDuration = 1.1f;
        [SerializeField] private Color noResponseColor = new Color(0.62f, 0.92f, 1f, 1f);
        [SerializeField, Min(12f)] private float noResponseFontSize = 22f;
        [SerializeField] private Vector2 noResponseOffsetFromCenter = new Vector2(0f, -96f);

        private RectTransform indicatorRoot;
        private RectTransform arrowPivotRect;
        private RectTransform arrowRect;
        private GameObject arrowPivotObject;
        private TMP_Text noResponseText;
        private Sprite arrowSprite;
        private Texture2D arrowTexture;
        private AnomalyBehaviour selectedAnomaly;
        private bool hasSelectedAnomaly;
        private bool wasEligible;
        private bool noResponseScheduled;
        private float noResponseUntil;
        private float nextSearchTime;

        private void Awake()
        {
            if (hudLayout == null) hudLayout = GetComponent<WardShiftHUDLayout>();
            if (hudLayout == null) hudLayout = FindFirstObjectByType<WardShiftHUDLayout>();
            if (detector == null) detector = FindFirstObjectByType<MagicDetector>();
            if (senseCamera == null && detector != null) senseCamera = detector.SenseCamera;
            if (senseCamera == null) senseCamera = Camera.main;
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();

            if (hudLayout == null || hudLayout.HUDRoot == null || detector == null || senseCamera == null)
            {
                Debug.LogError("AnomalyDirectionHUD: 기존 HUD, MagicDetector, 감지 카메라를 연결하세요.", this);
                enabled = false;
                return;
            }

            BuildUI(hudLayout.HUDRoot);
        }

        private void LateUpdate()
        {
            bool eligible = detector != null && detector.IsSensing
                && senseCamera != null && senseCamera.isActiveAndEnabled
                && Time.timeScale > 0f
                && (dayFlow == null || dayFlow.State == DayState.Playing)
                && hudLayout != null && hudLayout.HUDRoot != null
                && hudLayout.HUDRoot.gameObject.activeInHierarchy;

            if (!eligible)
            {
                if (indicatorRoot != null && indicatorRoot.gameObject.activeSelf)
                    indicatorRoot.gameObject.SetActive(false);
                ResetTracking();
                wasEligible = false;
                return;
            }

            if (!wasEligible)
            {
                ResetTracking();
                nextSearchTime = 0f;
                wasEligible = true;
            }

            UpdateSelectedAnomaly();
            bool showNoResponse = !hasSelectedAnomaly && noResponseScheduled
                && Time.unscaledTime < noResponseUntil;
            bool visible = hasSelectedAnomaly || showNoResponse;
            if (indicatorRoot.gameObject.activeSelf != visible)
                indicatorRoot.gameObject.SetActive(visible);
            if (!visible) return;

            arrowPivotObject.SetActive(hasSelectedAnomaly);
            noResponseText.gameObject.SetActive(showNoResponse);
            if (hasSelectedAnomaly) UpdateArrowDirection();
        }

        private void UpdateSelectedAnomaly()
        {
            float now = Time.unscaledTime;
            float radius = detector.DetectionRadius;
            bool lostTarget = false;

            if (hasSelectedAnomaly && !IsTrackable(selectedAnomaly, radius))
            {
                selectedAnomaly = null;
                hasSelectedAnomaly = false;
                lostTarget = true;
                noResponseScheduled = false;
            }

            if (hasSelectedAnomaly || (!lostTarget && now < nextSearchTime)) return;

            selectedAnomaly = FindNearestActiveAnomaly(radius);
            hasSelectedAnomaly = selectedAnomaly != null;
            nextSearchTime = now + Mathf.Max(0.02f, targetSearchInterval);

            if (hasSelectedAnomaly)
            {
                noResponseScheduled = false;
                return;
            }

            if (!noResponseScheduled)
            {
                noResponseUntil = now + Mathf.Max(0f, noResponseDuration);
                noResponseScheduled = true;
            }
        }

        private AnomalyBehaviour FindNearestActiveAnomaly(float radius)
        {
            AnomalyBehaviour nearest = null;
            float nearestDistanceSqr = radius * radius;
            Vector3 origin = senseCamera.transform.position;
            var anomalies = AnomalyBehaviour.ActiveAnomalies;
            for (int i = 0; i < anomalies.Count; i++)
            {
                AnomalyBehaviour anomaly = anomalies[i];
                if (!IsSupportedAndActive(anomaly)) continue;
                float distanceSqr = (anomaly.transform.position - origin).sqrMagnitude;
                if (distanceSqr > nearestDistanceSqr
                    || (nearest != null && distanceSqr >= nearestDistanceSqr)) continue;
                nearest = anomaly;
                nearestDistanceSqr = distanceSqr;
            }
            return nearest;
        }

        private bool IsTrackable(AnomalyBehaviour anomaly, float radius)
        {
            if (!IsSupportedAndActive(anomaly)) return false;
            Vector3 offset = anomaly.transform.position - senseCamera.transform.position;
            return offset.sqrMagnitude <= radius * radius;
        }

        private static bool IsSupportedAndActive(AnomalyBehaviour anomaly)
        {
            return anomaly != null && anomaly.isActiveAndEnabled && anomaly.gameObject.activeInHierarchy
                && !anomaly.IsEnded && anomaly.IsHazardActive
                && (anomaly is ManaRift || anomaly is CurseAnomaly || anomaly is ManaDeviceAnomaly);
        }

        private void UpdateArrowDirection()
        {
            if (!IsTrackable(selectedAnomaly, detector.DetectionRadius))
            {
                UpdateSelectedAnomaly();
                if (!hasSelectedAnomaly) return;
            }

            Vector3 local = senseCamera.transform.InverseTransformPoint(selectedAnomaly.transform.position);
            float depthRatio = local.z / Mathf.Max(0.001f, local.magnitude);
            Vector2 direction = new Vector2(local.x, local.y + depthRatio * frontBackBias);
            if (direction.sqrMagnitude < 0.0001f)
                direction = local.z >= 0f ? Vector2.up : Vector2.down;
            direction.Normalize();

            arrowPivotRect.anchoredPosition = direction * arrowDistanceFromCenter;
            arrowPivotRect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
        }

        private void BuildUI(RectTransform parent)
        {
            var rootObject = new GameObject("AnomalyDirectionIndicator", typeof(RectTransform));
            rootObject.transform.SetParent(parent, false);
            indicatorRoot = rootObject.GetComponent<RectTransform>();
            indicatorRoot.anchorMin = Vector2.zero;
            indicatorRoot.anchorMax = Vector2.one;
            indicatorRoot.offsetMin = Vector2.zero;
            indicatorRoot.offsetMax = Vector2.zero;
            indicatorRoot.SetAsLastSibling();

            arrowTexture = CreateArrowTexture();
            arrowSprite = Sprite.Create(arrowTexture, new Rect(0f, 0f, arrowTexture.width, arrowTexture.height),
                new Vector2(0.5f, 0.5f), arrowTexture.width);
            arrowSprite.name = "AnomalyDirectionArrow";

            arrowPivotObject = new GameObject("ArrowPivot", typeof(RectTransform));
            arrowPivotObject.transform.SetParent(indicatorRoot, false);
            arrowPivotRect = arrowPivotObject.GetComponent<RectTransform>();
            arrowPivotRect.anchorMin = arrowPivotRect.anchorMax = new Vector2(0.5f, 0.5f);
            arrowPivotRect.pivot = new Vector2(0.5f, 0.5f);

            GameObject glowObject = CreateCenteredImage("Arrow_Glow", arrowPivotRect, arrowSprite);
            Image glowImage = glowObject.GetComponent<Image>();
            glowImage.color = glowColor;
            glowImage.raycastTarget = false;
            RectTransform glowRect = glowObject.GetComponent<RectTransform>();
            glowRect.sizeDelta = Vector2.one * (arrowSize * 1.75f);

            GameObject arrowObject = CreateCenteredImage("Arrow", arrowPivotRect, arrowSprite);
            arrowRect = arrowObject.GetComponent<RectTransform>();
            arrowRect.sizeDelta = Vector2.one * arrowSize;
            Image arrowImage = arrowObject.GetComponent<Image>();
            arrowImage.color = arrowColor;
            arrowImage.raycastTarget = false;
            arrowObject.transform.SetAsLastSibling();
            arrowPivotObject.SetActive(false);

            GameObject textObject = new GameObject("NoNearbyAnomaly", typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(indicatorRoot, false);
            RectTransform textRect = textObject.GetComponent<RectTransform>();
            textRect.anchorMin = textRect.anchorMax = new Vector2(0.5f, 0.5f);
            textRect.pivot = new Vector2(0.5f, 0.5f);
            textRect.anchoredPosition = noResponseOffsetFromCenter;
            textRect.sizeDelta = new Vector2(460f, 42f);
            noResponseText = textObject.GetComponent<TMP_Text>();
            noResponseText.font = FindHudFont(parent);
            noResponseText.fontSize = noResponseFontSize;
            noResponseText.color = noResponseColor;
            noResponseText.alignment = TextAlignmentOptions.Center;
            noResponseText.textWrappingMode = TextWrappingModes.NoWrap;
            noResponseText.raycastTarget = false;
            noResponseText.text = "근처 마력 반응 없음";

            rootObject.SetActive(false);
        }

        private static GameObject CreateCenteredImage(string objectName, Transform parent, Sprite sprite)
        {
            var imageObject = new GameObject(objectName, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = rect.anchorMax = new Vector2(0.5f, 0.5f);
            rect.pivot = new Vector2(0.5f, 0.5f);
            rect.sizeDelta = Vector2.one * 44f;
            Image image = imageObject.GetComponent<Image>();
            image.sprite = sprite;
            image.preserveAspect = true;
            image.raycastTarget = false;
            return imageObject;
        }

        private static TMP_FontAsset FindHudFont(RectTransform parent)
        {
            TMP_Text[] texts = parent.GetComponentsInChildren<TMP_Text>(true);
            foreach (TMP_Text text in texts)
                if (text != null && text.font != null) return text.font;
            return TMP_Settings.defaultFontAsset;
        }

        private static Texture2D CreateArrowTexture()
        {
            const int size = 64;
            const int samples = 4;
            Vector2[] polygon =
            {
                new Vector2(0f, 0.94f), new Vector2(-0.64f, 0.08f),
                new Vector2(-0.22f, 0.08f), new Vector2(-0.22f, -0.9f),
                new Vector2(0.22f, -0.9f), new Vector2(0.22f, 0.08f),
                new Vector2(0.64f, 0.08f)
            };
            var texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = "AnomalyDirectionArrow_Texture",
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp,
                hideFlags = HideFlags.DontSave
            };
            var pixels = new Color32[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    int covered = 0;
                    for (int sy = 0; sy < samples; sy++)
                    for (int sx = 0; sx < samples; sx++)
                    {
                        Vector2 point = new Vector2(
                            ((x + (sx + 0.5f) / samples) / size) * 2f - 1f,
                            ((y + (sy + 0.5f) / samples) / size) * 2f - 1f);
                        if (PointInPolygon(point, polygon)) covered++;
                    }
                    byte alpha = (byte)(covered * 255 / (samples * samples));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }
            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            return texture;
        }

        private static bool PointInPolygon(Vector2 point, Vector2[] polygon)
        {
            bool inside = false;
            for (int i = 0, j = polygon.Length - 1; i < polygon.Length; j = i++)
            {
                Vector2 a = polygon[i];
                Vector2 b = polygon[j];
                if ((a.y > point.y) == (b.y > point.y)) continue;
                float crossingX = (b.x - a.x) * (point.y - a.y) / (b.y - a.y) + a.x;
                if (point.x < crossingX) inside = !inside;
            }
            return inside;
        }

        private void ResetTracking()
        {
            selectedAnomaly = null;
            hasSelectedAnomaly = false;
            noResponseScheduled = false;
            noResponseUntil = 0f;
            nextSearchTime = 0f;
        }

        private void OnDisable()
        {
            ResetTracking();
            wasEligible = false;
            if (indicatorRoot != null) indicatorRoot.gameObject.SetActive(false);
        }

        private void OnDestroy()
        {
            if (arrowSprite != null) Destroy(arrowSprite);
            if (arrowTexture != null) Destroy(arrowTexture);
        }
    }
}
