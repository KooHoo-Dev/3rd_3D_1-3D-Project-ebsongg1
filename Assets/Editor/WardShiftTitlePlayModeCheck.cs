using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace WardShift.Editor
{
    /// <summary>TitleScene 메뉴 전환과 새 DAY 1 진입을 Play Mode에서 확인합니다.</summary>
    [InitializeOnLoad]
    public static class WardShiftTitlePlayModeCheck
    {
        private const string RunKey = "WardShift.TitlePlayModeCheck.Running";
        private const string PhaseKey = "WardShift.TitlePlayModeCheck.Phase";
        private const string AssertionKey = "WardShift.TitlePlayModeCheck.Assertions";
        private const string TitleScenePath = "Assets/Scenes/TitleScene.unity";
        private const string OutputPath = "Logs/WardShiftTitlePlayModeCheck.txt";
        private static double phaseDeadline;

        static WardShiftTitlePlayModeCheck()
        {
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(RunKey, false))
                EditorApplication.update += Tick;
        }

        [MenuItem("WARD SHIFT/Run Title Play Mode Check")]
        public static void RunBatch()
        {
            if (EditorApplication.isPlayingOrWillChangePlaymode)
                throw new InvalidOperationException("Play Mode가 이미 실행 중입니다.");

            EditorSceneManager.OpenScene(TitleScenePath, OpenSceneMode.Single);
            SessionState.SetBool(RunKey, true);
            SessionState.SetInt(PhaseKey, 0);
            SessionState.SetInt(AssertionKey, 0);
            SetDeadline();
            Directory.CreateDirectory("Logs");
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (!SessionState.GetBool(RunKey, false)) return;
            if (state == PlayModeStateChange.EnteredPlayMode)
            {
                SetDeadline();
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.ExitingPlayMode)
            {
                bool success = SessionState.GetInt(PhaseKey, -1) == 7;
                Finish(success, success
                    ? "Title start, controls, failure return, ending return and quit passed."
                    : "Play Mode exited before all title transition checks completed.");
            }
        }

        private static void Tick()
        {
            if (!SessionState.GetBool(RunKey, false) || !Application.isPlaying) return;
            if (EditorApplication.timeSinceStartup > phaseDeadline)
            {
                Finish(false, "Timed out waiting for scene/state at phase "
                    + SessionState.GetInt(PhaseKey, -1) + ".");
                return;
            }

            try
            {
                int phase = SessionState.GetInt(PhaseKey, 0);
                switch (phase)
                {
                    case 0:
                        if (SceneManager.GetActiveScene().name != TitleMenuController.TitleSceneName) return;
                        Check(Cursor.lockState == CursorLockMode.None && Cursor.visible,
                            "Title shows and unlocks the cursor.");
                        Check(Object.FindFirstObjectByType<UnityEngine.EventSystems.EventSystem>() != null,
                            "Title has an EventSystem.");
                        TitleMenuController titleManager = Object.FindFirstObjectByType<TitleMenuController>();
                        if (titleManager == null)
                            throw new InvalidOperationException("TitleManager was not found.");
                        Button controlsButton = GetField<Button>(titleManager, "controlsButton");
                        Button closeButton = GetField<Button>(titleManager, "closeControlsButton");
                        Button startButton = GetField<Button>(titleManager, "startButton");
                        GameObject controlsPanel = GetField<GameObject>(titleManager, "controlsPanel");
                        if (controlsButton == null || closeButton == null || controlsPanel == null)
                            throw new InvalidOperationException("Title controls UI references are missing.");
                        controlsButton.onClick.Invoke();
                        Check(controlsPanel.activeSelf, "Controls button opens its overlay.");
                        Check(controlsPanel.GetComponent<UnityEngine.UI.Image>().raycastTarget,
                            "Controls overlay blocks menu clicks behind it.");
                        closeButton.onClick.Invoke();
                        Check(!controlsPanel.activeSelf, "Close button returns to the title menu.");
                        startButton.onClick.Invoke();
                        SetPhase(1);
                        break;

                    case 1:
                        if (SceneManager.GetActiveScene().name != TitleMenuController.GameplaySceneName) return;
                        DayFlowManager day = Object.FindFirstObjectByType<DayFlowManager>();
                        if (day == null || day.State != DayState.Playing) return;
                        Check(day.CurrentDay == 1, "Start loads MainGame at DAY 1.");
                        Check(CountDayManagers() == 1
                            && Object.FindFirstObjectByType<TitleMenuController>() == null,
                            "Single scene load leaves one gameplay manager and no title manager.");
                        if (Application.isBatchMode)
                        {
                            StarterAssets.StarterAssetsInputs playerInputs =
                                Object.FindFirstObjectByType<StarterAssets.StarterAssetsInputs>();
                            Check(playerInputs != null && playerInputs.cursorLocked,
                                "DAY 1 player input is configured for a locked gameplay cursor.");
                            Debug.Log("Title Play Mode check note: runtime cursor lock is controlled by the "
                                + "focused game window and cannot be observed in batch mode.");
                        }
                        else
                        {
                            Check(Cursor.lockState == CursorLockMode.Locked && !Cursor.visible,
                                "DAY 1 locks and hides the cursor.");
                        }
                        day.SetDayAndStateForTest(1, DayState.DayFailed);
                        SetPhase(2);
                        break;

                    case 2:
                        Button failureTitleButton = FindResultTitleButton("DayFailedPanel");
                        if (failureTitleButton == null || !failureTitleButton.gameObject.activeInHierarchy) return;
                        failureTitleButton.onClick.Invoke();
                        SetPhase(3);
                        break;

                    case 3:
                        if (SceneManager.GetActiveScene().name != TitleMenuController.TitleSceneName) return;
                        Check(CountDayManagers() == 0,
                            "Failure return unloads the gameplay manager.");
                        Check(Cursor.lockState == CursorLockMode.None && Cursor.visible && Time.timeScale == 1f,
                            "Failure return unlocks the cursor and restores time.");
                        FindButton("StartButton").onClick.Invoke();
                        SetPhase(4);
                        break;

                    case 4:
                        if (SceneManager.GetActiveScene().name != TitleMenuController.GameplaySceneName) return;
                        DayFlowManager restartedDay = Object.FindFirstObjectByType<DayFlowManager>();
                        if (restartedDay == null || restartedDay.State != DayState.Playing) return;
                        Check(CountDayManagers() == 1
                            && Object.FindFirstObjectByType<TitleMenuController>() == null,
                            "Restart creates one gameplay manager without a stale title manager.");
                        Check(restartedDay.CurrentDay == 1, "Starting again creates a fresh DAY 1 session.");
                        restartedDay.SetDayAndStateForTest(7, DayState.Ending);
                        SetPhase(5);
                        break;

                    case 5:
                        Button endingTitleButton = FindResultTitleButton("DayEndingPanel");
                        if (endingTitleButton == null || !endingTitleButton.gameObject.activeInHierarchy) return;
                        endingTitleButton.onClick.Invoke();
                        SetPhase(6);
                        break;

                    case 6:
                        if (SceneManager.GetActiveScene().name != TitleMenuController.TitleSceneName) return;
                        Check(CountDayManagers() == 0,
                            "DAY 7 ending return unloads the gameplay manager.");
                        Check(Cursor.lockState == CursorLockMode.None && Cursor.visible && Time.timeScale == 1f,
                            "DAY 7 ending return unlocks the cursor and restores time.");
                        SetPhase(7);
                        FindButton("QuitButton").onClick.Invoke();
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static Button FindButton(string name)
        {
            foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (button.name == name) return button;
            return null;
        }

        private static Button FindResultTitleButton(string panelName)
        {
            foreach (Button button in Object.FindObjectsByType<Button>(FindObjectsInactive.Include,
                         FindObjectsSortMode.None))
                if (button.name == "TitleButton" && button.transform.parent != null
                    && button.transform.parent.name == panelName)
                    return button;
            return null;
        }

        private static int CountDayManagers() => Object.FindObjectsByType<DayFlowManager>(
            FindObjectsInactive.Include, FindObjectsSortMode.None).Length;

        private static T GetField<T>(object target, string fieldName)
        {
            var field = target.GetType().GetField(fieldName,
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return field != null ? (T)field.GetValue(target) : default;
        }

        private static void Check(bool condition, string message)
        {
            int count = SessionState.GetInt(AssertionKey, 0) + 1;
            SessionState.SetInt(AssertionKey, count);
            if (!condition) throw new InvalidOperationException(message);
            Debug.Log("Title Play Mode check passed: " + message);
        }

        private static void SetPhase(int phase)
        {
            SessionState.SetInt(PhaseKey, phase);
            SetDeadline();
        }

        private static void SetDeadline() => phaseDeadline = EditorApplication.timeSinceStartup + 12.0;

        private static void Finish(bool success, string message)
        {
            EditorApplication.update -= Tick;
            SessionState.SetBool(RunKey, false);
            string report = (success ? "PASS " : "FAIL ") + message
                + Environment.NewLine + "Assertions: " + SessionState.GetInt(AssertionKey, 0);
            File.WriteAllText(OutputPath, report);
            Debug.Log("[WARD SHIFT TITLE CHECK] " + report);
            if (EditorApplication.isPlaying) EditorApplication.isPlaying = false;
            EditorApplication.Exit(success ? 0 : 1);
        }
    }
}
