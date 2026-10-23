using System;
using System.Linq;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>
    /// - MainGame 씬의 Play Mode 이상현상 검사
    /// - 테스트용 시간 설정은 런타임 인스턴스에만 적용
    /// - 테스트 종료 후 임시 설정 미저장
    /// </summary>
    [InitializeOnLoad]
    public static class WardShiftAnomalyPlayModeCheck
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string RunKey = "WardShift.AnomalyCheck.Running";
        private const string BatchKey = "WardShift.AnomalyCheck.Batch";
        private const string FinishKey = "WardShift.AnomalyCheck.Finish";
        private const string ResultKey = "WardShift.AnomalyCheck.Result";
        private const string MessageKey = "WardShift.AnomalyCheck.Message";
        private const string StartedKey = "WardShift.AnomalyCheck.Started";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static double startedAt;
        private static double phaseStartedAt;
        private static int phase;
        private static ManaRift firstRift;
        private static ManaRift secondRift;
        private static Vector3 firstSpawnPosition;
        private static float secondAgeAtReuse;

        static WardShiftAnomalyPlayModeCheck()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(RunKey, false) && EditorApplication.isPlaying)
                StartRuntimeCheck();
            if (SessionState.GetBool(FinishKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += RestoreSceneAndFinish;
            if (IsBatchTestCommandLine() && !SessionState.GetBool(StartedKey, false))
                EditorApplication.delayCall += StartBatchCommandLineCheck;
        }

        [MenuItem("Tools/WARD SHIFT/Run Multi Anomaly Play Mode Check")]
        public static void RunFromMenu() => StartCheck(false);

        /// <summary>
        /// - Unity Batch Mode 실행 진입점
        /// - 검사 실패 시 0이 아닌 종료 코드 반환
        /// </summary>
        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool batch)
        {
            if (SessionState.GetBool(StartedKey, false)) return;
            SessionState.SetBool(StartedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("WARD SHIFT anomaly check: stop Play Mode before starting the check.");
                if (batch) EditorApplication.Exit(1);
                return;
            }

            if (batch) EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            AnomalySpawner spawner = UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include);
            if (spawner == null)
            {
                Debug.LogError("WARD SHIFT anomaly check: AnomalySpawner was not found.");
                if (batch) EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(FinishKey, false);
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetString(MessageKey, string.Empty);
            phase = 0;
            firstRift = null;
            secondRift = null;
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
                StartRuntimeCheck();
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(FinishKey, false))
                RestoreSceneAndFinish();
        }

        private static void StartRuntimeCheck()
        {
            AnomalySpawner spawner = UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>();
            if (spawner == null)
            {
                Finish(false, "AnomalySpawner was not found after entering Play Mode");
                return;
            }
            // - 테스트용 값은 Play Mode 인스턴스에만 설정
            // - Play Mode 종료 시 임시 값 폐기
            SetPrivateField(spawner, "firstSpawnDelaySeconds", 0.2f);
            SetPrivateField(spawner, "spawnIntervalSeconds", 0.35f);
            SetPrivateField(spawner, "maximumConcurrentAnomalies", 2);
            SetPrivateField(spawner, "selectedPointIndex", 0);
            SetPrivateField(spawner, "randomizePoint", false);
            // This check isolates the ManaRift scheduler rules; the curse-specific check covers mixed types.
            SetPrivateField(spawner, "additionalAnomalyPrefabs", Array.Empty<AnomalyBehaviour>());
            SetPrivateField(spawner, "spawnOnStart", false);
            SetPrivateField(spawner, "spawnCountdown", 0.2f);
            phase = 0;
            startedAt = EditorApplication.timeSinceStartup;
            phaseStartedAt = startedAt;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            if (EditorApplication.timeSinceStartup - startedAt > 24d)
            {
                Finish(false, "timed out before all runtime checks completed");
                return;
            }

            AnomalySpawner spawner = UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>();
            DayFlowManager day = UnityEngine.Object.FindFirstObjectByType<DayFlowManager>();
            WardStatus ward = UnityEngine.Object.FindFirstObjectByType<WardStatus>();
            if (spawner == null || day == null || ward == null) return;

            switch (phase)
            {
                case 0:
                    if (day.State != DayState.Playing) return;
                    ManaRift[] pair = CurrentRifts(spawner);
                    if (pair.Length < 2) return;
                    firstRift = pair[0];
                    secondRift = pair[1];
                    firstSpawnPosition = firstRift.transform.position;
                    if (Vector3.Distance(firstSpawnPosition, secondRift.transform.position) < 0.25f)
                    {
                        Finish(false, "the first two ManaRifts used the same spawn point");
                        return;
                    }
                    ConfigureFastIndependentGrowth(firstRift);
                    ConfigureFastIndependentGrowth(secondRift);
                    if (spawner.ActiveAnomalyCount != 2 || spawner.ActiveAnomalyCount > spawner.MaximumConcurrentAnomalies)
                    {
                        Finish(false, "the active anomaly cap was not respected");
                        return;
                    }
                    phase = 1;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 1:
                    if (firstRift == null || secondRift == null) { Finish(false, "a test rift disappeared before sealing"); return; }
                    if (spawner.ActiveAnomalyCount > spawner.MaximumConcurrentAnomalies)
                    {
                        Finish(false, "the periodic scheduler exceeded the active anomaly cap");
                        return;
                    }
                    if (firstRift.DangerStage < 2 || secondRift.DangerStage != 1) return;
                    SetPrivateField(spawner, "spawnIntervalSeconds", 100f);
                    SetPrivateField(spawner, "spawnCountdown", 100f);
                    if (!Resolve(firstRift)) { Finish(false, "could not seal the first ManaRift through its two MagicTargets"); return; }
                    phase = 2;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 2:
                    if (firstRift != null) return;
                    if (secondRift == null || !spawner.ActiveAnomalies.Contains(secondRift)
                        || spawner.ActiveAnomalyCount != 1)
                    {
                        Finish(false, "sealing one ManaRift affected the other active anomaly");
                        return;
                    }
                    secondAgeAtReuse = secondRift.Age;
                    spawner.SpawnRift();
                    phase = 3;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 3:
                    if (spawner.ActiveAnomalyCount < 2) return;
                    ManaRift reused = CurrentRifts(spawner).FirstOrDefault(rift => rift != secondRift);
                    if (reused == null || Vector3.Distance(reused.transform.position, firstSpawnPosition) > 0.05f)
                    {
                        Finish(false, "the resolved spawn point was not reused");
                        return;
                    }
                    if (spawner.ActiveAnomalyCount > spawner.MaximumConcurrentAnomalies)
                    {
                        Finish(false, "manual spawning exceeded the active anomaly cap");
                        return;
                    }
                    phase = 4;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 4:
                    if (secondRift == null || secondRift.Age <= secondAgeAtReuse + 0.2f) return;
                    SetPrivateField(day, "remainingSeconds", 0f);
                    foreach (AnomalyBehaviour anomaly in AnomalyBehaviour.ActiveAnomalies.ToArray())
                        if (anomaly != null) UnityEngine.Object.Destroy(anomaly.gameObject);
                    phase = 5;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 5:
                    if (day.State != DayState.DayClear) return;
                    if (spawner.SpawningEnabled) { Finish(false, "spawning remained enabled after DAY CLEAR"); return; }
                    if (EditorApplication.timeSinceStartup - phaseStartedAt < 0.35d) return;
                    if (spawner.ActiveAnomalyCount != 0) { Finish(false, "a new anomaly spawned after DAY CLEAR"); return; }
                    day.BeginNextDay();
                    phase = 6;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 6:
                    if (day.State != DayState.Playing || spawner.ActiveAnomalyCount < 1) return;
                    MethodInfo fail = typeof(WardStatus).GetMethod("Fail", PrivateInstance);
                    fail?.Invoke(ward, new object[] { "automated anomaly check" });
                    if (day.State != DayState.DayFailed || spawner.SpawningEnabled)
                    {
                        Finish(false, "spawning did not stop after DAY FAILED");
                        return;
                    }
                    int activeAtFailure = spawner.ActiveAnomalyCount;
                    spawner.SpawnRift();
                    if (spawner.ActiveAnomalyCount != activeAtFailure)
                    {
                        Finish(false, "a manual spawn request was accepted after DAY FAILED");
                        return;
                    }
                    phase = 7;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 7:
                    if (EditorApplication.timeSinceStartup - phaseStartedAt < 0.35d) return;
                    if (spawner.ActiveAnomalyCount != 1)
                    {
                        Finish(false, "the active count changed after DAY FAILED");
                        return;
                    }
                    Finish(true, "two independent ManaRifts, cap, anchor reuse, DAY CLEAR and DAY FAILED checks passed");
                    break;
            }
        }

        private static ManaRift[] CurrentRifts(AnomalySpawner spawner)
        {
            return spawner.ActiveAnomalies.OfType<ManaRift>().Where(rift => rift != null).ToArray();
        }

        private static void ConfigureFastIndependentGrowth(ManaRift rift)
        {
            SetPrivateField(rift, "stageTwoAt", 2f);
            SetPrivateField(rift, "stageThreeAt", 4f);
            SetPrivateField(rift, "ruptureAt", 100f);
            SetPrivateField(rift, "stageTwoStabilityLossPerSecond", 0f);
            SetPrivateField(rift, "stageThreeStabilityLossPerSecond", 0f);
            SetPrivateField(rift, "ruptureStabilityDamage", 0f);
        }

        private static bool Resolve(ManaRift rift)
        {
            MagicTarget first = GetPrivateField<MagicTarget>(rift, "sealPoint01");
            MagicTarget second = GetPrivateField<MagicTarget>(rift, "sealPoint02");
            return CompleteSeal(first) && CompleteSeal(second);
        }

        private static bool CompleteSeal(MagicTarget target)
        {
            if (target == null || !target.IsAvailable) return false;
            target.AdvanceCast(MagicType.Seal, target.CastDuration * 0.7f);
            if (!target.IsCasting) return false;
            return target.ReleaseCast() == MagicCastResult.Success;
        }

        private static T GetPrivateField<T>(object target, string fieldName) where T : class
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            return field != null ? field.GetValue(target) as T : null;
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        private static void Finish(bool success, string message)
        {
            Debug.Log($"WARD SHIFT anomaly check {(success ? "PASSED" : "FAILED")}: {message}");
            SessionState.SetBool(RunKey, false);
            SessionState.SetBool(FinishKey, true);
            SessionState.SetInt(ResultKey, success ? 0 : 1);
            SessionState.SetString(MessageKey, message);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }

        private static void RestoreSceneAndFinish()
        {
            if (!SessionState.GetBool(FinishKey, false)) return;
            // - 모든 값은 Play Mode 인스턴스에서만 변경
            // - 원본 씬 복원 및 저장 불필요

            bool batch = SessionState.GetBool(BatchKey, false);
            int result = SessionState.GetInt(ResultKey, 1);
            string message = SessionState.GetString(MessageKey, "unknown result");
            SessionState.SetBool(FinishKey, false);
            SessionState.SetBool(BatchKey, false);
            SessionState.SetBool(StartedKey, false);
            if (batch) EditorApplication.Exit(result);
            else Debug.Log($"WARD SHIFT anomaly check finished: {message}");
        }

        private static bool IsBatchTestCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-executeMethod"
                    && args[i + 1] == "WardShift.Editor.WardShiftAnomalyPlayModeCheck.RunMainGameInBatch")
                    return true;
            return false;
        }

        private static void StartBatchCommandLineCheck()
        {
            if (!SessionState.GetBool(StartedKey, false)) StartCheck(true);
        }

    }
}
