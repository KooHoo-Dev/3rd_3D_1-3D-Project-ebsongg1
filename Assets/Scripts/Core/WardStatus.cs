using System;
using System.Collections.Generic;
using StarterAssets;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WardShift
{
    /// <summary>시전과 봉인 완료 후 위험을 계산하고 임시 게임 실패를 처리.</summary>
    [DisallowMultipleComponent]
    [DefaultExecutionOrder(10004)]
    public sealed class WardStatus : MonoBehaviour
    {
        [SerializeField] private PlayerContamination player;
        [SerializeField] private MagicCaster caster;
        [Tooltip("이동/시점 제어 스크립트, PlayerInput, MagicDetector를 연결. UI와 이 컴포넌트는 제외.")]
        [SerializeField] private Behaviour[] disableOnFailure = new Behaviour[0];
        [SerializeField, Range(0f, 100f)] private float stability = 100f;
        public float Stability => stability;
        public PlayerContamination Player => player;
        public bool HasFailed { get; private set; }
        public string FailureReason { get; private set; }
        public int HighestStage { get; private set; }
        public bool HasOverloadedRift { get; private set; }
        private readonly List<Behaviour> stopped = new List<Behaviour>();
        private float previousTimeScale;
        private bool previousInputBlocked;
        private CursorLockMode previousCursorLock;
        private bool previousCursorVisible;
        private bool controlsPaused;
        private bool hasPauseSnapshot;

        /// <summary>시설 또는 플레이어 조건으로 하루가 실패했을 때 발생합니다.</summary>
        public event Action<string> Failed;

        private void Awake()
        {
            if (FindObjectsByType<WardStatus>(FindObjectsSortMode.None).Length > 1)
            {
                Debug.LogError("WardStatus는 씬에 하나만 배치하세요.", this);
                enabled = false;
                return;
            }
            if (player == null || caster == null)
            {
                Debug.LogError("WardStatus: Player와 Caster를 연결하세요.", this);
                enabled = false;
            }
        }

        private void LateUpdate()
        {
            if (HasFailed || Time.timeScale <= 0f || player == null) return;
            // 소규모 프로토타입: 수동 배치/런타임 생성/삭제 모두 반영. 규모가 커지면 등록 목록으로 교체.
            ManaRift[] rifts = FindObjectsByType<ManaRift>(FindObjectsSortMode.None);
            HighestStage = 0;
            HasOverloadedRift = false;
            foreach (ManaRift rift in rifts)
            {
                stability = Mathf.Clamp(stability - rift.AdvanceHazard(Time.deltaTime), 0f, 100f);
                if (!rift.IsHazardActive) continue;
                HighestStage = Mathf.Max(HighestStage, rift.DangerStage);
                HasOverloadedRift |= rift.HasOverloaded;
            }
            if (player.isActiveAndEnabled)
                player.Advance(Time.deltaTime, rifts, FindObjectsByType<SafeZone>(FindObjectsSortMode.None));
            if (stability <= 0f || player.Value >= 100f)
                Fail(stability <= 0f ? "시설 안정도 0" : "마력 오염도 100");
        }

        private void Fail(string reason)
        {
            if (HasFailed) return;
            HasFailed = true;
            FailureReason = reason;
            PauseGameForUI();
            Debug.Log($"임시 게임 실패: {reason}. Play 모드를 종료 후 다시 시작하세요.", this);
            Failed?.Invoke(reason);
        }

        /// <summary>인벤토리 또는 DAY 종료 화면을 표시하며 게임플레이를 일시정지합니다.</summary>
        public void PauseGameForUI()
        {
            if (!hasPauseSnapshot)
            {
                previousTimeScale = Time.timeScale;
                previousCursorLock = Cursor.lockState;
                previousCursorVisible = Cursor.visible;
                previousInputBlocked = caster != null && caster.InputBlocked;
                hasPauseSnapshot = true;
            }
            if (caster != null) caster.SetInputBlocked(true);
            SetGameplayControlsEnabled(false);
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        /// <summary>일시정지를 해제하고 진입 전의 시간 및 커서 상태를 복원합니다.</summary>
        public void ResumeGameFromUI()
        {
            if (HasFailed) return;
            RestorePauseSnapshot();
        }

        /// <summary>새 게임 테스트를 위해 실패, 안정도와 플레이어 오염도를 초기화합니다.</summary>
        public void ResetForNewRun()
        {
            RestorePauseSnapshot();
            HasFailed = false;
            FailureReason = null;
            HighestStage = 0;
            HasOverloadedRift = false;
            stability = 100f;
            if (player != null) player.ResetForNewRun();
        }

        /// <summary>DAY 전환 때 임시 위험 표시만 지웁니다. 안정도와 오염도 값은 유지합니다.</summary>
        public void ResetDayHazardDisplay()
        {
            HighestStage = 0;
            HasOverloadedRift = false;
            if (player != null) player.ResetExposureDisplay();
        }

        private void SetGameplayControlsEnabled(bool enabled)
        {
            if (!enabled)
            {
                if (controlsPaused) return;
                controlsPaused = true;
                if (disableOnFailure != null)
                    foreach (Behaviour behaviour in disableOnFailure)
                        StopBehaviour(behaviour);
                if (player != null)
                {
                    foreach (PlayerInput input in player.GetComponentsInChildren<PlayerInput>(true))
                        StopBehaviour(input);
                    foreach (StarterAssetsInputs input in player.GetComponentsInChildren<StarterAssetsInputs>(true))
                    {
                        input.MoveInput(Vector2.zero);
                        input.LookInput(Vector2.zero);
                        input.JumpInput(false);
                        input.SprintInput(false);
                    }
                }
                return;
            }

            if (!controlsPaused) return;
            controlsPaused = false;
            foreach (Behaviour behaviour in stopped)
                if (behaviour != null) behaviour.enabled = true;
            stopped.Clear();
        }

        private void StopBehaviour(Behaviour behaviour)
        {
            if (behaviour == null || behaviour == this || !behaviour.enabled) return;
            if (!stopped.Contains(behaviour)) stopped.Add(behaviour);
            behaviour.enabled = false;
        }

        private void RestorePauseSnapshot()
        {
            if (!hasPauseSnapshot) return;
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            if (caster != null) caster.SetInputBlocked(previousInputBlocked);
            SetGameplayControlsEnabled(true);
            hasPauseSnapshot = false;
        }

        private void OnDisable()
        {
            // 씬 종료나 매니저 제거 때 전역 일시정지가 남지 않도록 복원합니다.
            RestorePauseSnapshot();
        }
    }
}
