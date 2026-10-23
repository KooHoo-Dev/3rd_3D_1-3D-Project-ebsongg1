using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>하루에 하나의 활성 이상현상을 고정 추적하는 화면 방향 핑입니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10009)]
    public sealed class AnomalyScreenPingHUD : MonoBehaviour
    {
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private ZoneIdentity[] zones = new ZoneIdentity[0];
        [SerializeField] private TMP_FontAsset koreanFont;

        private AnomalyBehaviour trackedAnomaly;
        private Camera playerCamera;
        private Canvas canvas;
        private RectTransform canvasRect;
        private GameObject pingRoot;
        private RectTransform markerRect;
        private TMP_Text markerText;
        private TMP_Text infoText;
        private Image infoBackground;
        private bool targetChosenForDay;
        private int observedDay = int.MinValue;
        private int skipTargetAcquisitionThroughFrame;

        public AnomalyBehaviour TrackedAnomaly => trackedAnomaly;
        public bool IsConfigured => dayFlow != null && zones != null && zones.Length == 5;

        private void Awake()
        {
            ResolveReferences();
            BuildUI();
            if (dayFlow != null) observedDay = dayFlow.CurrentDay;
            HidePing();
        }

        private void Update()
        {
            if (dayFlow == null) ResolveReferences();
            if (dayFlow == null || pingRoot == null) return;

            if (observedDay != dayFlow.CurrentDay)
            {
                ResetTrackedTarget();
                observedDay = dayFlow.CurrentDay;
                skipTargetAcquisitionThroughFrame = Time.frameCount + 1;
            }

            if (dayFlow.State != DayState.Playing)
            {
                ResetTrackedTarget();
                HidePing();
                return;
            }

            // 지도와 인벤토리가 열린 동안에는 표시만 숨기며 추적 대상은 유지합니다.
            if (dayFlow.IsMapOpen || dayFlow.InventoryOpen)
            {
                HidePing();
                return;
            }

            if (trackedAnomaly != null && (!trackedAnomaly || trackedAnomaly.IsEnded
                || !trackedAnomaly.isActiveAndEnabled || !trackedAnomaly.IsHazardActive))
                ReleaseTrackedTarget();

            if (trackedAnomaly == null && !targetChosenForDay
                && Time.frameCount > skipTargetAcquisitionThroughFrame)
                TryChooseInitialTarget();

            if (trackedAnomaly == null)
            {
                HidePing();
                return;
            }

            RefreshPing();
        }

        private void OnEnable()
        {
            if (trackedAnomaly != null) trackedAnomaly.Ended += OnTrackedAnomalyEnded;
        }

        public void Configure(DayFlowManager manager, ZoneIdentity[] configuredZones, TMP_FontAsset uiFont)
        {
            dayFlow = manager;
            zones = configuredZones ?? new ZoneIdentity[0];
            if (uiFont != null) koreanFont = uiFont;
        }

        /// <summary>지도 UI에서 추적 대상을 연결할 때 사용할 수 있는 선택 지점입니다.</summary>
        public bool SetTarget(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || anomaly.IsEnded || !anomaly.isActiveAndEnabled || !anomaly.IsHazardActive)
                return false;

            UnsubscribeFromTarget();
            trackedAnomaly = anomaly;
            trackedAnomaly.Ended += OnTrackedAnomalyEnded;
            targetChosenForDay = true;
            return true;
        }

        private void ResolveReferences()
        {
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (zones == null || zones.Length == 0)
                zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
        }

        private void TryChooseInitialTarget()
        {
            AnomalyBehaviour candidate = null;
            float closest = float.PositiveInfinity;
            Vector3 origin = dayFlow.Player != null ? dayFlow.Player.transform.position : transform.position;
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
            {
                if (anomaly == null || anomaly.IsEnded || !anomaly.IsHazardActive) continue;
                float distance = (anomaly.transform.position - origin).sqrMagnitude;
                if (distance >= closest) continue;
                candidate = anomaly;
                closest = distance;
            }

            if (candidate != null) SetTarget(candidate);
        }

        private void RefreshPing()
        {
            if (playerCamera == null || !playerCamera.isActiveAndEnabled)
                playerCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (playerCamera == null)
            {
                HidePing();
                return;
            }

            ZoneIdentity zone = trackedAnomaly.Zone != null ? trackedAnomaly.Zone
                : ZoneIdentity.FindNearest(trackedAnomaly.transform.position, zones);
            string location = zone != null ? zone.DisplayName
                : string.IsNullOrWhiteSpace(trackedAnomaly.SpawnLocationLabel)
                    ? "알 수 없는 구역" : trackedAnomaly.SpawnLocationLabel;
            string anomalyName = trackedAnomaly is ManaRift
                ? "마력 균열" : trackedAnomaly.name.Replace("(Clone)", "").Trim();
            float distance = Vector3.Distance(playerCamera.transform.position, trackedAnomaly.transform.position);
            string distanceText = distance >= 10f ? $"{distance:F0} m" : $"{distance:F1} m";
            infoText.text = $"추적 목표 · {location} · {anomalyName} · 위험 {trackedAnomaly.DangerStage}단계 · 직선거리 {distanceText}";
            markerText.color = DangerColor(trackedAnomaly.DangerStage);

            Vector3 screen = playerCamera.WorldToScreenPoint(trackedAnomaly.transform.position);
            bool inFront = screen.z > playerCamera.nearClipPlane;
            float inset = 42f;
            bool onScreen = inFront && screen.x >= inset && screen.x <= Screen.width - inset
                && screen.y >= inset && screen.y <= Screen.height - inset;

            if (onScreen)
            {
                markerText.text = "◆";
                markerRect.localRotation = Quaternion.identity;
                SetMarkerPosition(new Vector2(screen.x, screen.y));
            }
            else
            {
                markerText.text = "▲";
                Vector2 direction = new Vector2(screen.x - Screen.width * 0.5f,
                    screen.y - Screen.height * 0.5f);
                if (!inFront) direction = -direction;
                if (direction.sqrMagnitude < 0.0001f)
                {
                    Vector3 localTarget = playerCamera.transform.InverseTransformPoint(trackedAnomaly.transform.position);
                    direction = new Vector2(localTarget.x, localTarget.y);
                    if (localTarget.z < 0f) direction = -direction;
                }
                if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;
                direction.Normalize();

                float halfWidth = Mathf.Max(1f, Screen.width * 0.5f - inset);
                float halfHeight = Mathf.Max(1f, Screen.height * 0.5f - inset);
                float xScale = Mathf.Abs(direction.x) > 0.0001f ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
                float yScale = Mathf.Abs(direction.y) > 0.0001f ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;
                float edgeScale = Mathf.Min(xScale, yScale);
                Vector2 edgePoint = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f)
                    + direction * edgeScale;
                SetMarkerPosition(edgePoint);
                markerRect.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            }

            if (!pingRoot.activeSelf) pingRoot.SetActive(true);
        }

        private void BuildUI()
        {
            if (pingRoot != null) return;
            GameObject canvasObject = new GameObject("AnomalyPing_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 50;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;
            canvasRect = canvasObject.GetComponent<RectTransform>();

            pingRoot = new GameObject("AnomalyPing_Overlay", typeof(RectTransform));
            pingRoot.transform.SetParent(canvasObject.transform, false);
            RectTransform rootRect = pingRoot.GetComponent<RectTransform>();
            rootRect.anchorMin = Vector2.zero;
            rootRect.anchorMax = Vector2.one;
            rootRect.offsetMin = Vector2.zero;
            rootRect.offsetMax = Vector2.zero;

            GameObject backgroundObject = new GameObject("Target_Info_Background",
                typeof(RectTransform), typeof(Image));
            backgroundObject.transform.SetParent(pingRoot.transform, false);
            RectTransform backgroundRect = backgroundObject.GetComponent<RectTransform>();
            backgroundRect.anchorMin = backgroundRect.anchorMax = new Vector2(0.5f, 1f);
            backgroundRect.pivot = new Vector2(0.5f, 1f);
            backgroundRect.anchoredPosition = new Vector2(0f, -112f);
            backgroundRect.sizeDelta = new Vector2(1000f, 54f);
            infoBackground = backgroundObject.GetComponent<Image>();
            infoBackground.color = new Color(0.025f, 0.045f, 0.065f, 0.88f);
            infoBackground.raycastTarget = false;

            infoText = CreateText("Target_Info", backgroundObject.transform,
                Vector2.zero, Vector2.one, new Vector2(18f, 0f),
                new Vector2(-18f, 0f), 21f, TextAlignmentOptions.Center, Color.white);
            infoText.textWrappingMode = TextWrappingModes.NoWrap;

            GameObject markerObject = new GameObject("Target_Direction_Marker",
                typeof(RectTransform), typeof(TextMeshProUGUI));
            markerObject.transform.SetParent(pingRoot.transform, false);
            markerRect = markerObject.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.sizeDelta = new Vector2(58f, 58f);
            markerText = markerObject.GetComponent<TMP_Text>();
            markerText.font = koreanFont != null ? koreanFont : TMP_Settings.defaultFontAsset;
            markerText.fontSize = 40f;
            markerText.alignment = TextAlignmentOptions.Center;
            markerText.raycastTarget = false;
            markerText.textWrappingMode = TextWrappingModes.NoWrap;
        }

        private TMP_Text CreateText(string objectName, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float fontSize,
            TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new GameObject(objectName, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.offsetMin = offsetMin;
            rect.offsetMax = offsetMax;
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.font = koreanFont != null ? koreanFont : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            return text;
        }

        private void SetMarkerPosition(Vector2 screenPosition)
        {
            if (canvasRect != null && RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPosition, null, out Vector2 localPosition))
                markerRect.anchoredPosition = localPosition;
        }

        private static Color DangerColor(int stage)
        {
            if (stage >= 3) return new Color(1f, 0.34f, 0.28f);
            if (stage == 2) return new Color(1f, 0.68f, 0.28f);
            return new Color(0.55f, 0.88f, 1f);
        }

        private void OnTrackedAnomalyEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            if (trackedAnomaly != anomaly) return;
            ReleaseTrackedTarget();
        }

        private void ReleaseTrackedTarget()
        {
            UnsubscribeFromTarget();
            trackedAnomaly = null;
            targetChosenForDay = true;
            HidePing();
        }

        private void ResetTrackedTarget()
        {
            UnsubscribeFromTarget();
            trackedAnomaly = null;
            targetChosenForDay = false;
            HidePing();
        }

        private void UnsubscribeFromTarget()
        {
            if (trackedAnomaly != null) trackedAnomaly.Ended -= OnTrackedAnomalyEnded;
        }

        private void HidePing()
        {
            if (pingRoot != null && pingRoot.activeSelf) pingRoot.SetActive(false);
        }

        private void OnDisable()
        {
            UnsubscribeFromTarget();
        }
    }
}
