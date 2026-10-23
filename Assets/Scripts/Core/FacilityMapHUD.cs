using System.Collections.Generic;
using System.Text;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>현재 시설 구역과 활성 이상현상을 고정형 2D 배치도로 표시합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10008)]
    public sealed class FacilityMapHUD : MonoBehaviour
    {
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private FacilityZoneTracker zoneTracker;
        [SerializeField] private ZoneIdentity[] zones = new ZoneIdentity[0];
        [SerializeField] private TMP_FontAsset koreanFont;

        private readonly Dictionary<FacilityZoneId, TMP_Text> roomLabels =
            new Dictionary<FacilityZoneId, TMP_Text>();
        private readonly Dictionary<FacilityZoneId, Image> roomPanels =
            new Dictionary<FacilityZoneId, Image>();
        private GameObject overlayRoot;
        private TMP_Text currentLocationText;
        private RectTransform mapArea;
        private readonly List<Image> corridorImages = new List<Image>();
        private readonly ActiveAnomalyZoneSnapshot activeAnomalies = new ActiveAnomalyZoneSnapshot();

        private static readonly Color PanelColor = new Color(0.045f, 0.07f, 0.11f, 0.98f);
        private static readonly Color RoomColor = new Color(0.10f, 0.15f, 0.21f, 0.98f);
        private static readonly Color CurrentRoomColor = new Color(0.08f, 0.28f, 0.34f, 1f);
        private static readonly Color CorridorColor = new Color(0.20f, 0.30f, 0.36f, 1f);

        public bool IsMapOpen => dayFlow != null && dayFlow.IsMapOpen;
        public bool IsConfigured => dayFlow != null && zoneTracker != null && zones != null && zones.Length == 5;

        private void Awake()
        {
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (zoneTracker == null && dayFlow != null) zoneTracker = dayFlow.ZoneTracker;
            if (zoneTracker == null) zoneTracker = FindFirstObjectByType<FacilityZoneTracker>();
            if (zones == null || zones.Length == 0)
                zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
            BuildUI();
            RefreshMap();
        }

        private void Update()
        {
            bool visible = dayFlow != null && dayFlow.State == DayState.Playing && dayFlow.IsMapOpen;
            if (overlayRoot != null && overlayRoot.activeSelf != visible)
                overlayRoot.SetActive(visible);
            if (visible) RefreshMap();
        }

        public void Configure(DayFlowManager manager, FacilityZoneTracker tracker,
            ZoneIdentity[] configuredZones, TMP_FontAsset uiFont)
        {
            dayFlow = manager;
            zoneTracker = tracker;
            zones = configuredZones ?? new ZoneIdentity[0];
            if (uiFont != null) koreanFont = uiFont;
        }

        public bool HasVisitedZone(FacilityZoneId zone)
        {
            return zoneTracker != null && zoneTracker.HasVisited(zone);
        }

        private void BuildUI()
        {
            GameObject canvasObject = new GameObject("FacilityMap_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.ScreenSpaceOverlay;
            canvas.sortingOrder = 60;
            CanvasScaler scaler = canvasObject.GetComponent<CanvasScaler>();
            scaler.uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            scaler.referenceResolution = new Vector2(1920f, 1080f);
            scaler.matchWidthOrHeight = 0.5f;

            overlayRoot = CreateImage("FacilityMap_Overlay", canvasObject.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero,
                new Color(0.005f, 0.012f, 0.022f, 0.82f)).gameObject;

            Image panel = CreateImage("FacilityMap_Panel", overlayRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(0f, 0f), PanelColor);
            panel.rectTransform.sizeDelta = new Vector2(1460f, 860f);

            CreateText("FacilityMap_Title", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -45f), new Vector2(-100f, 70f), 38f,
                TextAlignmentOptions.Center, new Color(0.72f, 0.92f, 1f)).text = "시설 배치도  ·  [M] 닫기";
            currentLocationText = CreateText("FacilityMap_CurrentLocation", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -91f), new Vector2(-100f, 42f), 24f,
                TextAlignmentOptions.Center, Color.white);

            GameObject mapAreaObject = new GameObject("Schematic_Map_Area", typeof(RectTransform));
            mapAreaObject.transform.SetParent(panel.transform, false);
            mapArea = mapAreaObject.GetComponent<RectTransform>();
            mapArea.anchorMin = Vector2.zero;
            mapArea.anchorMax = Vector2.one;
            mapArea.offsetMin = new Vector2(95f, 112f);
            mapArea.offsetMax = new Vector2(-95f, -150f);

            CreateCorridors();
            CreateRoomCards();

            CreateText("FacilityMap_Legend", panel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 25f), new Vector2(-100f, 45f), 20f,
                TextAlignmentOptions.Center, new Color(0.75f, 0.82f, 0.88f)).text =
                "이상현상과 위험 단계  ·  위치 안내용 간이도(거리·동선 비례 아님)  ·  방문 기록은 DAY 전환 후에도 유지";
            overlayRoot.SetActive(false);
        }

        private void CreateCorridors()
        {
            var connections = new[]
            {
                new[] { FacilityZoneId.StartRoom, FacilityZoneId.ControlHall },
                new[] { FacilityZoneId.ControlHall, FacilityZoneId.PurificationRoom },
                new[] { FacilityZoneId.ControlHall, FacilityZoneId.SealingRoom },
                new[] { FacilityZoneId.ControlHall, FacilityZoneId.ManaCoreRoom }
            };
            foreach (FacilityZoneId[] connection in connections)
            {
                ZoneIdentity from = GetZone(connection[0]);
                ZoneIdentity to = GetZone(connection[1]);
                if (from == null || to == null) continue;

                Vector2 a = MapPoint(from.MapPosition);
                Vector2 b = MapPoint(to.MapPosition);
                Vector2 delta = b - a;
                Image line = CreateImage($"Corridor_{connection[0]}_{connection[1]}", mapArea,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    (a + b) * 0.5f, new Color(0f, 0f, 0f, 0f));
                line.rectTransform.sizeDelta = new Vector2(delta.magnitude, 9f);
                line.rectTransform.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
                line.color = CorridorColor;
                corridorImages.Add(line);
            }
        }

        private void CreateRoomCards()
        {
            foreach (ZoneIdentity zone in zones)
            {
                if (zone == null) continue;
                Vector2 point = MapPoint(zone.MapPosition);
                Image room = CreateImage($"Room_{zone.Id}", mapArea,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), point, RoomColor);
                room.rectTransform.sizeDelta = new Vector2(245f, 145f);
                TMP_Text label = CreateText("RoomContents", room.transform,
                    Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(-20f, -12f), 20f,
                    TextAlignmentOptions.Center, Color.white);
                label.text = zone.DisplayName;
                roomPanels[zone.Id] = room;
                roomLabels[zone.Id] = label;
            }
        }

        private void RefreshMap()
        {
            ZoneIdentity current = zoneTracker != null ? zoneTracker.CurrentZone : null;
            if (currentLocationText != null)
                currentLocationText.text = current != null
                    ? $"현재 위치  ·  {current.DisplayName}" : "현재 위치 확인 중";

            activeAnomalies.Refresh(zones);

            foreach (ZoneIdentity zone in zones)
            {
                if (zone == null || !roomLabels.TryGetValue(zone.Id, out TMP_Text label)) continue;
                bool isCurrent = current == zone;
                if (roomPanels.TryGetValue(zone.Id, out Image panel))
                    panel.color = isCurrent ? CurrentRoomColor : RoomColor;

                var contents = new StringBuilder();
                contents.Append(zone.DisplayName);
                contents.Append(isCurrent ? "\n- 현재 위치" : "\n");
                if (zoneTracker != null && zoneTracker.HasVisited(zone.Id)) contents.Append("방문");

                if (activeAnomalies.TryGetActivity(zone.Id,
                        out ActiveAnomalyZoneSnapshot.ZoneActivity zoneActivity))
                {
                    foreach (AnomalyBehaviour anomaly in zoneActivity.Anomalies)
                    {
                        string anomalyName = anomaly is ManaRift ? "마력 균열" : anomaly.name.Replace("(Clone)", "").Trim();
                        contents.Append("\n- ");
                        contents.Append(anomalyName);
                        contents.Append(" · 위험 ");
                        contents.Append(anomaly.DangerStage);
                        contents.Append("단계");
                    }
                }

                label.text = contents.ToString();
                int highestStage = activeAnomalies.TryGetActivity(zone.Id,
                    out ActiveAnomalyZoneSnapshot.ZoneActivity activity)
                    ? activity.HighestDangerStage : 0;
                label.color = highestStage >= 3 ? new Color(1f, 0.48f, 0.44f)
                    : highestStage == 2 ? new Color(1f, 0.76f, 0.38f)
                    : highestStage == 1 ? new Color(1f, 0.9f, 0.5f) : Color.white;
            }
        }

        private Vector2 MapPoint(Vector2 normalized)
        {
            float width = mapArea != null && mapArea.rect.width > 0f ? mapArea.rect.width : 1270f;
            float height = mapArea != null && mapArea.rect.height > 0f ? mapArea.rect.height : 600f;
            return new Vector2((normalized.x - 0.5f) * width, (normalized.y - 0.5f) * height);
        }

        private ZoneIdentity GetZone(FacilityZoneId id)
        {
            foreach (ZoneIdentity zone in zones)
                if (zone != null && zone.Id == id) return zone;
            return null;
        }

        private Image CreateImage(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 anchoredPosition, Color color)
        {
            return CreateImage(name, parent, anchorMin, anchorMax, Vector2.zero,
                anchoredPosition, color);
        }

        private Image CreateImage(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Color color)
        {
            GameObject imageObject = new GameObject(name, typeof(RectTransform), typeof(Image));
            imageObject.transform.SetParent(parent, false);
            RectTransform rect = imageObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot == Vector2.zero ? new Vector2(0.5f, 0.5f) : pivot;
            rect.anchoredPosition = anchoredPosition;
            Image image = imageObject.GetComponent<Image>();
            image.color = color;
            image.raycastTarget = true;
            return image;
        }

        private TMP_Text CreateText(string name, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 pivot, Vector2 anchoredPosition, Vector2 size,
            float fontSize, TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new GameObject(name, typeof(RectTransform), typeof(TextMeshProUGUI));
            textObject.transform.SetParent(parent, false);
            RectTransform rect = textObject.GetComponent<RectTransform>();
            rect.anchorMin = anchorMin;
            rect.anchorMax = anchorMax;
            rect.pivot = pivot;
            rect.anchoredPosition = anchoredPosition;
            rect.sizeDelta = size;
            TMP_Text text = textObject.GetComponent<TMP_Text>();
            text.font = koreanFont != null ? koreanFont : TMP_Settings.defaultFontAsset;
            text.fontSize = fontSize;
            text.alignment = alignment;
            text.color = color;
            text.raycastTarget = false;
            text.textWrappingMode = TextWrappingModes.Normal;
            return text;
        }
    }
}
