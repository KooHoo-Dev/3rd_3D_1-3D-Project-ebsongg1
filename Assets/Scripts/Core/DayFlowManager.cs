using System;
using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace WardShift
{
    /// <summary>
    /// DAY 재도전 체크포인트에 상태를 추가할 수 있는 확장 지점입니다.
    /// 강화 시스템은 이 인터페이스를 구현해 DAY 시작 상태를 저장·복원할 수 있습니다.
    /// </summary>
    public interface IDayRetryCheckpointParticipant
    {
        object CaptureDayRetryState();
        void RestoreDayRetryState(object state);
    }

    /// <summary>DAY 시작, 근무 시간, 진압 보상, CLEAR/FAILED와 다음 DAY 전환을 관리.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10006)]
    public sealed class DayFlowManager : MonoBehaviour
    {
        [Header("DAY 설정")]
        [SerializeField, Min(1f)] private float dayDurationSeconds = 180f;
        [SerializeField, Min(0f)] private float spawnStopBeforeEndSeconds = 30f;
        [SerializeField, Min(1)] private int finalDay = 7;
        [FormerlySerializedAs("minimumRiftReward"), SerializeField, Min(0)] private int minimumAnomalyReward = 1;
        [FormerlySerializedAs("maximumRiftReward"), SerializeField, Min(0)] private int maximumAnomalyReward = 2;
        [Header("디버그 치트")]
        [SerializeField] private bool enableDaySkipCheat = true;
        [SerializeField] private Key daySkipCheatKey = Key.F8;
        [Header("씬 참조")]
        [SerializeField] private WardStatus wardStatus;
        [SerializeField] private AnomalySpawner anomalySpawner;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MagicCaster magicCaster;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform startPoint;
        [SerializeField] private PlayerPickupInteractor pickupInteractor;
        [SerializeField] private FacilityZoneTracker zoneTracker;

        [Header("실행 상태 (읽기 전용)")]
        [SerializeField, Min(1)] private int currentDay = 1;
        [SerializeField] private DayState state = DayState.Preparing;
        [SerializeField] private float remainingSeconds;

        private readonly InputAction inventoryAction = new InputAction(
            "Toggle Inventory", InputActionType.Button, "<Keyboard>/b");
        private readonly InputAction mapAction = new InputAction(
            "Toggle Facility Map", InputActionType.Button, "<Keyboard>/m");
        private InputAction daySkipAction;
        private readonly HashSet<int> rewardedAnomalyIds = new HashSet<int>();
        private readonly List<AnomalyBehaviour> subscribedAnomalies = new List<AnomalyBehaviour>();
        private readonly List<PickupCheckpoint> pickupCheckpoint = new List<PickupCheckpoint>();
        private readonly List<ParticipantCheckpoint> participantCheckpoint = new List<ParticipantCheckpoint>();
        private PlayerInventory.DayStateSnapshot inventoryCheckpoint;
        private int dayRandomSeed;
        private bool inventoryOpen;
        private bool mapOpen;

        public int CurrentDay => currentDay;
        public DayState State => state;
        public float RemainingSeconds => remainingSeconds;
        public float DayDurationSeconds => dayDurationSeconds;
        public float SpawnStopBeforeEndSeconds => spawnStopBeforeEndSeconds;
        public int FinalDay => Mathf.Max(1, finalDay);
        public int DayRandomSeed => dayRandomSeed;
        public bool InventoryOpen => inventoryOpen;
        public bool IsMapOpen => mapOpen;
        public FacilityZoneTracker ZoneTracker => zoneTracker;
        public PlayerInventory Inventory => inventory;
        public WardStatus Status => wardStatus;
        public PlayerContamination Player => wardStatus != null ? wardStatus.Player : null;
        public PlayerPickupInteractor PickupInteractor => pickupInteractor;
        public event Action<bool> MapVisibilityChanged;
        public bool IsConfigured => wardStatus != null && anomalySpawner != null && inventory != null
            && magicCaster != null && playerRoot != null && startPoint != null && pickupInteractor != null;

        private void Awake()
        {
            if (wardStatus == null) wardStatus = FindFirstObjectByType<WardStatus>();
            if (anomalySpawner == null) anomalySpawner = FindFirstObjectByType<AnomalySpawner>();
            if (inventory == null) inventory = GetComponent<PlayerInventory>();
            if (magicCaster == null) magicCaster = FindFirstObjectByType<MagicCaster>();
            if (playerRoot == null && wardStatus != null && wardStatus.Player != null)
                playerRoot = wardStatus.Player.transform;
            if (pickupInteractor == null) pickupInteractor = FindFirstObjectByType<PlayerPickupInteractor>();
            if (zoneTracker == null) zoneTracker = FindFirstObjectByType<FacilityZoneTracker>();
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (daySkipCheatKey != Key.None)
            {
                string keyPath = daySkipCheatKey.ToString().ToLowerInvariant();
                daySkipAction = new InputAction("Cheat: Advance Day", InputActionType.Button,
                    $"<Keyboard>/{keyPath}");
            }
        }

        private void OnEnable()
        {
            inventoryAction.Enable();
            mapAction.Enable();
            if (daySkipAction != null) daySkipAction.Enable();
            if (wardStatus != null) wardStatus.Failed += OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned += OnAnomalySpawned;
        }

        private void Start()
        {
            if (!IsConfigured)
            {
                Debug.LogError("DayFlowManager: Setup DAY Progression 메뉴로 씬 참조를 연결하세요.", this);
                enabled = false;
                return;
            }
            currentDay = Mathf.Max(1, currentDay);
            if (wardStatus.HasFailed) OnDayFailed(wardStatus.FailureReason);
            else BeginDay();
        }

        private void LateUpdate()
        {
            if (enableDaySkipCheat && daySkipAction != null && daySkipAction.WasPressedThisFrame())
            {
                CheatAdvanceDay();
                return;
            }
            if (mapAction.WasPressedThisFrame())
            {
                ToggleMap();
                return;
            }
            if (inventoryAction.WasPressedThisFrame()) ToggleInventory();
            if (state != DayState.Playing || Time.timeScale <= 0f) return;
            if (wardStatus != null && wardStatus.HasFailed)
            {
                OnDayFailed(wardStatus.FailureReason);
                return;
            }

            if (remainingSeconds > 0f)
            {
                remainingSeconds = Mathf.Max(0f, remainingSeconds - Time.deltaTime);
                if (anomalySpawner != null)
                    anomalySpawner.SetSpawnWindowOpen(remainingSeconds > Mathf.Max(0f, spawnStopBeforeEndSeconds));
            }

            if (remainingSeconds <= 0f)
            {
                if (anomalySpawner != null)
                {
                    anomalySpawner.SetSpawningEnabled(false);
                    anomalySpawner.SetSpawnWindowOpen(false);
                }
                ClearAllAnomalies();
                if (HasSurvivedDay()) CompleteDay();
                else if (wardStatus == null || !wardStatus.HasFailed)
                    OnDayFailed("근무 종료 시 생존 조건을 만족하지 못했습니다.");
            }
        }

        /// <summary>DAY CLEAR 화면의 다음 날 버튼이 호출. 중복 클릭은 상태 검사로 무시.</summary>
        public void BeginNextDay()
        {
            if (state != DayState.DayClear) return;
            currentDay = Mathf.Min(currentDay + 1, FinalDay);
            dayRandomSeed = CreateDaySeed();
            state = DayState.Preparing;
            BeginDay();
        }

        /// <summary>디버그용 DAY 스킵. 진행 중 현상은 보상 없이 정리하고 다음 DAY를 시작합니다.</summary>
        public void CheatAdvanceDay()
        {
            if (wardStatus != null && wardStatus.HasFailed && state == DayState.Playing)
            {
                OnDayFailed(wardStatus.FailureReason);
                return;
            }

            if (state == DayState.Playing)
            {
                if (anomalySpawner != null)
                {
                    anomalySpawner.SetSpawningEnabled(false);
                    anomalySpawner.SetSpawnWindowOpen(false);
                }
                ClearAllAnomalies();
                CompleteDay();
            }

            if (state == DayState.DayClear) BeginNextDay();
        }

        /// <summary>FAILED 화면에서 현재 DAY 시작 체크포인트로 재도전.</summary>
        public void RetryCurrentDay()
        {
            if (state != DayState.DayFailed) return;
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            UnsubscribeFromAnomalies();
            ClearAllAnomalies();
            state = DayState.Preparing;
            SetMapOpen(false, false);
            if (zoneTracker != null) zoneTracker.ResetVisitedZones();
            RestoreDayCheckpoint();
            if (pickupInteractor != null) pickupInteractor.ClearCurrentPickup();
            StartCoroutine(BeginDayNextFrame());
        }

        /// <summary>기존 UI/호출부 호환용 이름. 동작은 현재 DAY 재도전입니다.</summary>
        public void RestartFromDayOne() => RetryCurrentDay();

        /// <summary>인벤토리 창을 열거나 닫음.</summary>
        public void ToggleInventory()
        {
            if (state != DayState.Playing || mapOpen) return;
            inventoryOpen = !inventoryOpen;
            if (inventoryOpen) wardStatus.PauseGameForUI();
            else wardStatus.ResumeGameFromUI();
        }

        /// <summary>지도 열기 입력. 인벤토리와 DAY 종료 화면이 열려 있을 때는 무시합니다.</summary>
        public void ToggleMap()
        {
            if (state != DayState.Playing || inventoryOpen) return;
            SetMapOpen(!mapOpen, true);
        }

        /// <summary>셋업 도구가 현재 씬의 기존 컴포넌트를 연결.</summary>
        public void Configure(WardStatus status, AnomalySpawner spawner, PlayerInventory playerInventory,
            MagicCaster caster, Transform player, Transform spawn, PlayerPickupInteractor pickup)
        {
            wardStatus = status;
            anomalySpawner = spawner;
            inventory = playerInventory;
            magicCaster = caster;
            playerRoot = player;
            startPoint = spawn;
            pickupInteractor = pickup;
        }

        public void ConfigureZoneTracker(FacilityZoneTracker tracker)
        {
            zoneTracker = tracker;
        }

        /// <summary>테스트 씬에서 근무 시간과 결정 보상 범위를 지정.</summary>
        public void ConfigureDaySettings(float durationSeconds, int minReward, int maxReward)
        {
            dayDurationSeconds = Mathf.Max(1f, durationSeconds);
            minimumAnomalyReward = Mathf.Max(0, minReward);
            maximumAnomalyReward = Mathf.Max(minimumAnomalyReward, maxReward);
        }

        private void BeginDay()
        {
            inventoryOpen = false;
            SetMapOpen(false, false);
            rewardedAnomalyIds.Clear();
            UnsubscribeFromAnomalies();
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            ClearAllAnomalies();
            ResetSceneManaDevicesForNewDay();
            TeleportPlayerToStart();
            inventory.BeginDay();
            if (wardStatus != null) wardStatus.ResetForNewDay();
            if (dayRandomSeed == 0) dayRandomSeed = CreateDaySeed();
            remainingSeconds = Mathf.Max(1f, dayDurationSeconds);
            state = DayState.Playing;
            Cursor.lockState = CursorLockMode.Locked;
            Cursor.visible = false;
            if (wardStatus != null) wardStatus.ResumeGameFromUI();
            if (magicCaster != null)
            {
                magicCaster.ResetForNewDay();
                magicCaster.SetInputBlocked(false);
            }
            if (anomalySpawner != null)
            {
                anomalySpawner.ConfigureForDay(currentDay, dayRandomSeed);
                anomalySpawner.SetSpawnWindowOpen(remainingSeconds > Mathf.Max(0f, spawnStopBeforeEndSeconds));
                CaptureDayCheckpoint();
                anomalySpawner.SetSpawningEnabled(true);
            }
            else CaptureDayCheckpoint();
        }

        private void CompleteDay()
        {
            if (state != DayState.Playing) return;
            state = currentDay >= FinalDay ? DayState.Ending : DayState.DayClear;
            SetMapOpen(false, false);
            remainingSeconds = 0f;
            inventoryOpen = false;
            if (anomalySpawner != null)
            {
                anomalySpawner.SetSpawningEnabled(false);
                anomalySpawner.SetSpawnWindowOpen(false);
            }
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            wardStatus.PauseGameForUI();
        }

        private void OnDayFailed(string reason)
        {
            if (state == DayState.DayClear || state == DayState.DayFailed) return;
            state = DayState.DayFailed;
            SetMapOpen(false, false);
            inventoryOpen = false;
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (anomalySpawner != null) anomalySpawner.SetSpawnWindowOpen(false);
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            if (wardStatus != null) wardStatus.PauseGameForUI();
        }

        private void SetMapOpen(bool open, bool managePause)
        {
            if (mapOpen == open) return;
            mapOpen = open;
            if (managePause && wardStatus != null)
            {
                if (open) wardStatus.PauseGameForUI();
                else wardStatus.ResumeGameFromUI();
            }
            MapVisibilityChanged?.Invoke(mapOpen);
        }

        private void OnAnomalySpawned(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || subscribedAnomalies.Contains(anomaly)) return;
            anomaly.ResolutionCompleted += OnAnomalyResolved;
            subscribedAnomalies.Add(anomaly);
        }

        private void OnAnomalyResolved(AnomalyBehaviour anomaly)
        {
            if (anomaly == null || state != DayState.Playing) return;
            int id = anomaly.GetInstanceID();
            if (!rewardedAnomalyIds.Add(id)) return;
            int reward = anomaly.ResolutionReward;
            if (reward < 0)
            {
                int min = Mathf.Max(0, minimumAnomalyReward);
                int max = Mathf.Max(min, maximumAnomalyReward);
                reward = UnityEngine.Random.Range(min, max + 1);
            }
            inventory.AddManaCrystals(Mathf.Max(0, reward));
            anomaly.ResolutionCompleted -= OnAnomalyResolved;
            subscribedAnomalies.Remove(anomaly);
        }

        private bool HasSurvivedDay()
        {
            return wardStatus != null && !wardStatus.HasFailed && wardStatus.Stability > 0f
                && wardStatus.Player != null && wardStatus.Player.Value < 100f;
        }

        private void CaptureDayCheckpoint()
        {
            if (inventory == null) return;
            inventoryCheckpoint = inventory.CaptureDayState();
            pickupCheckpoint.Clear();
            foreach (ItemPickup pickup in FindObjectsByType<ItemPickup>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (pickup == null || pickup.gameObject.scene != gameObject.scene) continue;
                pickupCheckpoint.Add(new PickupCheckpoint(pickup, pickup.IsCollected,
                    pickup.gameObject.activeSelf));
            }

            participantCheckpoint.Clear();
            foreach (MonoBehaviour behaviour in FindObjectsByType<MonoBehaviour>(
                         FindObjectsInactive.Include, FindObjectsSortMode.None))
            {
                if (behaviour == null || behaviour.gameObject.scene != gameObject.scene
                    || !(behaviour is IDayRetryCheckpointParticipant participant)) continue;
                participantCheckpoint.Add(new ParticipantCheckpoint(participant, participant.CaptureDayRetryState()));
            }
        }

        private void RestoreDayCheckpoint()
        {
            if (inventory != null && inventoryCheckpoint != null)
                inventory.RestoreDayState(inventoryCheckpoint);

            ItemPickup[] currentPickups = FindObjectsByType<ItemPickup>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ItemPickup pickup in currentPickups)
            {
                if (pickup == null || pickup.gameObject.scene != gameObject.scene) continue;
                PickupCheckpoint snapshot = FindPickupCheckpoint(pickup);
                if (snapshot != null) pickup.RestoreDayState(snapshot.IsCollected, snapshot.ActiveSelf);
                else
                {
                    pickup.gameObject.SetActive(false);
                    Destroy(pickup.gameObject);
                }
            }

            foreach (ParticipantCheckpoint snapshot in participantCheckpoint)
                if (snapshot.Participant is UnityEngine.Object participantObject && participantObject != null)
                    snapshot.Participant.RestoreDayRetryState(snapshot.State);
        }

        private PickupCheckpoint FindPickupCheckpoint(ItemPickup pickup)
        {
            foreach (PickupCheckpoint snapshot in pickupCheckpoint)
                if (snapshot.Pickup == pickup) return snapshot;
            return null;
        }

        private static int CreateDaySeed() => UnityEngine.Random.Range(1, int.MaxValue);

        private sealed class PickupCheckpoint
        {
            public readonly ItemPickup Pickup;
            public readonly bool IsCollected;
            public readonly bool ActiveSelf;
            public PickupCheckpoint(ItemPickup pickup, bool isCollected, bool activeSelf)
            {
                Pickup = pickup;
                IsCollected = isCollected;
                ActiveSelf = activeSelf;
            }
        }

        private sealed class ParticipantCheckpoint
        {
            public readonly IDayRetryCheckpointParticipant Participant;
            public readonly object State;
            public ParticipantCheckpoint(IDayRetryCheckpointParticipant participant, object state)
            {
                Participant = participant;
                State = state;
            }
        }

        private void ClearAllAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in FindObjectsByType<AnomalyBehaviour>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (anomaly != null) Destroy(anomaly.gameObject);
            if (anomalySpawner != null) anomalySpawner.ClearActiveAnomalies();
        }

        private static void ResetSceneManaDevicesForNewDay()
        {
            ManaDeviceVisuals[] devices = FindObjectsByType<ManaDeviceVisuals>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            foreach (ManaDeviceVisuals device in devices)
                if (device != null) device.ResetForNewDay();
        }

        private void TeleportPlayerToStart()
        {
            if (playerRoot == null || startPoint == null) return;
            CharacterController controller = playerRoot.GetComponent<CharacterController>();
            bool wasEnabled = controller != null && controller.enabled;
            if (wasEnabled) controller.enabled = false;
            playerRoot.SetPositionAndRotation(startPoint.position, startPoint.rotation);
            if (wasEnabled) controller.enabled = true;
        }

        private IEnumerator BeginDayNextFrame()
        {
            yield return null;
            BeginDay();
        }

        private void UnsubscribeFromAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in subscribedAnomalies)
                if (anomaly != null) anomaly.ResolutionCompleted -= OnAnomalyResolved;
            subscribedAnomalies.Clear();
        }

        private void OnDisable()
        {
            inventoryAction.Disable();
            mapAction.Disable();
            if (daySkipAction != null) daySkipAction.Disable();
            if (wardStatus != null) wardStatus.Failed -= OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned -= OnAnomalySpawned;
            UnsubscribeFromAnomalies();
        }

        private void OnDestroy()
        {
            inventoryAction.Dispose();
            mapAction.Dispose();
            if (daySkipAction != null) daySkipAction.Dispose();
        }
    }

    /// <summary>하루 진행의 외부에서 확인 가능한 상태.</summary>
    public enum DayState { Preparing, Playing, DayClear, DayFailed, Ending }
}
