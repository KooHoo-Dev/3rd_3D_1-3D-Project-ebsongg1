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
        private static MagicTarget castAcrossDeadline;
        private static Vector3 firstSpawnPosition;
        private static float secondAgeAtReuse;
        private static int cleanupRewardCap;
        private static int crystalsBeforeCleanupResolution;
        private static int activeAtFailure;

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
            castAcrossDeadline = null;
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
            if (!ValidateDaySchedule(spawner, out string scheduleError))
            {
                Finish(false, scheduleError);
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
            spawner.ConfigureForDay(2, 24601);
            SetPrivateField(spawner.ActiveDayProfile, "spawnIntervalSeconds", 0.35f);
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
            if (EditorApplication.timeSinceStartup - startedAt > 45d)
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
                    SetPrivateField(spawner.ActiveDayProfile, "spawnIntervalSeconds", 100f);
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
                    ManaRift castingRift = CurrentRifts(spawner).FirstOrDefault(rift => rift != secondRift);
                    castAcrossDeadline = castingRift != null
                        ? GetPrivateField<MagicTarget>(castingRift, "sealPoint01") : null;
                    if (castAcrossDeadline == null)
                    {
                        Finish(false, "no cast target was available at DAY expiration");
                        return;
                    }
                    SetPrivateField(castAcrossDeadline, "castDuration", 5f);
                    castAcrossDeadline.AdvanceCast(MagicType.Seal, 0.01f);
                    if (!castAcrossDeadline.IsCasting)
                    {
                        Finish(false, "could not start a cast before DAY expiration");
                        return;
                    }
                    cleanupRewardCap = secondRift.DangerStage;
                    day.SetRemainingSecondsForTest(0f);
                    phase = 5;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 5:
                    if (!day.IsCleaningUp) return;
                    if (day.State != DayState.Playing || spawner.SpawningEnabled
                        || spawner.ActiveAnomalyCount != 2)
                    {
                        Finish(false, "DAY expiration did not preserve both anomalies and stop spawning");
                        return;
                    }
                    if (EditorApplication.timeSinceStartup - phaseStartedAt < 0.35d) return;
                    if (spawner.ActiveAnomalyCount != 2)
                    {
                        Finish(false, "an anomaly spawned or disappeared during cleanup");
                        return;
                    }
                    if (castAcrossDeadline == null || !castAcrossDeadline.IsCasting)
                    {
                        Finish(false, "an in-progress cast was interrupted at DAY expiration");
                        return;
                    }
                    MagicCaster caster = UnityEngine.Object.FindFirstObjectByType<MagicCaster>();
                    if (caster == null || caster.RawInputBlocked)
                    {
                        Finish(false, "magic input was blocked during cleanup");
                        return;
                    }
                    WardShiftHUDLayout layout = UnityEngine.Object.FindFirstObjectByType<WardShiftHUDLayout>();
                    if (layout == null || !layout.DayStatusText.text.Contains("잔여 이상현상 정리 중 (2개)"))
                    {
                        Finish(false, "cleanup count was not visible in the DAY HUD");
                        return;
                    }
                    castAcrossDeadline.CancelCast();
                    crystalsBeforeCleanupResolution = day.Inventory.ManaCrystals;
                    SetPrivateField(secondRift, "stageTwoAt", secondRift.Age + 0.1f);
                    SetPrivateField(secondRift, "stageThreeAt", secondRift.Age + 0.2f);
                    phase = 6;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 6:
                    if (secondRift == null || secondRift.DangerStage <= cleanupRewardCap) return;
                    if (!Resolve(secondRift)) { Finish(false, "could not seal an anomaly during cleanup"); return; }
                    phase = 61;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 61:
                    // ManaRift completes the two clicked seals in LateUpdate.
                    if (secondRift != null) return;
                    int expectedCleanupReward = FloodConditionRules.ResolutionReward(
                        cleanupRewardCap, day.ActiveFloodCondition);
                    if (day.Inventory.ManaCrystals != crystalsBeforeCleanupResolution + expectedCleanupReward)
                    {
                        Finish(false, "post-deadline growth increased the resolution reward: expected "
                            + expectedCleanupReward + ", received "
                            + (day.Inventory.ManaCrystals - crystalsBeforeCleanupResolution));
                        return;
                    }
                    ManaRift lastRift = CurrentRifts(spawner).FirstOrDefault(rift => rift != secondRift);
                    if (lastRift == null || !day.IsCleaningUp || day.State != DayState.Playing
                        || !Resolve(lastRift))
                    {
                        Finish(false, "cleanup ended before the final anomaly was resolved");
                        return;
                    }
                    phase = 7;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 7:
                    if (day.State != DayState.DayClear) return;
                    if (day.IsCleaningUp || spawner.SpawningEnabled || spawner.ActiveAnomalyCount != 0)
                    {
                        Finish(false, "DAY CLEAR did not wait for the final resolution");
                        return;
                    }
                    day.BeginNextDay();
                    day.DeferDaySupplyChoice();
                    phase = 8;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 8:
                    if (day.State != DayState.Playing || spawner.ActiveAnomalyCount < 1) return;
                    day.SetRemainingSecondsForTest(0f);
                    phase = 9;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 9:
                    if (!day.IsCleaningUp) return;
                    SetPrivateField(ward, "stability", 0f);
                    phase = 91;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 91:
                    if (!ward.HasFailed) return;
                    if (day.State != DayState.DayFailed || spawner.SpawningEnabled || day.IsCleaningUp)
                    {
                        Finish(false, "failure during cleanup advanced or kept the cleanup state");
                        return;
                    }
                    activeAtFailure = spawner.ActiveAnomalyCount;
                    spawner.SpawnRift();
                    if (spawner.ActiveAnomalyCount != activeAtFailure)
                    {
                        Finish(false, "a manual spawn request was accepted after DAY FAILED");
                        return;
                    }
                    phase = 10;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 10:
                    if (EditorApplication.timeSinceStartup - phaseStartedAt < 0.35d) return;
                    if (day.State != DayState.DayFailed || spawner.ActiveAnomalyCount != activeAtFailure)
                    {
                        Finish(false, "the active count changed after DAY FAILED");
                        return;
                    }
                    day.RetryCurrentDay();
                    phase = 11;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 11:
                    if (day.State != DayState.Playing) return;
                    day.DeferDaySupplyChoice();
                    if (spawner.ActiveAnomalyCount < 1) return;
                    day.SetRemainingSecondsForTest(0f);
                    phase = 111;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 111:
                    if (!day.IsCleaningUp) return;
                    SetPrivateField(ward.Player, "recoveryPerSecond", 0f);
                    SetPrivateField(ward.Player, "contamination", 100f);
                    phase = 112;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 112:
                    if (!ward.HasFailed) return;
                    if (day.State != DayState.DayFailed || day.IsCleaningUp)
                    {
                        Finish(false, "contamination failure during cleanup advanced to DAY CLEAR");
                        return;
                    }
                    day.RetryCurrentDay();
                    phase = 113;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 113:
                    if (day.State != DayState.Playing) return;
                    day.DeferDaySupplyChoice();
                    spawner.SetSpawningEnabled(false);
                    spawner.ClearActiveAnomalies();
                    day.SetRemainingSecondsForTest(0f);
                    phase = 12;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 12:
                    if (day.State != DayState.DayClear) return;
                    if (day.IsCleaningUp || spawner.ActiveAnomalyCount != 0)
                    {
                        Finish(false, "an empty DAY did not clear immediately at expiration");
                        return;
                    }
                    day.BeginNextDay();
                    day.DeferDaySupplyChoice();
                    while (day.CurrentDay < 5 && day.State == DayState.Playing)
                    {
                        day.CheatAdvanceDay();
                        day.DeferDaySupplyChoice();
                    }
                    if (day.CurrentDay != 5 || day.State != DayState.Playing)
                    {
                        Finish(false, "F8 DAY advance failed before the rupture check");
                        return;
                    }
                    phase = 13;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 13:
                    if (spawner.ActiveAnomalyCount < 1) return;
                    day.SetRemainingSecondsForTest(0f);
                    phase = 14;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 14:
                    if (!day.IsCleaningUp) return;
                    ManaRift ruptureRift = CurrentRifts(spawner).FirstOrDefault();
                    if (ruptureRift == null || spawner.ActiveAnomalyCount != 1)
                    {
                        Finish(false, "rupture check expected one active cleanup anomaly");
                        return;
                    }
                    SetPrivateField(ruptureRift, "stageTwoAt", ruptureRift.Age + 0.1f);
                    SetPrivateField(ruptureRift, "stageThreeAt", ruptureRift.Age + 0.2f);
                    SetPrivateField(ruptureRift, "ruptureAt", ruptureRift.Age + 0.3f);
                    SetPrivateField(ruptureRift, "stageTwoStabilityLossPerSecond", 0f);
                    SetPrivateField(ruptureRift, "stageThreeStabilityLossPerSecond", 0f);
                    SetPrivateField(ruptureRift, "ruptureStabilityDamage", 0f);
                    phase = 15;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 15:
                    if (day.State != DayState.DayClear) return;
                    if (spawner.ActiveAnomalyCount != 0)
                    {
                        Finish(false, "ruptured anomaly retained an active slot");
                        return;
                    }
                    day.BeginNextDay();
                    day.DeferDaySupplyChoice();
                    phase = 16;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 16:
                    if (day.CurrentDay != 6 || spawner.ActiveAnomalyCount < 1) return;
                    day.SetRemainingSecondsForTest(0f);
                    phase = 17;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 17:
                    if (!day.IsCleaningUp) return;
                    day.CheatAdvanceDay();
                    day.DeferDaySupplyChoice();
                    if (day.CurrentDay != 7 || day.State != DayState.Playing || day.IsCleaningUp)
                    {
                        Finish(false, "F8 did not advance from cleanup to DAY 7");
                        return;
                    }
                    phase = 18;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 18:
                    if (spawner.ActiveAnomalyCount < 1) return;
                    day.SetRemainingSecondsForTest(0f);
                    phase = 19;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 19:
                    if (!day.IsCleaningUp) return;
                    ManaRift finalRift = CurrentRifts(spawner).FirstOrDefault();
                    if (finalRift == null || !Resolve(finalRift))
                    {
                        Finish(false, "DAY 7 anomaly could not be resolved during cleanup");
                        return;
                    }
                    phase = 20;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    break;

                case 20:
                    if (day.State != DayState.Ending) return;
                    if (day.IsCleaningUp || spawner.ActiveAnomalyCount != 0)
                    {
                        Finish(false, "DAY 7 cleanup did not reach the ending state");
                        return;
                    }
                    Finish(true, "DAY cleanup, reward cap, failure, empty DAY, rupture, DAY 7 ending and F8 passed");
                    break;
            }
        }

        private static ManaRift[] CurrentRifts(AnomalySpawner spawner)
        {
            return spawner.ActiveAnomalies.OfType<ManaRift>().Where(rift => rift != null).ToArray();
        }

        private static bool ValidateDaySchedule(AnomalySpawner spawner, out string error)
        {
            bool[] hasRift = { true, true, true, true, true, true, true };
            bool[] hasCurse = { false, true, true, true, true, true, true };
            bool[] hasDevice = { false, false, true, true, true, true, true };
            int[] concurrentLimits = { 1, 2, 2, 2, 2, 3, 3 };
            float[] intervals = { 17f, 16f, 15f, 12f, 11f, 10f, 9.5f };
            float previousInterval = float.PositiveInfinity;

            for (int i = 0; i < concurrentLimits.Length; i++)
            {
                int dayNumber = i + 1;
                AnomalySpawner.DaySpawnProfile profile = spawner.GetDayProfile(dayNumber);
                if (profile == null || profile.AllowManaRift != hasRift[i]
                    || profile.AllowCurseContamination != hasCurse[i]
                    || profile.AllowManaDeviceOverload != hasDevice[i]
                    || profile.MaximumConcurrentAnomalies != concurrentLimits[i]
                    || !Mathf.Approximately(profile.SpawnIntervalSeconds, intervals[i])
                    || profile.SpawnIntervalSeconds >= previousInterval)
                {
                    error = $"DAY {dayNumber} spawn profile mismatch: interval {profile?.SpawnIntervalSeconds}, expected {intervals[i]}; "
                        + $"rift {profile?.AllowManaRift}, curse {profile?.AllowCurseContamination}, "
                        + $"device {profile?.AllowManaDeviceOverload}, cap {profile?.MaximumConcurrentAnomalies}.";
                    return false;
                }
                previousInterval = profile.SpawnIntervalSeconds;
            }

            error = null;
            return true;
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
            return target != null && target.CompleteInstantly(MagicType.Seal);
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
