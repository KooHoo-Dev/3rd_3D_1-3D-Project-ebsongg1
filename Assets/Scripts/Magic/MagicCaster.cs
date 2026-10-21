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
        private bool hasFocus = true;
        private bool inputEnabled;
        private float resultUntil;
        public MagicType SelectedMagic => selectedMagic;
        public MagicTarget CastingTarget => castingTarget;
        public MagicTarget AimedTarget => interactor != null && interactor.isActiveAndEnabled
            && interactor.CurrentTarget != null && interactor.CurrentTarget.IsAvailable ? interactor.CurrentTarget : null;
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
            if (blocked) { CancelCast(); waitingForRelease = true; }
        }

        /// <summary>선택 마법을 변경하며 진행 중 시전을 취소.</summary>
        public void SelectMagic(MagicType magic)
        {
            if (InputBlocked || selectedMagic == magic) return;
            CancelCast();
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
            MagicTarget aimed = AimedTarget;
            if (aimed == null || aimed.RequiredMagic != selectedMagic) return;
            castingTarget = aimed;
            resultUntil = 0f;
            Charge(aimed);
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
