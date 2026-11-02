using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>타이틀 메뉴 동작과 플레이 씬 전환을 담당합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class TitleMenuController : MonoBehaviour
    {
        public const string TitleSceneName = "TitleScene";
        public const string GameplaySceneName = "MainGame";

        [SerializeField] private Button startButton;
        [SerializeField] private Button controlsButton;
        [SerializeField] private Button quitButton;
        [SerializeField] private Button closeControlsButton;
        [SerializeField] private GameObject controlsPanel;

        private bool isTransitioning;

        private void Awake()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
            if (controlsPanel != null) controlsPanel.SetActive(false);
        }

        private void Start()
        {
            AudioService.PlayLoop(WardShiftSoundId.TitleMusic);
        }

        private void OnEnable()
        {
            if (startButton != null) startButton.onClick.AddListener(StartNewGame);
            if (controlsButton != null) controlsButton.onClick.AddListener(OpenControls);
            if (quitButton != null) quitButton.onClick.AddListener(QuitGame);
            if (closeControlsButton != null) closeControlsButton.onClick.AddListener(CloseControls);
        }

        private void OnDisable()
        {
            if (startButton != null) startButton.onClick.RemoveListener(StartNewGame);
            if (controlsButton != null) controlsButton.onClick.RemoveListener(OpenControls);
            if (quitButton != null) quitButton.onClick.RemoveListener(QuitGame);
            if (closeControlsButton != null) closeControlsButton.onClick.RemoveListener(CloseControls);
        }

        private void StartNewGame()
        {
            if (isTransitioning) return;
            AudioService.StopLoop(WardShiftSoundId.TitleMusic);
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            isTransitioning = true;
            SetMenuButtonsInteractable(false);
            PrepareSceneTransition();
            SceneManager.LoadScene(GameplaySceneName, LoadSceneMode.Single);
        }

        private void OpenControls()
        {
            if (isTransitioning || controlsPanel == null) return;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            controlsPanel.SetActive(true);
        }

        private void CloseControls()
        {
            if (controlsPanel == null) return;
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
            controlsPanel.SetActive(false);
        }

        private void QuitGame()
        {
            AudioService.Play2D(WardShiftSoundId.UiButtonClick);
#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>게임 오버와 엔딩 화면에서 타이틀로 돌아갈 때 사용합니다.</summary>
        public static void ReturnToTitle()
        {
            PrepareSceneTransition();
            SceneManager.LoadScene(TitleSceneName, LoadSceneMode.Single);
        }

        private static void PrepareSceneTransition()
        {
            Time.timeScale = 1f;
            Cursor.lockState = CursorLockMode.None;
            Cursor.visible = true;
        }

        private void SetMenuButtonsInteractable(bool interactable)
        {
            if (startButton != null) startButton.interactable = interactable;
            if (controlsButton != null) controlsButton.interactable = interactable;
            if (quitButton != null) quitButton.interactable = interactable;
            if (closeControlsButton != null) closeControlsButton.interactable = interactable;
        }
    }
}
