using System.Collections.Generic;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>활성 이상현상이 있는 각 구역의 화면 방향 핑을 표시합니다.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10009)]
    public sealed class AnomalyScreenPingHUD : MonoBehaviour
    {
        [SerializeField] private DayFlowManager dayFlow;
        [SerializeField] private ZoneIdentity[] zones = new ZoneIdentity[0];
        [SerializeField] private TMP_FontAsset koreanFont;

        private readonly Dictionary<FacilityZoneId, PingView> pingViews =
            new Dictionary<FacilityZoneId, PingView>();
        private readonly List<FacilityZoneId> staleViewIds = new List<FacilityZoneId>();
        private readonly ActiveAnomalyZoneSnapshot snapshot = new ActiveAnomalyZoneSnapshot();

        private Camera playerCamera;
        private RectTransform canvasRect;
        private GameObject pingRoot;
        private Sprite diamondSprite;
        private Sprite arrowSprite;
        private Texture2D diamondTexture;
        private Texture2D arrowTexture;
        private int observedDay = int.MinValue;
        private DayState observedState = (DayState)(-1);

        public bool IsConfigured
        {
            get
            {
                if (dayFlow == null || zones == null || zones.Length != 5) return false;
                var ids = new HashSet<FacilityZoneId>();
                foreach (ZoneIdentity zone in zones)
                    if (zone == null || !ids.Add(zone.Id)) return false;
                return true;
            }
        }

        private void Awake()
        {
            ResolveReferences();
            BuildUI();
            if (dayFlow != null)
            {
                observedDay = dayFlow.CurrentDay;
                observedState = dayFlow.State;
            }
            HideAllPings();
        }

        private void Update()
        {
            if (dayFlow == null || zones == null || zones.Length == 0) ResolveReferences();
            if (pingRoot == null) BuildUI();
            if (dayFlow == null || pingRoot == null)
            {
                HideAllPings();
                return;
            }

            if (observedDay != dayFlow.CurrentDay || observedState != dayFlow.State)
            {
                HideAllPings();
                snapshot.Clear();
                observedDay = dayFlow.CurrentDay;
                observedState = dayFlow.State;
            }

            if (dayFlow.State != DayState.Playing || dayFlow.IsMapOpen || dayFlow.InventoryOpen)
            {
                HideAllPings();
                return;
            }

            if (playerCamera == null || !playerCamera.isActiveAndEnabled)
                playerCamera = Camera.main != null ? Camera.main : FindFirstObjectByType<Camera>();
            if (playerCamera == null)
            {
                HideAllPings();
                return;
            }

            EnsurePingViews();
            snapshot.Refresh(zones);
            foreach (ActiveAnomalyZoneSnapshot.ZoneActivity activity in snapshot.Activities)
            {
                if (!pingViews.TryGetValue(activity.Zone.Id, out PingView view)) continue;
                if (activity.ActiveCount == 0)
                {
                    view.Root.SetActive(false);
                    continue;
                }
                RefreshPing(view, activity);
            }
        }

        private void OnEnable()
        {
            HideAllPings();
        }

        public void Configure(DayFlowManager manager, ZoneIdentity[] configuredZones, TMP_FontAsset uiFont)
        {
            dayFlow = manager;
            zones = configuredZones ?? new ZoneIdentity[0];
            if (uiFont != null) koreanFont = uiFont;
            EnsurePingViews();
        }

        private void ResolveReferences()
        {
            if (dayFlow == null) dayFlow = FindFirstObjectByType<DayFlowManager>();
            if (zones == null || zones.Length == 0)
                zones = FindObjectsByType<ZoneIdentity>(FindObjectsSortMode.None);
        }

        private void EnsurePingViews()
        {
            if (pingRoot == null || zones == null) return;

            staleViewIds.Clear();
            foreach (KeyValuePair<FacilityZoneId, PingView> pair in pingViews)
            {
                bool found = false;
                foreach (ZoneIdentity zone in zones)
                    if (zone != null && zone.Id == pair.Key && zone == pair.Value.Zone)
                    {
                        found = true;
                        break;
                    }
                if (!found) staleViewIds.Add(pair.Key);
            }

            foreach (FacilityZoneId id in staleViewIds)
            {
                PingView stale = pingViews[id];
                if (stale.Root != null) Destroy(stale.Root);
                pingViews.Remove(id);
            }

            foreach (ZoneIdentity zone in zones)
            {
                if (zone == null || pingViews.ContainsKey(zone.Id)) continue;
                pingViews.Add(zone.Id, CreatePingView(zone));
            }
        }

        private void BuildUI()
        {
            if (pingRoot != null) return;

            GameObject canvasObject = new GameObject("AnomalyPing_Canvas",
                typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            canvasObject.transform.SetParent(transform, false);
            Canvas canvas = canvasObject.GetComponent<Canvas>();
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

            diamondSprite = CreateShapeSprite("AnomalyPing_Diamond", false, out diamondTexture);
            arrowSprite = CreateShapeSprite("AnomalyPing_Arrow", true, out arrowTexture);
            EnsurePingViews();
        }

        private PingView CreatePingView(ZoneIdentity zone)
        {
            GameObject root = new GameObject("Ping_" + zone.Id, typeof(RectTransform));
            root.transform.SetParent(pingRoot.transform, false);
            RectTransform rootRect = root.GetComponent<RectTransform>();
            rootRect.anchorMin = rootRect.anchorMax = new Vector2(0.5f, 0.5f);
            rootRect.pivot = new Vector2(0.5f, 0.5f);
            rootRect.sizeDelta = Vector2.zero;

            GameObject markerObject = new GameObject("Direction_Icon", typeof(RectTransform), typeof(Image));
            markerObject.transform.SetParent(root.transform, false);
            RectTransform markerRect = markerObject.GetComponent<RectTransform>();
            markerRect.anchorMin = markerRect.anchorMax = new Vector2(0.5f, 0.5f);
            markerRect.pivot = new Vector2(0.5f, 0.5f);
            markerRect.sizeDelta = new Vector2(38f, 38f);
            Image markerImage = markerObject.GetComponent<Image>();
            markerImage.sprite = diamondSprite;
            markerImage.preserveAspect = true;
            markerImage.raycastTarget = false;

            GameObject infoObject = new GameObject("Zone_Info_Background",
                typeof(RectTransform), typeof(Image));
            infoObject.transform.SetParent(root.transform, false);
            RectTransform infoRect = infoObject.GetComponent<RectTransform>();
            infoRect.anchorMin = infoRect.anchorMax = new Vector2(0.5f, 0.5f);
            infoRect.pivot = new Vector2(0.5f, 0.5f);
            infoRect.sizeDelta = new Vector2(400f, 68f);
            Image background = infoObject.GetComponent<Image>();
            background.color = new Color(0.018f, 0.035f, 0.052f, 0.93f);
            background.raycastTarget = false;

            TMP_Text zoneNameText = CreateText("Zone_Name", infoObject.transform,
                new Vector2(0f, 0.5f), new Vector2(1f, 1f),
                new Vector2(12f, 0f), new Vector2(-12f, -4f), 18f,
                TextAlignmentOptions.Center, Color.white);
            TMP_Text detailText = CreateText("Zone_Details", infoObject.transform,
                new Vector2(0f, 0f), new Vector2(1f, 0.5f),
                new Vector2(10f, 4f), new Vector2(-10f, 0f), 14f,
                TextAlignmentOptions.Center, Color.white);
            detailText.enableAutoSizing = true;
            detailText.fontSizeMin = 11f;
            detailText.fontSizeMax = 14f;
            detailText.textWrappingMode = TextWrappingModes.NoWrap;

            root.SetActive(false);
            return new PingView(zone, root, markerRect, markerImage, infoRect, background,
                zoneNameText, detailText);
        }

        private void RefreshPing(PingView view, ActiveAnomalyZoneSnapshot.ZoneActivity activity)
        {
            Vector3 worldPosition = view.Zone.transform.position;
            Vector3 screen = playerCamera.WorldToScreenPoint(worldPosition);
            bool inFront = screen.z > playerCamera.nearClipPlane;
            const float edgeInset = 58f;
            bool onScreen = inFront && screen.x >= edgeInset && screen.x <= Screen.width - edgeInset
                && screen.y >= edgeInset && screen.y <= Screen.height - edgeInset;

            Vector2 direction = DirectionToTarget(worldPosition, screen, inFront);
            Vector2 markerScreen = onScreen
                ? new Vector2(screen.x, screen.y)
                : ScreenEdgePoint(direction, edgeInset);
            if (onScreen)
            {
                view.MarkerImage.sprite = diamondSprite;
                view.MarkerRect.localRotation = Quaternion.identity;
            }
            else
            {
                view.MarkerImage.sprite = arrowSprite;
                view.MarkerRect.localRotation = Quaternion.Euler(0f, 0f,
                    Mathf.Atan2(direction.y, direction.x) * Mathf.Rad2Deg - 90f);
            }

            SetScreenPosition(view.MarkerRect, markerScreen);
            PositionInfo(view, markerScreen, direction, onScreen);

            float distance = Vector3.Distance(playerCamera.transform.position, worldPosition);
            string distanceText = distance >= 10f ? $"{distance:F0} m" : $"{distance:F1} m";
            view.ZoneNameText.text = view.Zone.DisplayName;
            view.DetailsText.text = $"활성 {activity.ActiveCount}개  위험 {activity.HighestDangerStage}단계  직선거리 {distanceText}";
            Color danger = DangerColor(activity.HighestDangerStage);
            view.MarkerImage.color = danger;
            view.ZoneNameText.color = danger;
            view.Background.color = new Color(0.018f, 0.035f, 0.052f, 0.93f);
            if (!view.Root.activeSelf) view.Root.SetActive(true);
        }

        private Vector2 DirectionToTarget(Vector3 worldPosition, Vector3 screen, bool inFront)
        {
            Vector2 direction;
            if (inFront)
            {
                direction = new Vector2(screen.x - Screen.width * 0.5f,
                    screen.y - Screen.height * 0.5f);
            }
            else
            {
                Vector3 localTarget = playerCamera.transform.InverseTransformPoint(worldPosition);
                direction = new Vector2(localTarget.x, localTarget.y);
            }

            if (direction.sqrMagnitude < 0.0001f) direction = Vector2.down;
            return direction.normalized;
        }

        private static Vector2 ScreenEdgePoint(Vector2 direction, float inset)
        {
            float halfWidth = Mathf.Max(1f, Screen.width * 0.5f - inset);
            float halfHeight = Mathf.Max(1f, Screen.height * 0.5f - inset);
            float xScale = Mathf.Abs(direction.x) > 0.0001f
                ? halfWidth / Mathf.Abs(direction.x) : float.PositiveInfinity;
            float yScale = Mathf.Abs(direction.y) > 0.0001f
                ? halfHeight / Mathf.Abs(direction.y) : float.PositiveInfinity;
            float edgeScale = Mathf.Min(xScale, yScale);
            return new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) + direction * edgeScale;
        }

        private void PositionInfo(PingView view, Vector2 markerScreen, Vector2 direction, bool onScreen)
        {
            if (!RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, markerScreen, null, out Vector2 markerLocal))
                return;

            Vector2 infoLocal;
            if (onScreen)
            {
                float verticalOffset = markerLocal.y > canvasRect.rect.height * 0.28f ? -76f : 76f;
                infoLocal = markerLocal + new Vector2(0f, verticalOffset);
            }
            else
            {
                infoLocal = markerLocal - direction * 178f;
            }

            float halfWidth = view.InfoRect.sizeDelta.x * 0.5f;
            float halfHeight = view.InfoRect.sizeDelta.y * 0.5f;
            infoLocal.x = Mathf.Clamp(infoLocal.x, -canvasRect.rect.width * 0.5f + halfWidth + 10f,
                canvasRect.rect.width * 0.5f - halfWidth - 10f);
            infoLocal.y = Mathf.Clamp(infoLocal.y, -canvasRect.rect.height * 0.5f + halfHeight + 10f,
                canvasRect.rect.height * 0.5f - halfHeight - 10f);
            view.InfoRect.anchoredPosition = infoLocal;
        }

        private void SetScreenPosition(RectTransform rect, Vector2 screenPosition)
        {
            if (RectTransformUtility.ScreenPointToLocalPointInRectangle(
                    canvasRect, screenPosition, null, out Vector2 localPosition))
                rect.anchoredPosition = localPosition;
        }

        private TMP_Text CreateText(string objectName, Transform parent, Vector2 anchorMin,
            Vector2 anchorMax, Vector2 offsetMin, Vector2 offsetMax, float fontSize,
            TextAlignmentOptions alignment, Color color)
        {
            GameObject textObject = new GameObject(objectName,
                typeof(RectTransform), typeof(TextMeshProUGUI));
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
            text.textWrappingMode = TextWrappingModes.NoWrap;
            return text;
        }

        private static Sprite CreateShapeSprite(string spriteName, bool triangle, out Texture2D texture)
        {
            const int size = 64;
            const int samples = 4;
            texture = new Texture2D(size, size, TextureFormat.RGBA32, false, true)
            {
                name = spriteName + "_Texture",
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
                        float px = ((x + (sx + 0.5f) / samples) / size) * 2f - 1f;
                        float py = ((y + (sy + 0.5f) / samples) / size) * 2f - 1f;
                        bool inside = triangle
                            ? py >= -0.92f && py <= 0.92f && Mathf.Abs(px) <= (py + 1f) * 0.5f
                            : Mathf.Abs(px) + Mathf.Abs(py) <= 0.94f;
                        if (inside) covered++;
                    }
                    byte alpha = (byte)(covered * 255 / (samples * samples));
                    pixels[y * size + x] = new Color32(255, 255, 255, alpha);
                }
            }

            texture.SetPixels32(pixels);
            texture.Apply(false, true);
            Sprite sprite = Sprite.Create(texture, new Rect(0f, 0f, size, size),
                new Vector2(0.5f, 0.5f), size);
            sprite.name = spriteName;
            return sprite;
        }

        private static Color DangerColor(int stage)
        {
            if (stage >= 3) return new Color(1f, 0.34f, 0.28f);
            if (stage == 2) return new Color(1f, 0.68f, 0.28f);
            if (stage == 1) return new Color(0.55f, 0.88f, 1f);
            return Color.white;
        }

        private void HideAllPings()
        {
            foreach (PingView view in pingViews.Values)
                if (view.Root != null && view.Root.activeSelf) view.Root.SetActive(false);
        }

        private void OnDisable()
        {
            HideAllPings();
            snapshot.Clear();
        }

        private void OnDestroy()
        {
            DestroyGeneratedObject(diamondSprite);
            DestroyGeneratedObject(arrowSprite);
            DestroyGeneratedObject(diamondTexture);
            DestroyGeneratedObject(arrowTexture);
        }

        private static void DestroyGeneratedObject(Object generated)
        {
            if (generated == null) return;
            if (Application.isPlaying) Destroy(generated);
            else DestroyImmediate(generated);
        }

        private sealed class PingView
        {
            public readonly ZoneIdentity Zone;
            public readonly GameObject Root;
            public readonly RectTransform MarkerRect;
            public readonly Image MarkerImage;
            public readonly RectTransform InfoRect;
            public readonly Image Background;
            public readonly TMP_Text ZoneNameText;
            public readonly TMP_Text DetailsText;

            public PingView(ZoneIdentity zone, GameObject root, RectTransform markerRect,
                Image markerImage, RectTransform infoRect, Image background,
                TMP_Text zoneNameText, TMP_Text detailsText)
            {
                Zone = zone;
                Root = root;
                MarkerRect = markerRect;
                MarkerImage = markerImage;
                InfoRect = infoRect;
                Background = background;
                ZoneNameText = zoneNameText;
                DetailsText = detailsText;
            }
        }
    }
}
