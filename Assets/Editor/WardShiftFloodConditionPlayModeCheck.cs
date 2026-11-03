using System;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>스토리 모드의 범람 조건 제거와 DAY 1–7 진행을 MainGame Play Mode에서 확인합니다.</summary>
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
        private static WardShiftHUDLayout layout;
        private static Day5HUD dayHud;
        private static bool retryPending;
        private static bool retriedDayFour;
        private static bool runtimeAssertionsDone;
        private static string unexpectedException;

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

        [MenuItem("Tools/WARD SHIFT/Verify Story Mode Without Flood Conditions")]
        public static void RunFromMenu() => StartCheck(false);

        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool batch)
        {
            if (SessionState.GetBool(StartedKey, false)) return;
            SessionState.SetBool(StartedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                FinishWithoutPlayMode(batch, "Stop Play Mode before starting the story mode check.");
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
            layout = UnityEngine.Object.FindFirstObjectByType<WardShiftHUDLayout>();
            dayHud = UnityEngine.Object.FindFirstObjectByType<Day5HUD>();
            if (day == null || spawner == null || inventory == null || layout == null || dayHud == null)
            {
                Finish(false, "MainGame is missing DayFlowManager, AnomalySpawner, PlayerInventory, or story UI.");
                return;
            }

            retryPending = false;
            retriedDayFour = false;
            runtimeAssertionsDone = false;
            unexpectedException = string.Empty;
            Application.logMessageReceived += CaptureRuntimeException;
            spawner.SetSpawningEnabled(false);
            spawner.SetSpawnWindowOpen(false);
            spawner.ClearActiveAnomalies();

            startedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            if (EditorApplication.timeSinceStartup - startedAt > 60d)
            {
                Finish(false, "DAY 1–7 story progression timed out.");
                return;
            }
            if (!string.IsNullOrEmpty(unexpectedException))
            {
                Finish(false, unexpectedException);
                return;
            }

            try
            {
                if (day.State != DayState.Playing) return;
                if (!runtimeAssertionsDone)
                {
                    AssertStoryCodeHasNoFloodReferences();
                    AssertNoConditionUi();
                    AssertExposureAndPotion();
                    AssertBaseAnomalyRewardsAndNoDuplicatePayout();
                    runtimeAssertionsDone = true;
                    return;
                }

                if (retryPending)
                {
                    if (day.State != DayState.Playing) return;
                    retryPending = false;
                    Assert(day.CurrentDay == 4, "Retry changed the current DAY.");
                    AssertNoConditionUi();
                }

                if (day.State != DayState.Playing) return;
                AssertNoConditionUi();

                if (day.CurrentDay == 4 && !retriedDayFour)
                {
                    Invoke(day.Status, "Fail", "스토리 모드 재도전 검사");
                    Assert(day.State == DayState.DayFailed, "DAY 4 did not enter FAILED for retry check.");
                    day.RetryCurrentDay();
                    retryPending = true;
                    retriedDayFour = true;
                    return;
                }

                CompleteCurrentDay();
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void CompleteCurrentDay()
        {
            int completedDay = day.CurrentDay;
            Invoke(day, "CompleteDay");
            RefreshHud();

            if (completedDay == day.FinalDay)
            {
                Assert(day.State == DayState.Ending, "DAY 7 did not transition to the existing ending state.");
                Assert(GetPrivateField<GameObject>(dayHud, "endingPanel").activeSelf,
                    "DAY 7 ending panel was not shown.");
                AssertNoConditionUi();
                Finish(true, "DAY 1–7 stayed condition-free; no passive recovery, potion, and fixed one-crystal anomaly rewards passed; upgrade, shop, retry, and DAY 7 ending passed.");
                return;
            }

            Assert(day.State == DayState.DayClear, $"DAY {completedDay} did not show DAY CLEAR.");
            Assert(day.DaySupplyPending, $"DAY {completedDay} did not preserve the supply shop step.");
            Assert(GetPrivateField<GameObject>(dayHud, "clearPanel").activeSelf,
                $"DAY {completedDay} clear panel was not shown.");
            AssertNoConditionUi();

            if (completedDay == 1)
            {
                int before = inventory.ManaCrystals;
                int price = day.GetNextUpgradePrice(DayUpgradeType.ContaminationResistance);
                inventory.AddManaCrystals(price);
                Assert(day.TryPurchaseUpgrade(DayUpgradeType.ContaminationResistance),
                    "DAY 1 permanent contamination resistance could not be purchased.");
                Assert(day.ContaminationResistanceLevel == 1 && day.SupplyPanelOpen,
                    "DAY 1 upgrade did not preserve the normal upgrade-to-shop flow.");
                Assert(inventory.ManaCrystals == before,
                    "DAY 1 upgrade did not spend its normal crystal price.");
            }
            else
            {
                day.BeginNextDay();
                Assert(day.SupplyPanelOpen, $"DAY {completedDay} did not open the supply shop after skipping upgrades.");
            }

            if (completedDay == 2)
            {
                int potionCount = inventory.GetItemCount(ItemType.PurificationPotion);
                Assert(day.TryPurchaseDaySupply(ItemType.PurificationPotion, out SupplyPurchaseBlockReason failure),
                    "DAY 2 could not buy a potion from the supply shop: " + failure);
                Assert(inventory.GetItemCount(ItemType.PurificationPotion) == potionCount + 1,
                    "DAY 2 potion purchase did not add exactly one potion.");
            }

            Assert(day.SupplyPanelOpen, $"DAY {completedDay} had no supply phase before the next DAY.");
            Assert(day.CompleteSupplyShopAndBeginNextDay(),
                $"DAY {completedDay} could not leave the supply shop and begin the next DAY.");
            Assert(day.CurrentDay == completedDay + 1 && day.State == DayState.Playing,
                $"DAY {completedDay} transition did not resume directly into DAY {completedDay + 1}.");
            Assert(Mathf.Approximately(day.PotionRecoveryAmount, PlayerInventory.PotionRecovery)
                && Mathf.Approximately(day.NextDayPotionRecoveryAmount, PlayerInventory.PotionRecovery),
                $"DAY {day.CurrentDay} potion recovery was not the base amount.");
            if (completedDay == 1)
            {
                Assert(day.ContaminationResistanceLevel == 1
                    && Mathf.Approximately(GetPrivateField<float>(day.Player, "contaminationGainMultiplier"), 0.92f),
                    "Contamination resistance upgrade no longer reduces gained contamination.");
            }
            AssertNoConditionUi();
        }

        private static void AssertStoryCodeHasNoFloodReferences()
        {
            string projectRoot = Directory.GetParent(Application.dataPath).FullName;
            string[] storyFiles =
            {
                "Assets/Scripts/Day/DayFlowManager.cs",
                "Assets/Scripts/Day/DayFlowManager.Api.cs",
                "Assets/Scripts/Day/DaySession.cs",
                "Assets/Scripts/Day/DayAnomalyRewardController.cs",
                "Assets/Scripts/Day/DayPlayerUpgradeEffects.cs",
                "Assets/Scripts/Day/DayUpgradeController.cs",
                "Assets/Scripts/Player/PlayerContamination.cs",
                "Assets/Scripts/UI/DayScreens/Day5HUD.cs",
                "Assets/Scripts/UI/HUD/WardShiftHUDLayout.cs"
            };

            foreach (string relativePath in storyFiles)
                Assert(!File.ReadAllText(Path.Combine(projectRoot, relativePath)).Contains("FloodCondition"),
                    relativePath + " still references the flood condition system.");

            Assert(!File.ReadAllText(Path.Combine(projectRoot, MainGamePath)).Contains("FloodCondition"),
                "MainGame still contains serialized flood condition UI.");
        }

        private static void AssertNoConditionUi()
        {
            Assert(layout.HUDRoot.Find("FloodCondition") == null,
                $"Gameplay HUD has no flood condition panel on DAY {day.CurrentDay}.");
            GameObject clear = GetPrivateField<GameObject>(dayHud, "clearPanel");
            Assert(clear.transform.Find("NextFloodCondition") == null,
                $"DAY clear has no next-condition guide on DAY {day.CurrentDay}.");
            Assert(typeof(WardShiftHUDLayout).GetField("floodText", PrivateInstance) == null,
                "HUD layout retains no serialized flood text reference.");
        }

        private static void AssertExposureAndPotion()
        {
            GameObject playerObject = new GameObject("Temporary Story Check Player");
            GameObject inventoryObject = new GameObject("Temporary Story Check Inventory");
            try
            {
                PlayerContamination player = playerObject.AddComponent<PlayerContamination>();
                SetPrivateField(player, "contamination", 50f);
                player.Advance(1f, Array.Empty<AnomalyBehaviour>());
                Assert(Mathf.Approximately(player.Value, 50f) && Mathf.Approximately(player.ExposurePerSecond, 0f),
                    "Contamination changed without an anomaly or an item.");

                PlayerInventory testInventory = inventoryObject.AddComponent<PlayerInventory>();
                Assert(testInventory.TryAddItem(ItemType.PurificationPotion), "Could not add a test potion.");
                SetPrivateField(player, "contamination", 80f);
                Assert(testInventory.TryUseItem(0, player, null, day.PotionRecoveryAmount),
                    "Base purification potion could not be used.");
                Assert(Mathf.Approximately(player.Value, 50f)
                    && Mathf.Approximately(day.PotionRecoveryAmount, 30f),
                    "Purification potion did not retain its full 30-point base recovery.");
            }
            finally
            {
                UnityEngine.Object.Destroy(playerObject);
                UnityEngine.Object.Destroy(inventoryObject);
            }
        }

        private static void AssertBaseAnomalyRewardsAndNoDuplicatePayout()
        {
            GameObject inactive = new GameObject("Temporary Story Reward Rules");
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
                        Assert(anomaly.ResolutionReward == 1,
                            $"{anomaly.GetType().Name} stage {stage} reward is not fixed at one crystal.");
                    }
                }
            }
            finally
            {
                UnityEngine.Object.Destroy(inactive);
            }

            for (int stage = 1; stage <= 3; stage++)
            {
                int before = inventory.CrystalsEarnedToday;
                GameObject probeObject = new GameObject($"Temporary story reward probe stage {stage}");
                probeObject.SetActive(false);
                CurseAnomaly probe = probeObject.AddComponent<CurseAnomaly>();
                SetPrivateField(probe, "dangerStage", stage);
                Invoke(day, "OnAnomalySpawned", probe);
                Invoke(day, "OnAnomalyResolved", probe);
                Invoke(day, "OnAnomalyResolved", probe);
                Assert(inventory.CrystalsEarnedToday - before == 1,
                    $"Stage {stage} resolution did not pay exactly one crystal.");
                UnityEngine.Object.Destroy(probeObject);
            }
        }

        private static void RefreshHud()
        {
            Invoke(dayHud, "Update");
            Invoke(layout, "LateUpdate");
            Canvas.ForceUpdateCanvases();
        }

        private static void CaptureRuntimeException(string condition, string stackTrace, LogType type)
        {
            if (type == LogType.Exception
                || condition.Contains("NullReferenceException")
                || condition.Contains("MissingReferenceException"))
                unexpectedException = condition + "\n" + stackTrace;
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

        private static void Invoke(object target, string methodName, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PrivateInstance);
            if (method == null) throw new MissingMethodException(target.GetType().Name, methodName);
            method.Invoke(target, args);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            Application.logMessageReceived -= CaptureRuntimeException;
            Debug.Log($"[WARD SHIFT 스토리 모드 검사 {(success ? "성공" : "실패")}]\n- {message.Replace("\n", "\n- ")}");
            if (day != null && day.Status != null) day.Status.ResumeGameFromUI();
            if (spawner != null)
            {
                spawner.SetSpawningEnabled(false);
                spawner.SetSpawnWindowOpen(false);
            }
            SessionState.SetBool(RunKey, false);
            SessionState.SetBool(FinishKey, true);
            SessionState.SetInt(ResultKey, success ? 0 : 1);
            SessionState.SetString(MessageKey, message);
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
            Debug.LogError($"[WARD SHIFT 스토리 모드 검사 실패] {message}");
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
            else Debug.Log($"[WARD SHIFT 스토리 모드 검사 종료]\n- {message.Replace("\n", "\n- ")}");
        }

        private static void StartBatchCommandLineCheck() => StartCheck(true);

        private static bool IsBatchTestCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-executeMethod"
                    && args[i + 1] == "WardShift.Editor.WardShiftFloodConditionPlayModeCheck.RunMainGameInBatch")
                    return true;
            return false;
        }
    }
}
