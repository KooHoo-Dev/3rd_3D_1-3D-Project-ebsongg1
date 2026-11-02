using System;
using UnityEngine;
using UnityEngine.InputSystem;

namespace WardShift
{
    /// <summary>MagicCaster의 입력 액션 생명주기와 프레임별 입력 읽기만 담당합니다.</summary>
    internal sealed class MagicCasterInput : IDisposable
    {
        internal readonly struct Selection
        {
            public readonly bool SealPressed;
            public readonly bool PurifyPressed;
            public readonly bool StabilizePressed;
            public readonly int ScrollDirection;

            public Selection(bool sealPressed, bool purifyPressed, bool stabilizePressed,
                int scrollDirection)
            {
                SealPressed = sealPressed;
                PurifyPressed = purifyPressed;
                StabilizePressed = stabilizePressed;
                ScrollDirection = scrollDirection;
            }
        }

        private readonly InputAction castAction;
        private readonly InputAction sealAction;
        private readonly InputAction purifyAction;
        private readonly InputAction stabilizeAction;
        private readonly InputAction scrollMagicAction = new InputAction(
            "ScrollMagic", InputActionType.PassThrough, "<Mouse>/scroll/y");

        public MagicCasterInput(InputAction castAction, InputAction sealAction,
            InputAction purifyAction, InputAction stabilizeAction)
        {
            this.castAction = castAction;
            this.sealAction = sealAction;
            this.purifyAction = purifyAction;
            this.stabilizeAction = stabilizeAction;
        }

        public bool IsCastPressed => castAction.IsPressed();
        public bool WasCastPressedThisFrame => castAction.WasPressedThisFrame();
        public bool WasCastReleasedThisFrame => castAction.WasReleasedThisFrame();

        public Selection ReadSelection()
        {
            bool sealPressed = sealAction.WasPressedThisFrame();
            bool purifyPressed = purifyAction.WasPressedThisFrame();
            bool stabilizePressed = stabilizeAction.WasPressedThisFrame();
            int scrollDirection = 0;
            if (!sealPressed && !purifyPressed && !stabilizePressed
                && scrollMagicAction.WasPerformedThisFrame())
            {
                float scroll = scrollMagicAction.ReadValue<float>();
                if (Mathf.Abs(scroll) > 0.01f) scrollDirection = scroll > 0f ? 1 : -1;
            }
            return new Selection(sealPressed, purifyPressed, stabilizePressed, scrollDirection);
        }

        public void Enable()
        {
            castAction.Enable();
            sealAction.Enable();
            purifyAction.Enable();
            stabilizeAction.Enable();
            scrollMagicAction.Enable();
        }

        public void Disable()
        {
            castAction.Disable();
            sealAction.Disable();
            purifyAction.Disable();
            stabilizeAction.Disable();
            scrollMagicAction.Disable();
        }

        public void Dispose()
        {
            castAction.Dispose();
            sealAction.Dispose();
            purifyAction.Dispose();
            stabilizeAction.Dispose();
            scrollMagicAction.Dispose();
        }
    }
}
