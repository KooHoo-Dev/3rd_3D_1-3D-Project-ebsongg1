using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>범람 조건과 DAY 흐름을 MainGame Play Mode에서 자동 검증합니다.</summary>
    [InitializeOnLoad]
    public static class WardShiftFloodConditionPlayModeCheck
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string RunKey = "WardShift.FloodConditionCheck.Running";
        private const string BatchKey = "WardShift.FloodConditionCheck.Batch";
        private const string FinishKey = "WardShift.FloodConditionCheck.Finish";
        private const string ResultKey = "WardShift.FloodConditionCheck.Result";
        private const string MessageKey = "WardShift.FloodConditionCheck.Message";
        private const string StartedKey = "WardShift.FloodConditionCheck.Started";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static double startedAt;
        private static DayFlowManager day;
        private static AnomalySpawner spawner;
        private static PlayerInventory inventory;

        static WardShiftFloodConditionPlayModeCheck()
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

        [MenuItem("Tools/WARD SHIFT/Run Flood Condition Play Mode Check")]
        public static void RunFromMenu() => StartCheck(false);

        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool batch)
        {
            if (SessionState.GetBool(StartedKey, false)) return;
            SessionState.SetBool(StartedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                FinishWithoutPlayMode(batch, "Stop Play Mode before starting the flood condition check.");
                return;
            }

            if (batch) EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<DayFlowManager>(FindObjectsInactive.Include) == null)
            {
                FinishWithoutPlayMode(batch, $"{MainGamePath} has no DayFlowManager.");
                return;
            }

            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(FinishKey, false);
            SessionState.SetInt("WardShift.FloodConditionCheck.FailurePending", 0);
            SessionState.SetInt("WardShift.FloodConditionCheck.RetryPending", 0);
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetString(MessageKey, string.Empty);
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
            day = UnityEngine.Object.FindFirstObjectByType<DayFlowManager>();
            spawner = UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>();
            inventory = day != null ? day.Inventory : null;
            if (day == null || spawner == null || inventory == null)
            {
                Finish(false, "MainGame is missing DayFlowManager, AnomalySpawner, or PlayerInventory.");
                return;
            }

            spawner.SetSpawningEnabled(false);
            spawner.SetSpawnWindowOpen(false);
            spawner.ClearActiveAnomalies();
            AssertStaticRules();
            AssertBuiltInAnomalyRewards();
            AssertSafeZoneRecoveryFormula();
            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            if (EditorApplication.timeSinceStartup - startedAt > 45d)
            {
                Finish(false, "Play Mode progression timed out.");
                return;
            }

            try
            {
                if (SessionState.GetInt("WardShift.FloodConditionCheck.FailurePending", 0) == 1)
                {
                    if (day.State != DayState.DayFailed) return;
                    day.RetryCurrentDay();
                    SessionState.SetInt("WardShift.FloodConditionCheck.FailurePending", 0);
                    SessionState.SetInt("WardShift.FloodConditionCheck.RetryPending", 1);
                    return;
                }

                if (day.CurrentDay == 5
                    && SessionState.GetInt("WardShift.FloodConditionCheck.RetryPending", 0) == 1)
                {
                    if (day.State != DayState.Playing) return;
                    Assert(day.ActiveFloodCondition == FloodCondition.EarlySealBonus,
                        "DAY 5 retry changed the active flood condition.");
                    Assert(inventory.CrystalsEarnedToday == 0,
                        "DAY 5 retry kept resolution rewards from the failed attempt.");
                    int before = inventory.CrystalsEarnedToday;
                    FloodRewardProbeAnomaly retryProbe = CreateProbe(day, 1, false);
                    retryProbe.ResolveForTest();
                    InvokeRewardForTest(day, retryProbe);
                    Assert(inventory.CrystalsEarnedToday - before == 2,
                        "Retry resolution granted a duplicated or incorrect stage 1 reward.");
                    day.CheatAdvanceDay();
                    Assert(day.ActiveFloodCondition == FloodCondition.None,
                        "Completed DAY 5 kept its condition during DAY CLEAR.");
                    Assert(day.NextDayFloodCondition == FloodCondition.PurificationCycle,
                        "The no-repeat selector did not choose the other condition for DAY 6.");
                    day.BeginNextDay();
                    SessionState.SetInt("WardShift.FloodConditionCheck.RetryPending", 0);
                    return;
                }

                if (day.State != DayState.Playing) return;
                switch (day.CurrentDay)
                {
                    case 1:
                        AssertWardStatusSnapshotOnRupture();
                        day.ConfigureFloodConditionTestOverride(true, FloodCondition.PurificationCycle);
                        Assert(day.ActiveFloodCondition == FloodCondition.None,
                            "DAY 1 unexpectedly has a flood condition.");
                        day.CheatAdvanceDay();
                        Assert(day.NextDayFloodCondition == FloodCondition.None,
                            "DAY 2 preview unexpectedly has a flood condition.");
                        day.BeginNextDay();
                        break;

                    case 2:
                        Assert(day.ActiveFloodCondition == FloodCondition.None,
                            "DAY 2 unexpectedly has a flood condition.");
                        day.CheatAdvanceDay();
                        Assert(day.NextDayFloodCondition == FloodCondition.None,
                            "DAY 3 preview unexpectedly has a flood condition.");
                        day.BeginNextDay();
                        break;

                    case 3:
                        Assert(day.ActiveFloodCondition == FloodCondition.None,
                            "DAY 3 unexpectedly has a flood condition.");
                        day.CheatAdvanceDay();
                        Assert(day.NextDayFloodCondition == FloodCondition.PurificationCycle,
                            "The forced DAY 4 preview was not retained on DAY CLEAR.");
                        inventory.AddManaCrystals(
                            day.GetNextUpgradePrice(DayUpgradeType.FacilityDefense));
                        Assert(day.TryPurchaseUpgrade(DayUpgradeType.FacilityDefense),
                            "Could not buy Facility Defense before the forced DAY 4.");
                        Assert(day.GetUpgradeEffectText(DayUpgradeType.FacilityDefense, 1)
                            .Contains("-5%"), "DAY CLEAR did not preview the configured Facility Defense effect.");
                        day.BeginNextDay();
                        break;

                    case 4:
                        Assert(day.ActiveFloodCondition == FloodCondition.PurificationCycle,
                            "DAY 4 did not keep its forced purification condition.");
                        Assert(Mathf.Approximately(day.SafeZoneRecoveryMultiplier, 2f),
                            "DAY 4 safe zone recovery multiplier is not exactly doubled.");
                        Assert(Mathf.Approximately(day.PotionRecoveryAmount, 20f),
                            "Facility Defense changed the 20-point condition-adjusted base potion recovery.");
                        AssertPotionUseAndRepairKitUnaffected();

                        int earnedBeforeCleanup = inventory.CrystalsEarnedToday;
                        CreateProbe(day, 1, false);
                        CreateProbe(day, 1, true);
                        day.ConfigureFloodConditionTestOverride(true, FloodCondition.EarlySealBonus);
                        day.CheatAdvanceDay();
                        Assert(inventory.CrystalsEarnedToday == earnedBeforeCleanup,
                            "Rupture or automatic DAY cleanup granted a resolution reward.");
                        Assert(day.NextDayFloodCondition == FloodCondition.EarlySealBonus,
                            "DAY 5 preview did not show the forced early-seal condition.");
                        day.BeginNextDay();
                        break;

                    case 5:
                        Assert(day.ActiveFloodCondition == FloodCondition.EarlySealBonus,
                            "DAY 5 did not keep its forced early-seal condition.");
                        Assert(Mathf.Approximately(day.SafeZoneRecoveryMultiplier, 1f)
                            && Mathf.Approximately(day.PotionRecoveryAmount, 30f),
                            "Facility Defense changed the base potion recovery on DAY 5.");
                        AssertEarlySealRewardsAndRetry();
                        day.ConfigureFloodConditionTestOverride(false, FloodCondition.None);
                        day.DeferDaySupplyChoice();
                        SetPrivateField(day.Status, "stability", 0f);
                        SessionState.SetInt("WardShift.FloodConditionCheck.FailurePending", 1);
                        return;
                }

                if (day.CurrentDay == 6 && day.State == DayState.Playing)
                {
                    Assert(day.ActiveFloodCondition == FloodCondition.PurificationCycle,
                        "DAY 6 should use the other condition after DAY 5.");
                    Assert(Mathf.Approximately(day.SafeZoneRecoveryMultiplier, 2f)
                        && Mathf.Approximately(day.PotionRecoveryAmount, 20f),
                        "DAY 6 effects accumulated or Facility Defense changed potion recovery.");
                    day.CheatAdvanceDay();
                    day.BeginNextDay();
                }

                if (day.CurrentDay == 7 && day.State == DayState.Playing)
                {
                    Assert(FloodConditionRules.IsAvailableCondition(day.ActiveFloodCondition),
                        "DAY 7 did not receive exactly one valid flood condition.");
                    day.CheatAdvanceDay();
                    Assert(day.State == DayState.Ending
                        && day.ActiveFloodCondition == FloodCondition.None,
                        "DAY 7 could not complete or left its condition active.");
                    Finish(true, "DAY 1–7 progression, both forced conditions, recovery, potion, repair kit, rewards, cleanup, and retry passed.");
                }
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void AssertStaticRules()
        {
            for (int dayNumber = 1; dayNumber <= 3; dayNumber++)
                Assert(FloodConditionRules.SelectForDay(dayNumber, dayNumber,
                    FloodCondition.EarlySealBonus, true, FloodCondition.PurificationCycle)
                    == FloodCondition.None, $"DAY {dayNumber} ignored the condition-free rule.");

            for (int dayNumber = 4; dayNumber <= 7; dayNumber++)
            {
                FloodCondition a = FloodConditionRules.SelectForDay(dayNumber, 1,
                    FloodCondition.None, true, FloodCondition.PurificationCycle);
                FloodCondition b = FloodConditionRules.SelectForDay(dayNumber, 2,
                    FloodCondition.None, true, FloodCondition.EarlySealBonus);
                Assert(a == FloodCondition.PurificationCycle && b == FloodCondition.EarlySealBonus,
                    $"DAY {dayNumber} could not be fixed to each supported condition.");
                FloodCondition random = FloodConditionRules.SelectForDay(dayNumber, dayNumber * 131,
                    FloodCondition.None);
                Assert(FloodConditionRules.IsAvailableCondition(random),
                    $"DAY {dayNumber} selected more than one or an invalid condition.");
            }

            for (int seed = 1; seed <= 64; seed++)
            {
                FloodCondition current = FloodConditionRules.SelectForDay(4, seed, FloodCondition.None);
                FloodCondition next = FloodConditionRules.SelectForDay(5, seed * 37,
                    current);
                Assert(current != next, $"Random selection repeated across adjacent DAYs for seed {seed}.");
            }

            Assert(Mathf.Approximately(FloodConditionRules.SafeZoneRecoveryMultiplier(
                    FloodCondition.PurificationCycle), 2f), "Safe zone multiplier is not 2x.");
            Assert(Mathf.Approximately(FloodConditionRules.PotionRecoveryAmount(30f,
                    FloodCondition.PurificationCycle), 20f)
                && Mathf.Approximately(FloodConditionRules.PotionRecoveryAmount(45f,
                    FloodCondition.PurificationCycle), 30f)
                && Mathf.Approximately(FloodConditionRules.PotionRecoveryAmount(60f,
                    FloodCondition.PurificationCycle), 40f),
                "Condition-adjusted potion amount is not 2/3 of each base upgrade value.");
            Assert(FloodConditionRules.ResolutionReward(1, FloodCondition.EarlySealBonus) == 2
                && FloodConditionRules.ResolutionReward(2, FloodCondition.EarlySealBonus) == 2
                && FloodConditionRules.ResolutionReward(3, FloodCondition.EarlySealBonus) == 3
                && FloodConditionRules.ResolutionReward(1, FloodCondition.PurificationCycle) == 1,
                "Early Seal Bonus changed the wrong reward stages or a different condition.");
        }

        private static void AssertBuiltInAnomalyRewards()
        {
            var inactive = new GameObject("Flood Condition Reward Probe");
            inactive.SetActive(false);
            try
            {
                AnomalyBehaviour[] anomalies =
                {
                    inactive.AddComponent<ManaRift>(),
                    inactive.AddComponent<CurseAnomaly>(),
                    inactive.AddComponent<ManaDeviceAnomaly>()
                };
                foreach (AnomalyBehaviour anomaly in anomalies)
                {
                    for (int stage = 1; stage <= 3; stage++)
                    {
                        SetPrivateField(anomaly, "dangerStage", stage);
                        Assert(anomaly.ResolutionReward == stage,
                            $"{anomaly.GetType().Name} reports an incorrect stage {stage} base reward.");
                        Assert(FloodConditionRules.ResolutionReward(anomaly.ResolutionReward,
                                FloodCondition.EarlySealBonus) == (stage == 1 ? 2 : stage),
                            $"{anomaly.GetType().Name} stage {stage} reward was adjusted incorrectly.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(inactive);
            }
        }

        private static void AssertSafeZoneRecoveryFormula()
        {
            var zoneObject = new GameObject("Temporary Flood Condition Safe Zone");
            var playerObject = new GameObject("Temporary Flood Condition Player");
            try
            {
                BoxCollider box = zoneObject.AddComponent<BoxCollider>();
                box.isTrigger = true;
                box.size = Vector3.one * 10f;
                SafeZone zone = zoneObject.AddComponent<SafeZone>();
                PlayerContamination player = playerObject.AddComponent<PlayerContamination>();
                SetPrivateField(player, "contamination", 50f);
                player.SetSafeZoneRecoveryMultiplier(2f);
                player.Advance(1f, Array.Empty<AnomalyBehaviour>(), new[] { zone });
                Assert(player.InSafeZone && Mathf.Approximately(player.Value, 30f),
                    "A safe zone did not recover 20 contamination in one second at 2x.");

                SetPrivateField(player, "contamination", 50f);
                player.SetSafeZoneRecoveryMultiplier(2f);
                player.Advance(1f, Array.Empty<AnomalyBehaviour>(), new[] { zone });
                Assert(Mathf.Approximately(player.Value, 30f),
                    "Setting the same recovery multiplier twice stacked its effect.");

                SetPrivateField(player, "contamination", 50f);
                player.SetSafeZoneRecoveryMultiplier(1f);
                player.Advance(1f, Array.Empty<AnomalyBehaviour>(), new[] { zone });
                Assert(Mathf.Approximately(player.Value, 40f),
                    "Resetting the condition did not restore the base safe zone recovery rate.");
            }
            finally
            {
                UnityEngine.Object.Destroy(zoneObject);
                UnityEngine.Object.Destroy(playerObject);
            }
        }

        private static void AssertPotionUseAndRepairKitUnaffected()
        {
            PlayerContamination player = day.Player;
            SetPrivateField(player, "contamination", 80f);
            int potionSlot = FindItemSlot(ItemType.PurificationPotion);
            if (potionSlot < 0 && !inventory.TryAddItem(ItemType.PurificationPotion))
                inventory.TryReplaceItem(0, ItemType.PurificationPotion);
            potionSlot = FindItemSlot(ItemType.PurificationPotion);
            Assert(potionSlot >= 0 && inventory.TryUseItem(potionSlot, player, day.Status,
                    day.PotionRecoveryAmount), "The condition-adjusted potion did not apply.");
            Assert(Mathf.Approximately(player.Value, 60f),
                "Facility Defense changed the 2/3 condition factor applied to the 30-point base potion recovery.");

            if (!inventory.TryAddItem(ItemType.EmergencyRepairKit))
                inventory.TryReplaceItem(0, ItemType.EmergencyRepairKit);
            int repairSlot = FindItemSlot(ItemType.EmergencyRepairKit);
            SetPrivateField(day.Status, "stability", 50f);
            Assert(repairSlot >= 0 && inventory.TryUseItem(repairSlot, player, day.Status,
                    day.PotionRecoveryAmount), "The repair kit did not apply on a purification condition day.");
            Assert(Mathf.Approximately(day.Status.Stability, 70f),
                "The flood condition changed the emergency repair kit's 20 stability recovery.");
        }

        private static void AssertWardStatusSnapshotOnRupture()
        {
            if (day.SupplyPanelOpen) day.DeferDaySupplyChoice();
            int earnedBeforeRupture = inventory.CrystalsEarnedToday;
            FloodRewardProbeAnomaly anomaly = CreateProbe(day, 1, false);
            anomaly.RuptureDuringAdvanceForTest();
            MethodInfo method = typeof(WardStatus).GetMethod("LateUpdate", PrivateInstance);
            if (method == null) throw new MissingMethodException(nameof(WardStatus), "LateUpdate");
            method.Invoke(day.Status, null);
            Assert(anomaly.IsEnded, "The rupture probe did not end during WardStatus iteration.");
            Assert(inventory.CrystalsEarnedToday == earnedBeforeRupture,
                "Anomaly rupture granted a resolution reward.");
        }

        private static void AssertEarlySealRewardsAndRetry()
        {
            int baseline = inventory.CrystalsEarnedToday;
            for (int stage = 1; stage <= 3; stage++)
            {
                FloodRewardProbeAnomaly anomaly = CreateProbe(day, stage, false);
                anomaly.ResolveForTest();
                InvokeRewardForTest(day, anomaly);
            }
            Assert(inventory.CrystalsEarnedToday - baseline == 7,
                "Early Seal Bonus did not pay 2, 2, and 3 crystals for stages 1–3.");

            FloodRewardProbeAnomaly rupture = CreateProbe(day, 1, true);
            rupture.RuptureForTest();
            Assert(inventory.CrystalsEarnedToday - baseline == 7,
                "Rupture granted a resolution reward.");
        }

        private static FloodRewardProbeAnomaly CreateProbe(DayFlowManager manager, int stage, bool rupture)
        {
            var gameObject = new GameObject($"Temporary reward probe stage {stage}");
            FloodRewardProbeAnomaly anomaly = gameObject.AddComponent<FloodRewardProbeAnomaly>();
            anomaly.SetStage(stage);
            InvokeOnAnomalySpawnedForTest(manager, anomaly);
            if (rupture) anomaly.RuptureForTest();
            return anomaly;
        }

        private static void InvokeOnAnomalySpawnedForTest(DayFlowManager manager, AnomalyBehaviour anomaly)
        {
            MethodInfo method = typeof(DayFlowManager).GetMethod("OnAnomalySpawned", PrivateInstance);
            if (method == null) throw new MissingMethodException(nameof(DayFlowManager), "OnAnomalySpawned");
            method.Invoke(manager, new object[] { anomaly });
        }

        private static void InvokeRewardForTest(DayFlowManager manager, AnomalyBehaviour anomaly)
        {
            MethodInfo method = typeof(DayFlowManager).GetMethod("OnAnomalyResolved", PrivateInstance);
            if (method == null) throw new MissingMethodException(nameof(DayFlowManager), "OnAnomalyResolved");
            method.Invoke(manager, new object[] { anomaly });
        }

        private static int FindItemSlot(ItemType item)
        {
            for (int i = 0; i < PlayerInventory.SlotCount; i++)
                if (inventory.GetItem(i) == item && inventory.GetQuantity(i) > 0) return i;
            return -1;
        }

        private static T GetPrivateField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            Debug.Log($"[WARD SHIFT 범람 조건 검사 {(success ? "성공" : "실패")}]\n- {message.Replace("\n", "\n- ")}");
            if (day != null && day.Status != null) day.Status.ResumeGameFromUI();
            if (spawner != null) spawner.SetSpawningEnabled(false);
            SessionState.SetBool(RunKey, false);
            SessionState.SetBool(FinishKey, true);
            SessionState.SetInt(ResultKey, success ? 0 : 1);
            SessionState.SetString(MessageKey, message);
            SessionState.SetInt("WardShift.FloodConditionCheck.FailurePending", 0);
            SessionState.SetInt("WardShift.FloodConditionCheck.RetryPending", 0);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }

        private static void FinishWithoutPlayMode(bool batch, string message)
        {
            SessionState.SetBool(StartedKey, false);
            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(FinishKey, true);
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetString(MessageKey, message);
            Debug.LogError($"[WARD SHIFT 범람 조건 검사 실패] {message}");
            if (batch) EditorApplication.Exit(1);
        }

        private static void RestoreSceneAndFinish()
        {
            if (!SessionState.GetBool(FinishKey, false)) return;
            bool batch = SessionState.GetBool(BatchKey, false);
            int result = SessionState.GetInt(ResultKey, 1);
            string message = SessionState.GetString(MessageKey, "결과 미확인");
            SessionState.SetBool(FinishKey, false);
            SessionState.SetBool(BatchKey, false);
            SessionState.SetBool(StartedKey, false);
            if (batch) EditorApplication.Exit(result);
            else Debug.Log($"[WARD SHIFT 범람 조건 검사 종료]\n- {message.Replace("\n", "\n- ")}");
        }

        private static bool IsBatchTestCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-executeMethod"
                    && args[i + 1] == "WardShift.Editor.WardShiftFloodConditionPlayModeCheck.RunMainGameInBatch")
                    return true;
            return false;
        }

        private static void StartBatchCommandLineCheck()
        {
            if (!SessionState.GetBool(StartedKey, false)) StartCheck(true);
        }
    }

}
