using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>MainGame의 Map_Root 로컬 좌표를 사용하는 2D 시설 평면도입니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10008)]
    public sealed class FacilityMapHUD : MonoBehaviour
    {
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private FacilityZoneTracker zoneTracker;
        [SerializeField] private ZoneIdentity[] zones = new ZoneIdentity[0];
        [SerializeField] private Transform mapRoot;
        [SerializeField] private Transform mapReferenceOrigin;
        [SerializeField] private TMP_FontAsset koreanFont;

        [Header("MainGame 좌표 보정")]
        [Tooltip("첨부 탑뷰에서 중앙 복도 앵커가 있는 기준 픽셀입니다.")]
        [SerializeField] private Vector2 referenceAnchorImagePoint = new Vector2(150f, 280f);
        [Tooltip("첨부 탑뷰의 픽셀을 Map_Root 로컬 단위로 변환하는 값입니다.")]
        [SerializeField, Min(0.01f)] private float referencePixelsPerUnit = 4f;
        [Tooltip("첨부 탑뷰에서 시설 전체를 감싸는 좌상단/우하단 픽셀입니다.")]
        [SerializeField] private Vector2 referenceImageBoundsMin = new Vector2(30f, 4f);
        [SerializeField] private Vector2 referenceImageBoundsMax = new Vector2(288f, 509f);
        [Tooltip("Map_Root의 로컬 X/Z 평면을 UI로 회전합니다. MainGame 화면 방향은 180도입니다.")]
        [SerializeField] private float mapRotationDegrees = 180f;
        [Tooltip("시설 전체가 지도 영역에 들어간 뒤 적용하는 크기 배율입니다.")]
        [SerializeField, Min(0.1f)] private float mapScaleMultiplier = 1f;
        [Tooltip("평면도 외곽선과 지도 가장자리 사이의 여백입니다. Map_Root 로컬 단위.")]
        [SerializeField, Min(0f)] private float mapBoundsPaddingUnits = 4f;
        [Tooltip("시설 바깥에서 이 거리까지 플레이어 마커를 표시합니다.")]
        [SerializeField, Min(0f)] private float markerOutsidePaddingUnits = 8f;
        [Tooltip("플레이어 화살표에 더하는 회전 보정각입니다.")]
        [SerializeField] private float playerArrowAngleOffset;

        private readonly Dictionary<FacilityZoneId, ZoneMapView> zoneViews =
            new Dictionary<FacilityZoneId, ZoneMapView>();
        private readonly Dictionary<int, AnomalyMarkerView> anomalyMarkers =
            new Dictionary<int, AnomalyMarkerView>();
        private readonly List<MapPolygonView> mapPolygons = new List<MapPolygonView>();
        private readonly List<MapLineView> mapLines = new List<MapLineView>();

        private GameObject overlayRoot;
        private TMP_Text currentLocationText;
        private RectTransform mapArea;
        private RectTransform playerMarker;
        private Sprite glowSprite;
        private Sprite ringSprite;
        private float pixelsPerUnit;
        private Vector2 cachedMapAreaSize;
        private Vector2 cachedReferenceOriginLocal;
        private float cachedRotation = float.NaN;
        private bool mapGeometryBuilt;

        private const float CorridorWidthImagePixels = 14f;
        private const float CorridorWallImagePixels = 2.5f;
        private const float RoomWallImagePixels = 1.5f;

        private static readonly Color OverlayColor = new Color(0.006f, 0.01f, 0.018f, 0.84f);
        private static readonly Color PanelColor = new Color(0.035f, 0.052f, 0.068f, 0.99f);
        private static readonly Color MapBackColor = new Color(0.025f, 0.036f, 0.043f, 0.98f);
        private static readonly Color MapGridColor = new Color(0.28f, 0.35f, 0.36f, 0.17f);
        private static readonly Color CorridorWallColor = new Color(0.39f, 0.43f, 0.39f, 0.97f);
        private static readonly Color CorridorFloorColor = new Color(0.19f, 0.24f, 0.24f, 1f);
        private static readonly Color RoomWallColor = new Color(0.72f, 0.64f, 0.45f, 0.95f);
        private static readonly Color CurrentZoneTint = new Color(0.18f, 0.52f, 0.52f, 1f);
        private static readonly Color LabelColor = new Color(0.91f, 0.88f, 0.76f, 1f);
        private static readonly Color StepColor = new Color(0.82f, 0.67f, 0.39f, 0.94f);

        public bool IsMapOpen => dayFlow != null && dayFlow.IsMapOpen;
        public bool IsConfigured => dayFlow != null && zoneTracker != null && mapRoot != null
            && mapReferenceOrigin != null && zones != null && zones.Length == 5;

        private void Awake()
        {
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (zoneTracker == null && dayFlow != null) zoneTracker = dayFlow.ZoneTracker;
            if (zoneTracker == null) zoneTracker = FindFirstObjectByType<FacilityZoneTracker>();
            if (zones == null || zones.Length == 0)
                zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
            if (mapRoot == null)
            {
                GameObject rootObject = GameObject.Find("Map_Root");
                if (rootObject != null) mapRoot = rootObject.transform;
            }
            if (mapReferenceOrigin == null)
                mapReferenceOrigin = FindZone(FacilityZoneId.ControlHall)?.transform;

            glowSprite = CreateRadialSprite(false);
            ringSprite = CreateRadialSprite(true);
            BuildUI();
            RefreshMap();
        }

        private void Update()
        {
            bool visible = dayFlow != null && dayFlow.State == DayState.Playing && dayFlow.IsMapOpen;
            if (overlayRoot != null && overlayRoot.activeSelf != visible)
                overlayRoot.SetActive(visible);

            EnsureMapGeometry();
            if (visible) RefreshMap();
        }

        private void OnDestroy()
        {
            DestroyGeneratedSprite(glowSprite);
            DestroyGeneratedSprite(ringSprite);
        }

        public void Configure(DayFlowManager manager, FacilityZoneTracker tracker,
            ZoneIdentity[] configuredZones, TMP_FontAsset uiFont)
        {
            dayFlow = manager;
            zoneTracker = tracker;
            zones = configuredZones ?? new ZoneIdentity[0];
            if (uiFont != null) koreanFont = uiFont;
        }

        public void ConfigureMapCoordinates(Transform configuredMapRoot, Transform referenceOrigin)
        {
            mapRoot = configuredMapRoot;
            mapReferenceOrigin = referenceOrigin;
            mapGeometryBuilt = false;
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

            Image overlay = CreateImage("FacilityMap_Overlay", canvasObject.transform,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, OverlayColor);
            overlay.raycastTarget = true;
            overlayRoot = overlay.gameObject;
            Image panel = CreateImage("FacilityMap_Panel", overlayRoot.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.zero, PanelColor);
            panel.rectTransform.sizeDelta = new Vector2(1180f, 980f);
            Outline panelOutline = panel.gameObject.AddComponent<Outline>();
            panelOutline.effectColor = new Color(0.53f, 0.60f, 0.52f, 0.42f);
            panelOutline.effectDistance = new Vector2(2f, -2f);

            CreateText("FacilityMap_Title", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -39f), new Vector2(-100f, 64f), 35f,
                TextAlignmentOptions.Center, new Color(0.79f, 0.88f, 0.81f, 1f)).text = "시설 배치도  ·  [TAB] 닫기";
            currentLocationText = CreateText("FacilityMap_CurrentLocation", panel.transform,
                new Vector2(0f, 1f), new Vector2(1f, 1f), new Vector2(0.5f, 1f),
                new Vector2(0f, -94f), new Vector2(-100f, 40f), 22f,
                TextAlignmentOptions.Center, Color.white);

            GameObject mapAreaObject = new GameObject("MainGame_Floorplan", typeof(RectTransform));
            mapAreaObject.transform.SetParent(panel.transform, false);
            mapArea = mapAreaObject.GetComponent<RectTransform>();
            mapArea.anchorMin = Vector2.zero;
            mapArea.anchorMax = Vector2.one;
            mapArea.offsetMin = new Vector2(55f, 87f);
            mapArea.offsetMax = new Vector2(-55f, -145f);
            CreateImage("Floorplan_Background", mapArea,
                Vector2.zero, Vector2.one, Vector2.zero, Vector2.zero, MapBackColor);

            CreateMapGrid();
            CreateCorridors();
            CreateZoneAreas();
            CreateStairMarkers();
            CreateRoomLabels();
            CreatePlayerMarker();

            CreateMapLegend();

            CreateText("FacilityMap_Footer", panel.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0f), new Vector2(0.5f, 0f),
                new Vector2(0f, 23f), new Vector2(-100f, 42f), 17f,
                TextAlignmentOptions.Center, new Color(0.59f, 0.67f, 0.66f, 1f)).text =
                "활성 이상현상만 표시  ·  높이 차이는 계단 기호로 표시";

            overlayRoot.SetActive(false);
        }

        private void CreateMapGrid()
        {
            float xMin = referenceImageBoundsMin.x;
            float xMax = referenceImageBoundsMax.x;
            float yMin = referenceImageBoundsMin.y;
            float yMax = referenceImageBoundsMax.y;
            for (float x = 50f; x < xMax; x += 50f)
                AddMapLine(new Vector2(x, yMin), new Vector2(x, yMax), 0.55f, MapGridColor);
            for (float y = 50f; y < yMax; y += 50f)
                AddMapLine(new Vector2(xMin, y), new Vector2(xMax, y), 0.55f, MapGridColor);
        }

        private void CreateCorridors()
        {
            // 연결 중심선은 첨부 탑뷰에서 꺾이는 구간을 따라 따로 그립니다.
            AddCorridor(new Vector2(150f, 102f), new Vector2(150f, 178f));
            AddCorridor(new Vector2(150f, 126f), new Vector2(84f, 126f),
                new Vector2(84f, 149f), new Vector2(74f, 149f), new Vector2(74f, 174f),
                new Vector2(98f, 190f));
            AddCorridor(new Vector2(190f, 284f), new Vector2(207f, 284f));
            AddCorridor(new Vector2(150f, 343f), new Vector2(150f, 373f),
                new Vector2(137f, 385f), new Vector2(118f, 393f), new Vector2(117f, 462f),
                new Vector2(119f, 476f));
            AddCorridor(new Vector2(109f, 322f), new Vector2(99f, 322f));
        }

        private void AddCorridor(params Vector2[] points)
        {
            for (int i = 0; i < points.Length - 1; i++)
            {
                AddMapLine(points[i], points[i + 1], CorridorWidthImagePixels + CorridorWallImagePixels * 2f,
                    CorridorWallColor);
                AddMapLine(points[i], points[i + 1], CorridorWidthImagePixels, CorridorFloorColor);
            }
        }

        private void CreateZoneAreas()
        {
            CreateZoneArea(FacilityZoneId.StartRoom, new Color(0.25f, 0.29f, 0.27f, 1f),
                new Vector2(111f, 489f), 18f, new[]
                {
                    new Vector2(92f, 470f), new Vector2(130f, 468f), new Vector2(138f, 474f),
                    new Vector2(139f, 495f), new Vector2(134f, 503f), new Vector2(91f, 504f),
                    new Vector2(85f, 498f), new Vector2(86f, 477f)
                });

            CreateZoneArea(FacilityZoneId.ControlHall, new Color(0.18f, 0.27f, 0.27f, 1f),
                new Vector2(151f, 294f), 17f, new[]
                {
                    new Vector2(137f, 173f), new Vector2(164f, 173f), new Vector2(167f, 184f),
                    new Vector2(183f, 188f), new Vector2(190f, 197f), new Vector2(188f, 216f),
                    new Vector2(197f, 225f), new Vector2(193f, 248f), new Vector2(198f, 262f),
                    new Vector2(193f, 280f), new Vector2(198f, 293f), new Vector2(193f, 319f),
                    new Vector2(186f, 324f), new Vector2(189f, 345f), new Vector2(175f, 348f),
                    new Vector2(172f, 356f), new Vector2(151f, 358f), new Vector2(149f, 349f),
                    new Vector2(126f, 350f), new Vector2(123f, 344f), new Vector2(111f, 345f),
                    new Vector2(108f, 332f), new Vector2(93f, 328f), new Vector2(94f, 314f),
                    new Vector2(105f, 311f), new Vector2(108f, 298f), new Vector2(105f, 285f),
                    new Vector2(108f, 270f), new Vector2(104f, 262f), new Vector2(109f, 240f),
                    new Vector2(106f, 226f), new Vector2(114f, 218f), new Vector2(115f, 198f),
                    new Vector2(124f, 191f), new Vector2(128f, 181f), new Vector2(137f, 181f)
                });

            CreateZoneArea(FacilityZoneId.PurificationRoom, new Color(0.21f, 0.29f, 0.23f, 1f),
                new Vector2(243f, 285f), 18f, new[]
                {
                    new Vector2(205f, 256f), new Vector2(215f, 254f), new Vector2(226f, 257f),
                    new Vector2(239f, 253f), new Vector2(248f, 256f), new Vector2(263f, 254f),
                    new Vector2(273f, 257f), new Vector2(282f, 255f), new Vector2(287f, 261f),
                    new Vector2(285f, 278f), new Vector2(289f, 286f), new Vector2(284f, 298f),
                    new Vector2(286f, 307f), new Vector2(279f, 314f), new Vector2(266f, 312f),
                    new Vector2(258f, 317f), new Vector2(245f, 314f), new Vector2(237f, 318f),
                    new Vector2(226f, 313f), new Vector2(215f, 315f), new Vector2(209f, 311f),
                    new Vector2(201f, 309f), new Vector2(200f, 296f), new Vector2(196f, 289f),
                    new Vector2(201f, 278f), new Vector2(198f, 271f)
                });

            CreateZoneArea(FacilityZoneId.SealingRoom, new Color(0.25f, 0.22f, 0.31f, 1f),
                new Vector2(67f, 204f), 18f, new[]
                {
                    new Vector2(45f, 181f), new Vector2(51f, 175f), new Vector2(62f, 175f),
                    new Vector2(65f, 168f), new Vector2(79f, 168f), new Vector2(83f, 178f),
                    new Vector2(94f, 179f), new Vector2(101f, 184f), new Vector2(106f, 192f),
                    new Vector2(109f, 209f), new Vector2(103f, 216f), new Vector2(100f, 225f),
                    new Vector2(86f, 225f), new Vector2(79f, 232f), new Vector2(63f, 229f),
                    new Vector2(54f, 233f), new Vector2(47f, 226f), new Vector2(38f, 224f),
                    new Vector2(34f, 216f), new Vector2(31f, 208f), new Vector2(35f, 198f),
                    new Vector2(32f, 190f), new Vector2(38f, 184f)
                });

            CreateZoneArea(FacilityZoneId.ManaCoreRoom, new Color(0.31f, 0.25f, 0.17f, 1f),
                new Vector2(148f, 57f), 18f, new[]
                {
                    new Vector2(134f, 10f), new Vector2(152f, 10f), new Vector2(158f, 16f),
                    new Vector2(170f, 17f), new Vector2(176f, 29f), new Vector2(173f, 44f),
                    new Vector2(177f, 53f), new Vector2(170f, 63f), new Vector2(169f, 77f),
                    new Vector2(160f, 83f), new Vector2(156f, 94f), new Vector2(157f, 108f),
                    new Vector2(141f, 108f), new Vector2(138f, 94f), new Vector2(126f, 89f),
                    new Vector2(120f, 80f), new Vector2(118f, 63f), new Vector2(114f, 54f),
                    new Vector2(118f, 42f), new Vector2(116f, 30f), new Vector2(121f, 23f),
                    new Vector2(129f, 22f)
                });
        }

        private void CreateZoneArea(FacilityZoneId id, Color baseColor, Vector2 labelPoint,
            float labelSize, Vector2[] outlinePoints)
        {
            FacilityMapPolygonGraphic floor = CreatePolygon($"Floor_{id}", outlinePoints, baseColor);
            ZoneIdentity identity = FindZone(id);
            zoneViews[id] = new ZoneMapView(identity, floor, baseColor, labelPoint, labelSize);
            for (int i = 0; i < outlinePoints.Length; i++)
                AddMapLine(outlinePoints[i], outlinePoints[(i + 1) % outlinePoints.Length],
                    RoomWallImagePixels, RoomWallColor);
        }

        private void CreateStairMarkers()
        {
            AddStairMarker(new Vector2(150f, 115f), true);
            AddStairMarker(new Vector2(150f, 159f), true);
            AddStairMarker(new Vector2(150f, 365f), true);
            AddStairMarker(new Vector2(117f, 421f), true);
            AddStairMarker(new Vector2(84f, 138f), false);
        }

        private void AddStairMarker(Vector2 center, bool inVerticalCorridor)
        {
            const float halfWidth = 7f;
            for (int i = 0; i < 4; i++)
            {
                float offset = (i - 1.5f) * 2.6f;
                Vector2 a = inVerticalCorridor
                    ? center + new Vector2(-halfWidth, offset)
                    : center + new Vector2(offset, -halfWidth);
                Vector2 b = inVerticalCorridor
                    ? center + new Vector2(halfWidth, offset)
                    : center + new Vector2(offset, halfWidth);
                AddMapLine(a, b, 1.35f, StepColor);
            }
        }

        private void CreateRoomLabels()
        {
            foreach (KeyValuePair<FacilityZoneId, ZoneMapView> pair in zoneViews)
            {
                ZoneMapView view = pair.Value;
                string label = view.Identity != null ? view.Identity.DisplayName : GetFallbackZoneName(pair.Key);
                TMP_Text text = CreateText($"Label_{pair.Key}", mapArea,
                    new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f),
                    Vector2.zero, new Vector2(190f, 38f), view.LabelSize,
                    TextAlignmentOptions.Center, LabelColor);
                text.text = label;
                text.fontStyle = FontStyles.Bold;
                view.Label = text;
            }
        }

        private void CreatePlayerMarker()
        {
            GameObject root = new GameObject("Player_Position_Arrow", typeof(RectTransform));
            root.transform.SetParent(mapArea, false);
            playerMarker = root.GetComponent<RectTransform>();
            playerMarker.anchorMin = new Vector2(0.5f, 0.5f);
            playerMarker.anchorMax = new Vector2(0.5f, 0.5f);
            playerMarker.pivot = new Vector2(0.5f, 0.5f);
            playerMarker.sizeDelta = new Vector2(52f, 52f);

            Image glow = CreateImage("PlayerMarker_Glow", root.transform,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.zero, new Color(0.12f, 0.91f, 1f, 0.72f));
            glow.sprite = glowSprite;
            glow.rectTransform.sizeDelta = new Vector2(48f, 48f);

            TMP_Text arrow = CreateText("PlayerMarker_Direction", root.transform,
                Vector2.zero, Vector2.one, new Vector2(0.5f, 0.5f), Vector2.zero,
                new Vector2(45f, 45f), 36f, TextAlignmentOptions.Center,
                new Color(0.43f, 1f, 1f, 1f));
            arrow.text = "▲";
            arrow.fontStyle = FontStyles.Bold;
        }

        private void CreateMapLegend()
        {
            Vector2 left = new Vector2(0.71f, 0.5f);
            CreateText("FacilityMap_LegendTitle", mapArea, left, left,
                new Vector2(0f, 0.5f), new Vector2(0f, 102f), new Vector2(190f, 32f),
                20f, TextAlignmentOptions.Left, new Color(0.84f, 0.77f, 0.55f, 1f)).text = "표시";

            Vector2 playerIconAnchor = left + new Vector2(0f, 0.055f);
            TMP_Text playerIcon = CreateText("Legend_Player_Icon", mapArea,
                playerIconAnchor, playerIconAnchor, new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(24f, 28f), 23f, TextAlignmentOptions.Center,
                new Color(0.35f, 0.95f, 1f, 1f));
            playerIcon.text = "▲";
            CreateLegendLabel("Legend_Player_Label", left + new Vector2(0.045f, 0.055f), "플레이어");

            Vector2 anomalyAnchor = left + new Vector2(0f, -0.005f);
            Image anomalyIcon = CreateImage("Legend_Anomaly_Icon", mapArea,
                anomalyAnchor, anomalyAnchor, Vector2.zero, Vector2.zero,
                new Color(1f, 0.75f, 0.26f, 1f));
            anomalyIcon.sprite = ringSprite;
            anomalyIcon.rectTransform.sizeDelta = new Vector2(20f, 20f);
            Image anomalyGlow = CreateImage("Legend_Anomaly_Glow", mapArea,
                anomalyAnchor, anomalyAnchor, Vector2.zero, Vector2.zero,
                new Color(1f, 0.62f, 0.18f, 0.56f));
            anomalyGlow.sprite = glowSprite;
            anomalyGlow.rectTransform.sizeDelta = new Vector2(30f, 30f);
            anomalyIcon.transform.SetAsLastSibling();
            CreateLegendLabel("Legend_Anomaly_Label", left + new Vector2(0.045f, -0.005f), "활성 이상현상");

            Vector2 stairAnchor = left + new Vector2(0f, -0.065f);
            for (int i = 0; i < 3; i++)
            {
                Image step = CreateImage("Legend_Stair_Step", mapArea,
                    stairAnchor, stairAnchor, Vector2.zero,
                    new Vector2(0f, (1 - i) * 4f), StepColor);
                step.rectTransform.sizeDelta = new Vector2(17f, 1.8f);
            }
            CreateLegendLabel("Legend_Stair_Label", left + new Vector2(0.045f, -0.065f), "계단·높이 변화");
            CreateLegendLabel("Legend_Live_Label", left + new Vector2(0f, -0.135f), "마커 실시간 반영");
        }

        private void CreateLegendLabel(string name, Vector2 anchor, string value)
        {
            CreateText(name, mapArea, anchor, anchor, new Vector2(0f, 0.5f),
                Vector2.zero, new Vector2(220f, 32f), 17f,
                TextAlignmentOptions.Left, new Color(0.74f, 0.79f, 0.75f, 1f)).text = value;
        }

        private FacilityMapPolygonGraphic CreatePolygon(string objectName, Vector2[] imagePoints,
            Color color)
        {
            GameObject polygonObject = new GameObject(objectName, typeof(RectTransform),
                typeof(CanvasRenderer), typeof(FacilityMapPolygonGraphic));
            polygonObject.transform.SetParent(mapArea, false);
            RectTransform rect = polygonObject.GetComponent<RectTransform>();
            rect.anchorMin = Vector2.zero;
            rect.anchorMax = Vector2.one;
            rect.offsetMin = Vector2.zero;
            rect.offsetMax = Vector2.zero;
            FacilityMapPolygonGraphic graphic = polygonObject.GetComponent<FacilityMapPolygonGraphic>();
            graphic.color = color;
            mapPolygons.Add(new MapPolygonView(graphic, imagePoints));
            return graphic;
        }

        private void AddMapLine(Vector2 imageA, Vector2 imageB, float widthImagePixels, Color color)
        {
            Image line = CreateImage("Map_Line", mapArea,
                new Vector2(0.5f, 0.5f), new Vector2(0.5f, 0.5f), Vector2.zero,
                Vector2.zero, color);
            line.rectTransform.pivot = new Vector2(0.5f, 0.5f);
            line.raycastTarget = false;
            mapLines.Add(new MapLineView(line.rectTransform, imageA, imageB, widthImagePixels));
        }

        private void RefreshMap()
        {
            EnsureMapGeometry();
            if (mapRoot == null || mapArea == null) return;

            ZoneIdentity current = zoneTracker != null ? zoneTracker.CurrentZone : null;
            if (currentLocationText != null)
                currentLocationText.text = current != null
                    ? $"현재 위치  ·  {current.DisplayName}" : "현재 위치 확인 중";

            foreach (KeyValuePair<FacilityZoneId, ZoneMapView> pair in zoneViews)
            {
                bool isCurrent = current != null && current.Id == pair.Key;
                ZoneMapView view = pair.Value;
                view.Floor.color = isCurrent
                    ? Color.Lerp(view.BaseColor, CurrentZoneTint, 0.4f) : view.BaseColor;
                if (view.Label != null)
                    view.Label.color = isCurrent ? new Color(0.48f, 1f, 0.92f, 1f) : LabelColor;
            }

            RefreshPlayerMarker();
            RefreshAnomalyMarkers();
            AnimateAnomalyMarkers();
        }

        private void RefreshPlayerMarker()
        {
            if (playerMarker == null || zoneTracker == null || mapRoot == null) return;
            Vector3 worldPosition = zoneTracker.transform.position;
            Vector3 localPosition = mapRoot.InverseTransformPoint(worldPosition);
            Vector2 localXZ = new Vector2(localPosition.x, localPosition.z);
            GetMapBounds(out Vector2 min, out Vector2 max, out _);
            if (localXZ.x < min.x - markerOutsidePaddingUnits || localXZ.x > max.x + markerOutsidePaddingUnits
                || localXZ.y < min.y - markerOutsidePaddingUnits || localXZ.y > max.y + markerOutsidePaddingUnits)
            {
                playerMarker.gameObject.SetActive(false);
                return;
            }

            playerMarker.anchoredPosition = MapLocalToUI(localXZ);
            Vector3 localForward = mapRoot.InverseTransformDirection(zoneTracker.transform.forward);
            Vector2 mapForward = RotateMapVector(new Vector2(localForward.x, localForward.z));
            if (mapForward.sqrMagnitude > 0.0001f)
            {
                float angle = Mathf.Atan2(mapForward.y, mapForward.x) * Mathf.Rad2Deg - 90f
                    + playerArrowAngleOffset;
                playerMarker.localRotation = Quaternion.Euler(0f, 0f, angle);
            }
            if (!playerMarker.gameObject.activeSelf) playerMarker.gameObject.SetActive(true);
        }

        private void RefreshAnomalyMarkers()
        {
            HashSet<int> activeIds = new HashSet<int>();
            IReadOnlyList<AnomalyBehaviour> active = AnomalyBehaviour.ActiveAnomalies;
            for (int i = 0; i < active.Count; i++)
            {
                AnomalyBehaviour anomaly = active[i];
                if (anomaly == null || anomaly.IsEnded || !anomaly.isActiveAndEnabled
                    || !anomaly.gameObject.activeInHierarchy || !anomaly.IsHazardActive)
                    continue;

                int id = anomaly.GetInstanceID();
                activeIds.Add(id);
                if (!anomalyMarkers.TryGetValue(id, out AnomalyMarkerView view))
                {
                    view = CreateAnomalyMarker(anomaly.DangerStage);
                    anomalyMarkers.Add(id, view);
                }

                view.SetDangerStage(anomaly.DangerStage);
                Vector3 localPosition = mapRoot.InverseTransformPoint(anomaly.transform.position);
                view.Root.anchoredPosition = MapLocalToUI(new Vector2(localPosition.x, localPosition.z));
            }

            List<int> staleIds = null;
            foreach (KeyValuePair<int, AnomalyMarkerView> pair in anomalyMarkers)
            {
                if (activeIds.Contains(pair.Key)) continue;
                if (staleIds == null) staleIds = new List<int>();
                staleIds.Add(pair.Key);
            }
            if (staleIds == null) return;
            for (int i = 0; i < staleIds.Count; i++)
            {
                int id = staleIds[i];
                Destroy(anomalyMarkers[id].Root.gameObject);
                anomalyMarkers.Remove(id);
            }
        }

        private AnomalyMarkerView CreateAnomalyMarker(int dangerStage)
        {
            GameObject rootObject = new GameObject("Active_Anomaly_Map_Marker", typeof(RectTransform));
            rootObject.transform.SetParent(mapArea, false);
            RectTransform root = rootObject.GetComponent<RectTransform>();
            root.anchorMin = new Vector2(0.5f, 0.5f);
            root.anchorMax = new Vector2(0.5f, 0.5f);
            root.pivot = new Vector2(0.5f, 0.5f);
            root.sizeDelta = new Vector2(44f, 44f);

            Image glow = CreateImage("Anomaly_Glow", root, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);
            glow.sprite = glowSprite;
            glow.rectTransform.sizeDelta = new Vector2(42f, 42f);

            Image steadyRing = CreateImage("Anomaly_Ring", root, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);
            steadyRing.sprite = ringSprite;
            steadyRing.rectTransform.sizeDelta = new Vector2(19f, 19f);

            Image pulse = CreateImage("Anomaly_Appear_Pulse", root, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero, Color.white);
            pulse.sprite = ringSprite;
            pulse.rectTransform.sizeDelta = new Vector2(24f, 24f);

            Image core = CreateImage("Anomaly_Core", root, new Vector2(0.5f, 0.5f),
                new Vector2(0.5f, 0.5f), Vector2.zero, Vector2.zero,
                new Color(1f, 0.94f, 0.72f, 1f));
            core.rectTransform.sizeDelta = new Vector2(7f, 7f);
            core.rectTransform.localRotation = Quaternion.Euler(0f, 0f, 45f);

            return new AnomalyMarkerView(root, glow, steadyRing, pulse);
        }

        private void AnimateAnomalyMarkers()
        {
            float deltaTime = Time.unscaledDeltaTime;
            foreach (AnomalyMarkerView marker in anomalyMarkers.Values)
            {
                marker.PulseAge += deltaTime;
                if (marker.PulseAge >= 0.72f)
                {
                    marker.Pulse.enabled = false;
                    continue;
                }

                float t = Mathf.Clamp01(marker.PulseAge / 0.72f);
                marker.Pulse.enabled = true;
                marker.Pulse.rectTransform.localScale = Vector3.one * Mathf.Lerp(0.45f, 1.75f, t);
                Color color = marker.Glow.color;
                marker.Pulse.color = new Color(color.r, color.g, color.b, 1f - t);
            }
        }

        private void EnsureMapGeometry()
        {
            if (mapArea == null || mapRoot == null) return;
            Vector2 size = mapArea.rect.size;
            if (size.x <= 1f || size.y <= 1f) return;
            GetMapBounds(out Vector2 min, out Vector2 max, out Vector2 center);
            float rangeX = Mathf.Max(0.01f, max.x - min.x);
            float rangeZ = Mathf.Max(0.01f, max.y - min.y);
            float radians = mapRotationDegrees * Mathf.Deg2Rad;
            float rotatedWidth = Mathf.Abs(Mathf.Cos(radians)) * rangeX
                + Mathf.Abs(Mathf.Sin(radians)) * rangeZ;
            float rotatedHeight = Mathf.Abs(Mathf.Sin(radians)) * rangeX
                + Mathf.Abs(Mathf.Cos(radians)) * rangeZ;
            float nextScale = Mathf.Min(size.x / rotatedWidth, size.y / rotatedHeight)
                * Mathf.Max(0.1f, mapScaleMultiplier);
            Vector2 referenceOriginLocal = GetReferenceOriginLocal();

            bool changed = !mapGeometryBuilt || Mathf.Abs(nextScale - pixelsPerUnit) > 0.01f
                || (size - cachedMapAreaSize).sqrMagnitude > 0.25f
                || (referenceOriginLocal - cachedReferenceOriginLocal).sqrMagnitude > 0.0001f
                || Mathf.Abs(mapRotationDegrees - cachedRotation) > 0.001f;
            if (!changed) return;

            pixelsPerUnit = nextScale;
            cachedMapAreaSize = size;
            cachedReferenceOriginLocal = referenceOriginLocal;
            cachedRotation = mapRotationDegrees;
            for (int i = 0; i < mapLines.Count; i++) UpdateMapLine(mapLines[i]);
            for (int i = 0; i < mapPolygons.Count; i++) UpdateMapPolygon(mapPolygons[i]);
            foreach (KeyValuePair<FacilityZoneId, ZoneMapView> pair in zoneViews)
                if (pair.Value.Label != null)
                    pair.Value.Label.rectTransform.anchoredPosition = MapReferenceToUI(pair.Value.LabelImagePoint);
            mapGeometryBuilt = true;
        }

        private void UpdateMapLine(MapLineView line)
        {
            Vector2 a = MapReferenceToUI(line.ImageA);
            Vector2 b = MapReferenceToUI(line.ImageB);
            Vector2 delta = b - a;
            line.Rect.anchoredPosition = (a + b) * 0.5f;
            line.Rect.sizeDelta = new Vector2(delta.magnitude,
                Mathf.Max(1f, line.WidthImagePixels * pixelsPerUnit / referencePixelsPerUnit));
            line.Rect.localRotation = Quaternion.Euler(0f, 0f,
                Mathf.Atan2(delta.y, delta.x) * Mathf.Rad2Deg);
        }

        private void UpdateMapPolygon(MapPolygonView polygon)
        {
            Vector2[] points = new Vector2[polygon.ImagePoints.Length];
            for (int i = 0; i < points.Length; i++) points[i] = MapReferenceToUI(polygon.ImagePoints[i]);
            polygon.Graphic.SetVertices(points);
        }

        private Vector2 MapReferenceToUI(Vector2 imagePoint)
        {
            return MapLocalToUI(ReferenceImageToMapLocal(imagePoint));
        }

        private Vector2 ReferenceImageToMapLocal(Vector2 imagePoint)
        {
            Vector2 origin = GetReferenceOriginLocal();
            float pixelsPerUnitSafe = Mathf.Max(0.01f, referencePixelsPerUnit);
            return origin + new Vector2(
                (referenceAnchorImagePoint.x - imagePoint.x) / pixelsPerUnitSafe,
                (imagePoint.y - referenceAnchorImagePoint.y) / pixelsPerUnitSafe);
        }

        private Vector2 MapLocalToUI(Vector2 localXZ)
        {
            GetMapBounds(out _, out _, out Vector2 center);
            return RotateMapVector(localXZ - center) * pixelsPerUnit;
        }

        private Vector2 RotateMapVector(Vector2 vector)
        {
            float radians = mapRotationDegrees * Mathf.Deg2Rad;
            float cosine = Mathf.Cos(radians);
            float sine = Mathf.Sin(radians);
            return new Vector2(vector.x * cosine - vector.y * sine,
                vector.x * sine + vector.y * cosine);
        }

        private void GetMapBounds(out Vector2 min, out Vector2 max, out Vector2 center)
        {
            Vector2 a = ReferenceImageToMapLocal(referenceImageBoundsMin);
            Vector2 b = ReferenceImageToMapLocal(referenceImageBoundsMax);
            min = Vector2.Min(a, b) - Vector2.one * mapBoundsPaddingUnits;
            max = Vector2.Max(a, b) + Vector2.one * mapBoundsPaddingUnits;
            center = (min + max) * 0.5f;
        }

        private Vector2 GetReferenceOriginLocal()
        {
            if (mapRoot == null || mapReferenceOrigin == null) return Vector2.zero;
            Vector3 local = mapRoot.InverseTransformPoint(mapReferenceOrigin.position);
            return new Vector2(local.x, local.z);
        }

        private ZoneIdentity FindZone(FacilityZoneId id)
        {
            if (zones == null) return null;
            for (int i = 0; i < zones.Length; i++)
                if (zones[i] != null && zones[i].Id == id) return zones[i];
            return null;
        }

        private static string GetFallbackZoneName(FacilityZoneId id)
        {
            switch (id)
            {
                case FacilityZoneId.StartRoom: return "시작 구역";
                case FacilityZoneId.ControlHall: return "중앙 복도";
                case FacilityZoneId.PurificationRoom: return "정화실";
                case FacilityZoneId.SealingRoom: return "봉인실";
                case FacilityZoneId.ManaCoreRoom: return "마력핵실";
                default: return id.ToString();
            }
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
            image.raycastTarget = false;
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

        private Sprite CreateRadialSprite(bool ring)
        {
            const int size = 64;
            Texture2D texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = ring ? "FacilityMap_Ring" : "FacilityMap_Glow",
                hideFlags = HideFlags.HideAndDontSave,
                filterMode = FilterMode.Bilinear,
                wrapMode = TextureWrapMode.Clamp
            };
            Color[] pixels = new Color[size * size];
            for (int y = 0; y < size; y++)
            {
                for (int x = 0; x < size; x++)
                {
                    float dx = (x + 0.5f) / size * 2f - 1f;
                    float dy = (y + 0.5f) / size * 2f - 1f;
                    float radius = Mathf.Sqrt(dx * dx + dy * dy);
                    float alpha = ring
                        ? 1f - Mathf.SmoothStep(0.035f, 0.14f, Mathf.Abs(radius - 0.78f))
                        : Mathf.Pow(Mathf.Clamp01(1f - radius), 2.2f);
                    pixels[y * size + x] = new Color(1f, 1f, 1f, alpha);
                }
            }
            texture.SetPixels(pixels);
            texture.Apply(false, false);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size);
            sprite.name = ring ? "FacilityMap_RingSprite" : "FacilityMap_GlowSprite";
            sprite.hideFlags = HideFlags.HideAndDontSave;
            return sprite;
        }

        private static void DestroyGeneratedSprite(Sprite sprite)
        {
            if (sprite == null) return;
            Texture2D texture = sprite.texture;
            if (Application.isPlaying)
            {
                Destroy(sprite);
                if (texture != null) Destroy(texture);
            }
            else
            {
                DestroyImmediate(sprite);
                if (texture != null) DestroyImmediate(texture);
            }
        }

        private sealed class ZoneMapView
        {
            public readonly ZoneIdentity Identity;
            public readonly FacilityMapPolygonGraphic Floor;
            public readonly Color BaseColor;
            public readonly Vector2 LabelImagePoint;
            public readonly float LabelSize;
            public TMP_Text Label;

            public ZoneMapView(ZoneIdentity identity, FacilityMapPolygonGraphic floor,
                Color baseColor, Vector2 labelImagePoint, float labelSize)
            {
                Identity = identity;
                Floor = floor;
                BaseColor = baseColor;
                LabelImagePoint = labelImagePoint;
                LabelSize = labelSize;
            }
        }

        private sealed class MapPolygonView
        {
            public readonly FacilityMapPolygonGraphic Graphic;
            public readonly Vector2[] ImagePoints;

            public MapPolygonView(FacilityMapPolygonGraphic graphic, Vector2[] imagePoints)
            {
                Graphic = graphic;
                ImagePoints = imagePoints;
            }
        }

        private sealed class MapLineView
        {
            public readonly RectTransform Rect;
            public readonly Vector2 ImageA;
            public readonly Vector2 ImageB;
            public readonly float WidthImagePixels;

            public MapLineView(RectTransform rect, Vector2 imageA, Vector2 imageB,
                float widthImagePixels)
            {
                Rect = rect;
                ImageA = imageA;
                ImageB = imageB;
                WidthImagePixels = widthImagePixels;
            }
        }

        private sealed class AnomalyMarkerView
        {
            public readonly RectTransform Root;
            public readonly Image Glow;
            public readonly Image Ring;
            public readonly Image Pulse;
            public float PulseAge;
            private int dangerStage;

            public AnomalyMarkerView(RectTransform root, Image glow, Image ring, Image pulse)
            {
                Root = root;
                Glow = glow;
                Ring = ring;
                Pulse = pulse;
                PulseAge = 0f;
                SetDangerStage(1);
            }

            public void SetDangerStage(int stage)
            {
                stage = Mathf.Clamp(stage, 1, 3);
                if (dangerStage == stage && Glow.color.a > 0f) return;
                dangerStage = stage;
                Color tint = stage >= 3 ? new Color(1f, 0.23f, 0.18f, 1f)
                    : stage == 2 ? new Color(1f, 0.54f, 0.2f, 1f)
                    : new Color(1f, 0.86f, 0.38f, 1f);
                Glow.color = new Color(tint.r, tint.g, tint.b, 0.85f);
                Ring.color = new Color(tint.r, tint.g, tint.b, 1f);
                Pulse.color = tint;
            }
        }
    }
}
