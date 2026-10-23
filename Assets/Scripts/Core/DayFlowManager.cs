using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.Serialization;

namespace WardShift
{
    /// <summary>DAY 시작, 근무 시간, 진압 보상, CLEAR/FAILED와 다음 DAY 전환을 관리.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10006)]
    public sealed class DayFlowManager : MonoBehaviour
    {
        [Header("DAY 설정")]
        [SerializeField, Min(1f)] private float dayDurationSeconds = 180f;
        [FormerlySerializedAs("minimumRiftReward"), SerializeField, Min(0)] private int minimumAnomalyReward = 1;
        [FormerlySerializedAs("maximumRiftReward"), SerializeField, Min(0)] private int maximumAnomalyReward = 2;
        [Header("씬 참조")]
        [SerializeField] private WardStatus wardStatus;
        [SerializeField] private AnomalySpawner anomalySpawner;
        [SerializeField] private PlayerInventory inventory;
        [SerializeField] private MagicCaster magicCaster;
        [SerializeField] private Transform playerRoot;
        [SerializeField] private Transform startPoint;
        [SerializeField] private PlayerPickupInteractor pickupInteractor;

        [Header("실행 상태 (읽기 전용)")]
        [SerializeField, Min(1)] private int currentDay = 1;
        [SerializeField] private DayState state = DayState.Preparing;
        [SerializeField] private float remainingSeconds;

        private readonly InputAction inventoryAction = new InputAction(
            "Toggle Inventory", InputActionType.Button, "<Keyboard>/b");
        private readonly HashSet<int> rewardedAnomalyIds = new HashSet<int>();
        private readonly List<AnomalyBehaviour> subscribedAnomalies = new List<AnomalyBehaviour>();
        private bool inventoryOpen;

        public int CurrentDay => currentDay;
        public DayState State => state;
        public float RemainingSeconds => remainingSeconds;
        public float DayDurationSeconds => dayDurationSeconds;
        public bool InventoryOpen => inventoryOpen;
        public PlayerInventory Inventory => inventory;
        public WardStatus Status => wardStatus;
        public PlayerContamination Player => wardStatus != null ? wardStatus.Player : null;
        public PlayerPickupInteractor PickupInteractor => pickupInteractor;
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
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
        }

        private void OnEnable()
        {
            inventoryAction.Enable();
            if (wardStatus != null) wardStatus.Failed += OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned += OnAnomalySpawned;
        }

        private void Start()
        {
            if (wardStatus == null || anomalySpawner == null || inventory == null
                || playerRoot == null || startPoint == null)
            {
                Debug.LogError("DayFlowManager: Setup Day 5 메뉴로 씬 참조를 연결하세요.", this);
                enabled = false;
                return;
            }
            currentDay = Mathf.Max(1, currentDay);
            if (wardStatus.HasFailed) OnDayFailed(wardStatus.FailureReason);
            else BeginDay();
        }

        private void LateUpdate()
        {
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
                if (remainingSeconds <= 0f && anomalySpawner != null)
                    anomalySpawner.SetSpawningEnabled(false);
            }

            if (remainingSeconds <= 0f && !HasActiveAnomalies())
            {
                if (wardStatus.Stability > 0f && wardStatus.Player != null
                    && wardStatus.Player.Value < 100f)
                    CompleteDay();
                else if (wardStatus.HasFailed)
                    OnDayFailed(wardStatus.FailureReason);
            }
        }

        /// <summary>DAY CLEAR 화면의 다음 날 버튼이 호출. 중복 클릭은 상태 검사로 무시.</summary>
        public void BeginNextDay()
        {
            if (state != DayState.DayClear) return;
            currentDay++;
            state = DayState.Preparing;
            BeginDay();
        }

        /// <summary>FAILED 화면에서 DAY 1부터 새 테스트를 시작.</summary>
        public void RestartFromDayOne()
        {
            if (state != DayState.DayFailed) return;
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            UnsubscribeFromAnomalies();
            ClearAllAnomalies();
            currentDay = 1;
            state = DayState.Preparing;
            inventory.ResetForNewRun();
            wardStatus.ResetForNewRun();
            StartCoroutine(BeginDayNextFrame());
        }

        /// <summary>인벤토리 창을 열거나 닫음.</summary>
        public void ToggleInventory()
        {
            if (state != DayState.Playing) return;
            inventoryOpen = !inventoryOpen;
            if (inventoryOpen) wardStatus.PauseGameForUI();
            else wardStatus.ResumeGameFromUI();
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
            rewardedAnomalyIds.Clear();
            UnsubscribeFromAnomalies();
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            ClearAllAnomalies();
            TeleportPlayerToStart();
            inventory.BeginDay();
            if (wardStatus != null) wardStatus.ResetDayHazardDisplay();
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
                anomalySpawner.SetSpawningEnabled(true);
        }

        private void CompleteDay()
        {
            if (state != DayState.Playing) return;
            state = DayState.DayClear;
            remainingSeconds = 0f;
            inventoryOpen = false;
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            wardStatus.PauseGameForUI();
        }

        private void OnDayFailed(string reason)
        {
            if (state == DayState.DayClear || state == DayState.DayFailed) return;
            state = DayState.DayFailed;
            inventoryOpen = false;
            if (anomalySpawner != null) anomalySpawner.SetSpawningEnabled(false);
            if (magicCaster != null) magicCaster.SetInputBlocked(true);
            if (wardStatus != null) wardStatus.PauseGameForUI();
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
                reward = Random.Range(min, max + 1);
            }
            inventory.AddManaCrystals(Mathf.Max(0, reward));
            anomaly.ResolutionCompleted -= OnAnomalyResolved;
            subscribedAnomalies.Remove(anomaly);
        }

        private bool HasActiveAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies)
                if (anomaly != null && anomaly.IsHazardActive) return true;
            return false;
        }

        private void ClearAllAnomalies()
        {
            foreach (AnomalyBehaviour anomaly in FindObjectsByType<AnomalyBehaviour>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (anomaly != null) Destroy(anomaly.gameObject);
            if (anomalySpawner != null) anomalySpawner.ClearActiveAnomalies();
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
            if (wardStatus != null) wardStatus.Failed -= OnDayFailed;
            if (anomalySpawner != null) anomalySpawner.AnomalySpawned -= OnAnomalySpawned;
            UnsubscribeFromAnomalies();
        }

        private void OnDestroy() => inventoryAction.Dispose();
    }

    /// <summary>하루 진행의 외부에서 확인 가능한 상태.</summary>
    public enum DayState { Preparing, Playing, DayClear, DayFailed }
}
