using System.Collections.Generic;
using UnityEngine;

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
            previousTimeScale = Time.timeScale;
            previousCursorLock = Cursor.lockState;
            previousCursorVisible = Cursor.visible;
            previousInputBlocked = caster.InputBlocked;
            caster.SetInputBlocked(true);
            if (disableOnFailure != null)
                foreach (Behaviour behaviour in disableOnFailure)
                    if (behaviour != null && behaviour != this && behaviour.enabled)
                    {
                        stopped.Add(behaviour);
                        behaviour.enabled = false;
                    }
            Time.timeScale = 0f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            Debug.Log($"임시 게임 실패: {reason}. Play 모드를 종료 후 다시 시작하세요.", this);
        }

        private void OnDisable()
        {
            if (!HasFailed) return;
            // 실패 담당 오브젝트 제거/씬 종료 시 전역 일시정지가 남지 않게 복원.
            Time.timeScale = previousTimeScale;
            Cursor.lockState = previousCursorLock;
            Cursor.visible = previousCursorVisible;
            if (caster != null) caster.SetInputBlocked(previousInputBlocked);
            foreach (Behaviour behaviour in stopped)
                if (behaviour != null) behaviour.enabled = true;
            stopped.Clear();
        }
    }
}
