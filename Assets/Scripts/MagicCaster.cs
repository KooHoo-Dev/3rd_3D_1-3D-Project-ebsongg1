using UnityEngine;
using UnityEngine.InputSystem;

namespace WardShift
{
    /// <summary>마법 선택과 유지 입력을 받아 현재 조준 대상에 시전을 전달.</summary>
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

        [Header("실행 상태 확인")]
        [SerializeField] private MagicType selectedMagic = MagicType.Seal;
        [SerializeField] private MagicTarget castingTarget;
        private bool waitingForRelease;
        private float completionMessageUntil;
        private string completedTargetName;

        public MagicType SelectedMagic => selectedMagic;
        public MagicTarget CastingTarget => castingTarget;
        public MagicTarget AimedTarget => interactor != null && interactor.isActiveAndEnabled
            && interactor.CurrentTarget != null && interactor.CurrentTarget.IsAvailable ? interactor.CurrentTarget : null;
        public float Progress => castingTarget != null ? castingTarget.Progress : 0f;
        public bool ShowingCompletion => Time.unscaledTime < completionMessageUntil;
        public string CompletedTargetName => completedTargetName;
        public bool WaitingForRelease => waitingForRelease;

        private void Awake()
        {
            if (interactor == null) interactor = GetComponent<PlayerInteractor>();
            sealAction.performed += SelectSeal;
            purifyAction.performed += SelectPurify;
            stabilizeAction.performed += SelectStabilize;
            castAction.canceled += ReleaseCast;
        }

        private void OnEnable() => EnableInput();

        private void EnableInput()
        {
            castAction.Enable();
            sealAction.Enable();
            purifyAction.Enable();
            stabilizeAction.Enable();
        }

        private void DisableInput()
        {
            castAction.Disable();
            sealAction.Disable();
            purifyAction.Disable();
            stabilizeAction.Disable();
            CancelCast();
        }

        private void SelectSeal(InputAction.CallbackContext context) => SelectMagic(MagicType.Seal);
        private void SelectPurify(InputAction.CallbackContext context) => SelectMagic(MagicType.Purify);
        private void SelectStabilize(InputAction.CallbackContext context) => SelectMagic(MagicType.Stabilize);

        public void SelectMagic(MagicType magic)
        {
            if (selectedMagic == magic) return;
            CancelCast();
            selectedMagic = magic;
            completionMessageUntil = 0f;
        }

        private void ReleaseCast(InputAction.CallbackContext context)
        {
            // 한 프레임 안에서 놓았다 다시 눌러도 이전 진행도는 초기화.
            waitingForRelease = false;
            CancelCast();
        }

        private void LateUpdate()
        {
            if (Time.timeScale <= 0f || !castAction.IsPressed())
            {
                CancelCast();
                if (!castAction.IsPressed()) waitingForRelease = false;
                return;
            }
            if (waitingForRelease) return;

            MagicTarget target = AimedTarget;
            if (target == null || target.RequiredMagic != selectedMagic)
            {
                CancelCast();
                return;
            }
            if (castingTarget != target)
            {
                CancelCast();
                castingTarget = target;
                completionMessageUntil = 0f;
            }

            if (target.AdvanceCast(selectedMagic, Time.deltaTime))
            {
                completedTargetName = target.DisplayName;
                completionMessageUntil = Time.unscaledTime + 1.5f;
                castingTarget = null;
                // 완료 직후 뒤쪽 대상까지 연속 처리하지 않도록 새 클릭을 기다림.
                waitingForRelease = true;
            }
        }

        private void CancelCast()
        {
            if (castingTarget != null) castingTarget.CancelCast();
            castingTarget = null;
        }

        private void OnApplicationFocus(bool hasFocus)
        {
            if (!hasFocus) DisableInput();
            else if (isActiveAndEnabled) EnableInput();
        }

        private void OnDisable() => DisableInput();

        private void OnDestroy()
        {
            castAction.Dispose();
            sealAction.Dispose();
            purifyAction.Dispose();
            stabilizeAction.Dispose();
        }
    }
}
