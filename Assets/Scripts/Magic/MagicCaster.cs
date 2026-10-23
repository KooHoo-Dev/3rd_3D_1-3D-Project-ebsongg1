using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;

namespace WardShift
{
    /// <summary>조준 갱신 이후 입력을 검사하고 충전과 해제 판정을 전달.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(PlayerInteractor))]
    [DefaultExecutionOrder(10002)]
    public sealed class MagicCaster : MonoBehaviour
    {
        [Header("연결")]
        [SerializeField] private PlayerInteractor interactor;
        [SerializeField, Min(0.1f)] private float cursePatchPurifyRange = 2.25f;
        [Header("입력")]
        [SerializeField] private InputAction castAction = new InputAction("Cast", InputActionType.Button, "<Mouse>/leftButton");
        [SerializeField] private InputAction sealAction = new InputAction("SelectSeal", InputActionType.Button, "<Keyboard>/1");
        [SerializeField] private InputAction purifyAction = new InputAction("SelectPurify", InputActionType.Button, "<Keyboard>/2");
        [SerializeField] private InputAction stabilizeAction = new InputAction("SelectStabilize", InputActionType.Button, "<Keyboard>/3");
        [Tooltip("메뉴를 열 때 SetInputBlocked(true), 닫을 때 false를 전달.")]
        [SerializeField] private bool inputBlocked;
        [Header("실행 상태 확인")]
        [SerializeField] private MagicType selectedMagic = MagicType.Seal;
        [SerializeField] private MagicTarget castingTarget;
        private bool waitingForRelease;
        private bool continuousPurifySession;
        private bool hasFocus = true;
        private bool inputEnabled;
        private float resultUntil;
        public MagicType SelectedMagic => selectedMagic;
        public MagicTarget CastingTarget => castingTarget;
        /// <summary>저주 오염 패치는 선택한 정화 마법의 짧은 사거리 안에서만 조준 대상으로 반환합니다.</summary>
        public MagicTarget AimedTarget
        {
            get
            {
                if (interactor == null || !interactor.isActiveAndEnabled) return null;
                MagicTarget target = interactor.CurrentTarget;
                if (target == null || !target.IsAvailable) return null;
                if (selectedMagic == MagicType.Purify && target.GetComponent<CursePatch>() != null)
                {
                    Vector3 offset = target.transform.position - transform.position;
                    offset.y = 0f;
                    float range = Mathf.Max(0.1f, cursePatchPurifyRange);
                    if (offset.sqrMagnitude > range * range) return null;
                }
                if (selectedMagic == MagicType.Stabilize
                    && target.GetComponentInParent<ManaDeviceVisuals>() is ManaDeviceVisuals device)
                {
                    Vector3 offset = target.transform.position - transform.position;
                    offset.y = 0f;
                    float range = Mathf.Max(0.1f, device.StabilizeRange);
                    if (offset.sqrMagnitude > range * range) return null;
                }
                return target;
            }
        }
        public float Progress => castingTarget != null ? castingTarget.Progress : 0f;
        public bool ShowingResult => Time.unscaledTime < resultUntil;
        public bool ShowingCompletion => ShowingResult && LastResult == MagicCastResult.Success;
        public string CompletedTargetName { get; private set; }
        public bool WaitingForRelease => waitingForRelease;
        public MagicCastResult LastResult { get; private set; }
        public float ResultProgress { get; private set; }
        public float ResultStableStart { get; private set; }
        public float ResultStableEnd { get; private set; }
        public bool ResultUsesTiming { get; private set; }
        public bool InputBlocked => inputBlocked || !hasFocus || !inputEnabled || Time.timeScale <= 0f
            || (Cursor.lockState != CursorLockMode.Locked && EventSystem.current != null
                && EventSystem.current.IsPointerOverGameObject());

        private void Awake()
        {
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
        }

        private void OnEnable()
        {
            inputEnabled = true;
            castAction.Enable();
            sealAction.Enable();
            purifyAction.Enable();
            stabilizeAction.Enable();
            waitingForRelease = true;
        }

        /// <summary>메뉴 등 게임 입력을 막아야 하는 상태를 연결. 진행 중 시전은 취소.</summary>
        public void SetInputBlocked(bool blocked)
        {
            inputBlocked = blocked;
            if (blocked)
            {
                CancelCast();
                continuousPurifySession = false;
                waitingForRelease = true;
            }
        }

        /// <summary>새 DAY를 시작할 때 이전 시전 결과와 대상을 지움.</summary>
        public void ResetForNewDay()
        {
            CancelCast();
            continuousPurifySession = false;
            LastResult = MagicCastResult.None;
            ResultProgress = 0f;
            CompletedTargetName = null;
            resultUntil = 0f;
            waitingForRelease = castAction.IsPressed();
        }

        /// <summary>선택 마법을 변경하며 진행 중 시전을 취소.</summary>
        public void SelectMagic(MagicType magic)
        {
            if (InputBlocked || selectedMagic == magic) return;
            CancelCast();
            continuousPurifySession = false;
            selectedMagic = magic;
            resultUntil = 0f;
            waitingForRelease = castAction.IsPressed();
        }

        private void LateUpdate()
        {
            bool pressed = castAction.IsPressed();
            if (InputBlocked)
            {
                CancelCast();
                waitingForRelease = true;
                return;
            }
            if (sealAction.WasPressedThisFrame()) SelectMagic(MagicType.Seal);
            if (purifyAction.WasPressedThisFrame()) SelectMagic(MagicType.Purify);
            if (stabilizeAction.WasPressedThisFrame()) SelectMagic(MagicType.Stabilize);

            // Only explicitly configured non-timing Purify targets may chain while held.
            MagicTarget aimed = AimedTarget;
            if (selectedMagic == MagicType.Purify
                && (continuousPurifySession || (aimed != null && aimed.AllowHeldPurifyChain)))
            {
                UpdateContinuousPurify(pressed, aimed);
                return;
            }
            continuousPurifySession = false;

            // 해제도 Raycast 갱신 이후 처리: 같은 프레임의 조준 이탈을 성공으로 판정하지 않음.
            if (castingTarget != null)
            {
                MagicTarget target = castingTarget;
                if (AimedTarget != target || target.RequiredMagic != selectedMagic || !target.IsCasting)
                {
                    CancelCast();
                    waitingForRelease = pressed;
                    return;
                }
                if (castAction.WasReleasedThisFrame() || !pressed)
                {
                    CaptureFeedback(target);
                    castingTarget = null;
                    ShowResult(target.ReleaseCast());
                    waitingForRelease = pressed;
                    return;
                }
                Charge(target);
                return;
            }
            // 파괴된 대상의 Unity null도 안전하게 정리.
            castingTarget = null;
            if (waitingForRelease)
            {
                if (!pressed) waitingForRelease = false;
                return;
            }
            if (!pressed || !castAction.WasPressedThisFrame()) return;
            if (aimed == null || aimed.RequiredMagic != selectedMagic) return;
            castingTarget = aimed;
            resultUntil = 0f;
            Charge(aimed);
        }

        private void UpdateContinuousPurify(bool pressed, MagicTarget aimed)
        {
            if (!pressed)
            {
                if (castingTarget != null)
                {
                    MagicTarget target = castingTarget;
                    castingTarget = null;
                    CaptureFeedback(target);
                    ShowResult(target.ReleaseCast());
                }
                continuousPurifySession = false;
                waitingForRelease = false;
                return;
            }

            if (castingTarget != null && (aimed != castingTarget || !castingTarget.IsCasting))
            {
                MagicTarget previous = castingTarget;
                castingTarget = null;
                if (previous.IsCasting)
                {
                    CaptureFeedback(previous);
                    previous.CancelCast();
                    ShowResult(MagicCastResult.Canceled);
                }
            }

            if (castingTarget != null)
            {
                ChargeContinuousPurify(castingTarget);
                return;
            }

            if (aimed == null || !aimed.AllowHeldPurifyChain) return;
            castingTarget = aimed;
            continuousPurifySession = true;
            resultUntil = 0f;
            ChargeContinuousPurify(aimed);
        }

        private void ChargeContinuousPurify(MagicTarget target)
        {
            CaptureFeedback(target);
            ResultProgress = Mathf.Clamp01(target.Progress + Time.deltaTime / Mathf.Max(0.1f, target.CastDuration));
            target.AdvanceCast(MagicType.Purify, Time.deltaTime);
            if (target == null)
            {
                castingTarget = null;
                return;
            }
            if (target.IsCasting) return;
            castingTarget = null;
            ShowResult(target.LastResult);
        }

        private void Charge(MagicTarget target)
        {
            CaptureFeedback(target);
            // 실패 후 대상의 진행도가 초기화되기 전에 결과 표시용 충전량을 저장.
            ResultProgress = Mathf.Clamp01(target.Progress + Time.deltaTime / Mathf.Max(0.1f, target.CastDuration));
            target.AdvanceCast(selectedMagic, Time.deltaTime);
            if (target == null) { castingTarget = null; waitingForRelease = true; return; }
            if (target.IsCasting) return;
            castingTarget = null;
            ShowResult(target.LastResult);
            waitingForRelease = true;
        }

        private void CaptureFeedback(MagicTarget target)
        {
            CompletedTargetName = target.DisplayName;
            ResultProgress = target.Progress;
            ResultStableStart = target.StableStart;
            ResultStableEnd = target.StableEnd;
            ResultUsesTiming = target.UseTimingCast;
        }

        private void ShowResult(MagicCastResult result)
        {
            LastResult = result;
            resultUntil = result == MagicCastResult.None ? 0f : Time.unscaledTime + 1.5f;
        }

        private void CancelCast()
        {
            MagicTarget target = castingTarget;
            castingTarget = null;
            if (target == null) return;
            CaptureFeedback(target);
            target.CancelCast();
            ShowResult(MagicCastResult.Canceled);
        }

        private void OnApplicationFocus(bool focused)
        {
            hasFocus = focused;
            if (!focused) { CancelCast(); waitingForRelease = true; }
        }

        private void OnDisable()
        {
            inputEnabled = false;
            CancelCast();
            continuousPurifySession = false;
            waitingForRelease = true;
            // canceled 콜백으로 판정하지 않으므로 액션 비활성화가 성공을 발생시키지 않음.
            castAction.Disable(); sealAction.Disable(); purifyAction.Disable(); stabilizeAction.Disable();
        }

        private void OnDestroy()
        {
            castAction.Dispose(); sealAction.Dispose(); purifyAction.Dispose(); stabilizeAction.Dispose();
        }
    }
}
