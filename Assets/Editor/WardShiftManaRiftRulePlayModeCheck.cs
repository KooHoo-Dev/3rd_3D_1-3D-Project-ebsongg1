using System;
using System.Linq;
using System.Reflection;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>
    /// - ManaRift 성장 단계 확인
    /// - 진압 보상 및 파열 처리 확인
    /// - 두 현상 동시 성장과 HUD 표시 확인
    /// </summary>
    [InitializeOnLoad]
    public static class WardShiftManaRiftRulePlayModeCheck
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string RunKey = "WardShift.ManaRiftRules.Running";
        private const string BatchKey = "WardShift.ManaRiftRules.Batch";
        private const string FinishKey = "WardShift.ManaRiftRules.Finish";
        private const string ResultKey = "WardShift.ManaRiftRules.Result";
        private const string MessageKey = "WardShift.ManaRiftRules.Message";
        private const string StartedKey = "WardShift.ManaRiftRules.Started";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static int phase;
        private static double phaseStartedAt;
        private static AnomalySpawner spawner;
        private static DayFlowManager day;
        private static WardStatus ward;
        private static PlayerInventory inventory;
        private static ManaRift currentRift;
        private static int crystalBaseline;
        private static int resolutionEvents;
        private static int ruptureEvents;
        private static AnomalyEndReason lastEndReason;
        private static float simultaneousStabilityBaseline;
        private static ManaRift[] simultaneousRifts;

        static WardShiftManaRiftRulePlayModeCheck()
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

        [MenuItem("Tools/WARD SHIFT/ManaRift 규칙 검사 실행")]
        public static void RunFromMenu() => StartCheck(false);

        /// <summary>
        /// - Unity 배치 모드에서 검사 실행
        /// - 실패 시 0이 아닌 종료 코드 반환
        /// </summary>
        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool batch)
        {
            if (SessionState.GetBool(StartedKey, false)) return;
            SessionState.SetBool(StartedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[WARD SHIFT 규칙 검사] 검사 전에 플레이 모드를 종료하세요.");
                if (batch) EditorApplication.Exit(1);
                return;
            }

            if (batch) EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include) == null)
            {
                Debug.LogError("[WARD SHIFT 규칙 검사] AnomalySpawner를 찾을 수 없습니다.");
                if (batch) EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(BatchKey, batch);
            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(FinishKey, false);
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetString(MessageKey, string.Empty);
            phase = 0;
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
            spawner = UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>();
            day = UnityEngine.Object.FindFirstObjectByType<DayFlowManager>();
            ward = UnityEngine.Object.FindFirstObjectByType<WardStatus>();
            inventory = day != null ? day.Inventory : null;
            if (spawner == null || day == null || ward == null || inventory == null)
            {
                Finish(false, "MainGame 필수 참조 누락: AnomalySpawner, DayFlowManager, WardStatus 또는 PlayerInventory");
                return;
            }

            SetPrivateField(spawner, "spawnOnStart", false);
            SetPrivateField(spawner, "firstSpawnDelaySeconds", 100f);
            SetPrivateField(spawner, "spawnIntervalSeconds", 100f);
            SetPrivateField(spawner, "maximumConcurrentAnomalies", 2);
            phase = -1;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            try
            {
                if (EditorApplication.timeSinceStartup - phaseStartedAt > 18d)
                {
                    Finish(false, $"규칙 검사 {phase}단계 시간 초과");
                    return;
                }

                switch (phase)
                {
                    case -1:
                        if (day.State != DayState.Playing) return;
                        spawner.SetSpawningEnabled(false);
                        ward.PauseGameForUI();
                        spawner.ClearActiveAnomalies();
                        phase = 0;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        return;

                    case 0:
                        if (spawner.ActiveAnomalyCount != 0
                            || EditorApplication.timeSinceStartup - phaseStartedAt < 0.15d) return;
                        currentRift = SpawnOne();
                        if (currentRift == null) { Finish(false, "1단계 보상 검사 이상현상 생성 실패"); return; }
                        if (Mathf.Abs(GetPrivateField<float>(currentRift, "stageTwoAt") - 20f) > 0.001f
                            || Mathf.Abs(GetPrivateField<float>(currentRift, "stageThreeAt") - 40f) > 0.001f
                            || Mathf.Abs(GetPrivateField<float>(currentRift, "ruptureAt") - 60f) > 0.001f
                            || Mathf.Abs(GetPrivateField<float>(currentRift, "stageTwoStabilityLossPerSecond") - 0.5f) > 0.001f
                            || Mathf.Abs(GetPrivateField<float>(currentRift, "stageThreeStabilityLossPerSecond") - 1f) > 0.001f
                            || Mathf.Abs(GetPrivateField<float>(currentRift, "ruptureStabilityDamage") - 20f) > 0.001f)
                        {
                            Finish(false, "인스펙터 기본값 불일치: 성장 20/40/60초, 안정도 초당 0.5/1.0 감소, 파열 피해 20");
                            return;
                        }
                        crystalBaseline = inventory.ManaCrystals;
                        resolutionEvents = 0;
                        currentRift.ResolutionCompleted += OnResolutionCompleted;
                        if (currentRift.DangerStage != 1 || currentRift.ResolutionReward != 1
                            || !ResolveFirstSealOnly(currentRift)
                            || !currentRift.IsHazardActive || currentRift.CompletedSealCount != 1
                            || resolutionEvents != 0 || inventory.ManaCrystals != crystalBaseline)
                        {
                            Finish(false, "1단계 부분 진압 오류: 보상 지급 또는 이상현상 종료 발생");
                            return;
                        }
                        if (!ResolveSecondSeal(currentRift)) { Finish(false, "두 번째 봉인 지점 진압 실패"); return; }
                        phase = 1;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;

                    case 1:
                        if (currentRift != null && !currentRift.IsEnded) return;
                        if (resolutionEvents != 1 || inventory.ManaCrystals != crystalBaseline + 1)
                        {
                            Finish(false, $"1단계 보상 중복 또는 누락: 이벤트 {resolutionEvents}회, 결정 {inventory.ManaCrystals - crystalBaseline}개");
                            return;
                        }
                        currentRift = SpawnOne();
                        if (currentRift == null) { Finish(false, "2단계 보상 검사 이상현상 생성 실패"); return; }
                        Configure(currentRift, 0.1f, 0.2f, 100f, 0.5f, 1f, 20f);
                        currentRift.AdvanceHazard(0.11f);
                        crystalBaseline = inventory.ManaCrystals;
                        resolutionEvents = 0;
                        currentRift.ResolutionCompleted += OnResolutionCompleted;
                        if (currentRift.DangerStage != 2 || currentRift.ResolutionReward != 2
                            || !ResolveFirstSealOnly(currentRift) || !currentRift.IsHazardActive
                            || currentRift.CompletedSealCount != 1 || resolutionEvents != 0
                            || inventory.ManaCrystals != crystalBaseline)
                        {
                            Finish(false, "2단계 부분 진압 오류: 보상 없이 현상이 유지되지 않음");
                            return;
                        }
                        if (!ResolveSecondSeal(currentRift)) { Finish(false, "2단계 두 번째 봉인 지점 진압 실패"); return; }
                        phase = 2;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;

                    case 2:
                        if (currentRift != null && !currentRift.IsEnded) return;
                        if (resolutionEvents != 1 || inventory.ManaCrystals != crystalBaseline + 2)
                        {
                            Finish(false, $"2단계 보상 중복 또는 누락: 이벤트 {resolutionEvents}회, 결정 {inventory.ManaCrystals - crystalBaseline}개");
                            return;
                        }
                        currentRift = SpawnOne();
                        if (currentRift == null) { Finish(false, "3단계 보상 검사 이상현상 생성 실패"); return; }
                        Configure(currentRift, 0.1f, 0.2f, 100f, 0.5f, 1f, 20f);
                        currentRift.AdvanceHazard(0.21f);
                        crystalBaseline = inventory.ManaCrystals;
                        resolutionEvents = 0;
                        currentRift.ResolutionCompleted += OnResolutionCompleted;
                        if (currentRift.DangerStage != 3 || currentRift.ResolutionReward != 3
                            || !Resolve(currentRift))
                        {
                            Finish(false, "3단계 이상현상 진압 또는 보상 검사 실패");
                            return;
                        }
                        phase = 3;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;

                    case 3:
                        if (currentRift != null && !currentRift.IsEnded) return;
                        if (resolutionEvents != 1 || inventory.ManaCrystals != crystalBaseline + 3)
                        {
                            Finish(false, $"3단계 보상 중복 또는 누락: 이벤트 {resolutionEvents}회, 결정 {inventory.ManaCrystals - crystalBaseline}개");
                            return;
                        }
                        currentRift = SpawnOne();
                        if (currentRift == null) { Finish(false, "파열 검사 이상현상 생성 실패"); return; }
                        Configure(currentRift, 0.1f, 0.2f, 0.3f, 0.5f, 1f, 20f);
                        crystalBaseline = inventory.ManaCrystals;
                        resolutionEvents = 0;
                        ruptureEvents = 0;
                        lastEndReason = AnomalyEndReason.Removed;
                        currentRift.ResolutionCompleted += OnResolutionCompleted;
                        currentRift.Ended += OnRiftEnded;
                        float firstPart = currentRift.AdvanceHazard(0.1f);
                        float secondPart = currentRift.AdvanceHazard(0.1f);
                        float rupturePart = currentRift.AdvanceHazard(0.1f);
                        float repeatedPart = currentRift.AdvanceHazard(0.1f);
                        if (Mathf.Abs(firstPart) > 0.001f || Mathf.Abs(secondPart - 0.05f) > 0.002f
                            || Mathf.Abs(rupturePart - 20.1f) > 0.002f || Mathf.Abs(repeatedPart) > 0.001f
                            || currentRift.IsHazardActive
                            || !currentRift.IsEnded || resolutionEvents != 0 || ruptureEvents != 1
                            || lastEndReason != AnomalyEndReason.Ruptured || inventory.ManaCrystals != crystalBaseline)
                        {
                            Finish(false, $"파열 처리 오류: 안정도 피해 20 1회 적용 및 보상 없는 종료 확인 실패 (구간 피해={firstPart:F3}/{secondPart:F3}/{rupturePart:F3}, 재호출 피해={repeatedPart:F3}, 진압 이벤트={resolutionEvents}, 파열 이벤트={ruptureEvents}, 결정={inventory.ManaCrystals - crystalBaseline}개)");
                            return;
                        }
                        phase = 4;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;

                    case 4:
                        if (spawner.ActiveAnomalyCount != 0
                            || EditorApplication.timeSinceStartup - phaseStartedAt < 0.15d) return;
                        simultaneousRifts = SpawnPair();
                        if (simultaneousRifts == null || simultaneousRifts.Length != 2)
                        {
                            Finish(false, "ManaRift 두 개 동시 생성 실패");
                            return;
                        }
                        foreach (ManaRift rift in simultaneousRifts)
                            Configure(rift, 0.15f, 0.3f, 100f, 0.5f, 1f, 20f);
                        simultaneousStabilityBaseline = ward.Stability;
                        string[] labels = simultaneousRifts.Select(rift => rift.SpawnLocationLabel).ToArray();
                        ward.ResumeGameFromUI();
                        phase = 5;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        Debug.Log($"[WARD SHIFT 규칙 검사] 동시 생성 위치: {string.Join(" / ", labels)}");
                        break;

                    case 5:
                        if (simultaneousRifts.Any(rift => rift == null || rift.Age < 0.65f)) return;
                        float expectedLoss = simultaneousRifts.Sum(ExpectedStabilityLoss);
                        float actualLoss = simultaneousStabilityBaseline - ward.Stability;
                        string hudText = GetHudText();
                        bool hudHasBoth = simultaneousRifts.All(rift => hudText.Contains(rift.SpawnLocationLabel)
                            && (hudText.Contains("단계까지") || hudText.Contains("파열까지")));
                        if (simultaneousRifts.Any(rift => rift.DangerStage != 3)
                            || Mathf.Abs(actualLoss - expectedLoss) > 0.04f || !hudHasBoth)
                        {
                            Finish(false, $"동시 성장 검사 실패: 단계={string.Join("/", simultaneousRifts.Select(rift => rift.DangerStage))}, 안정도 예상 감소={expectedLoss:F3}, 실제 감소={actualLoss:F3}, HUD={hudText.Replace('\n', '|')}");
                            return;
                        }
                        ward.PauseGameForUI();
                        spawner.ClearActiveAnomalies();
                        phase = 6;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        Debug.Log($"[WARD SHIFT 규칙 검사] 동시 3단계 성장: 안정도 예상 감소 {expectedLoss:F3}, 실제 감소 {actualLoss:F3}, HUD {hudText.Replace('\n', '|')}");
                        break;

                    case 6:
                        if (spawner.ActiveAnomalyCount != 0) return;
                        ward.ResumeGameFromUI();
                        Finish(true, "검사 통과\n1~3단계 진압 및 결정 보상\n부분 진압 보상 미지급\n파열 피해 1회 적용 및 보상 미지급\n두 현상 동시 성장, 안정도 감소, HUD 표시");
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, $"규칙 검사 {phase}단계 예외: {exception}");
            }
        }

        private static ManaRift SpawnOne()
        {
            spawner.SetSpawningEnabled(true);
            spawner.SpawnRift();
            spawner.SetSpawningEnabled(false);
            return spawner.ActiveAnomalies.OfType<ManaRift>().LastOrDefault();
        }

        private static ManaRift[] SpawnPair()
        {
            spawner.SetSpawningEnabled(true);
            spawner.SpawnRift();
            spawner.SpawnRift();
            spawner.SetSpawningEnabled(false);
            return spawner.ActiveAnomalies.OfType<ManaRift>().ToArray();
        }

        private static void Configure(ManaRift rift, float second, float third, float rupture,
            float secondLoss, float thirdLoss, float ruptureDamage)
        {
            SetPrivateField(rift, "stageTwoAt", second);
            SetPrivateField(rift, "stageThreeAt", third);
            SetPrivateField(rift, "ruptureAt", rupture);
            SetPrivateField(rift, "stageTwoStabilityLossPerSecond", secondLoss);
            SetPrivateField(rift, "stageThreeStabilityLossPerSecond", thirdLoss);
            SetPrivateField(rift, "ruptureStabilityDamage", ruptureDamage);
        }

        private static bool ResolveFirstSealOnly(ManaRift rift)
        {
            MagicTarget first = GetPrivateField<MagicTarget>(rift, "sealPoint01");
            return CompleteSeal(first);
        }

        private static bool ResolveSecondSeal(ManaRift rift)
        {
            MagicTarget second = GetPrivateField<MagicTarget>(rift, "sealPoint02");
            return CompleteSeal(second);
        }

        private static bool Resolve(ManaRift rift)
        {
            return ResolveFirstSealOnly(rift) && ResolveSecondSeal(rift);
        }

        private static bool CompleteSeal(MagicTarget target)
        {
            if (target == null || !target.IsAvailable) return false;
            target.AdvanceCast(MagicType.Seal, target.CastDuration * 0.7f);
            return target.IsCasting && target.ReleaseCast() == MagicCastResult.Success;
        }

        private static float ExpectedStabilityLoss(ManaRift rift)
        {
            float age = rift.Age;
            float second = GetPrivateField<float>(rift, "stageTwoAt");
            float third = GetPrivateField<float>(rift, "stageThreeAt");
            float stageTwo = Mathf.Max(0f, Mathf.Min(age, third) - second) * GetPrivateField<float>(rift, "stageTwoStabilityLossPerSecond");
            float stageThree = Mathf.Max(0f, age - third) * GetPrivateField<float>(rift, "stageThreeStabilityLossPerSecond");
            return stageTwo + stageThree;
        }

        private static string GetHudText()
        {
            WardStatusHUD hud = UnityEngine.Object.FindFirstObjectByType<WardStatusHUD>();
            TMP_Text text = hud != null ? GetPrivateField<TMP_Text>(hud, "statusText") : null;
            return text != null ? text.text : string.Empty;
        }

        private static void OnResolutionCompleted(AnomalyBehaviour anomaly) => resolutionEvents++;

        private static void OnRiftEnded(AnomalyBehaviour anomaly, AnomalyEndReason reason)
        {
            ruptureEvents++;
            lastEndReason = reason;
        }

        private static T GetPrivateField<T>(object target, string fieldName)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            return (T)field.GetValue(target);
        }

        private static void SetPrivateField(object target, string fieldName, object value)
        {
            FieldInfo field = target.GetType().GetField(fieldName, PrivateInstance);
            if (field == null) throw new MissingFieldException(target.GetType().Name, fieldName);
            field.SetValue(target, value);
        }

        private static void Finish(bool success, string message)
        {
            Debug.Log($"[WARD SHIFT 규칙 검사 {(success ? "성공" : "실패")}]\n- {message.Replace("\n", "\n- ")}");
            if (ward != null) ward.ResumeGameFromUI();
            if (spawner != null) spawner.SetSpawningEnabled(false);
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
            bool batch = SessionState.GetBool(BatchKey, false);
            int result = SessionState.GetInt(ResultKey, 1);
            string message = SessionState.GetString(MessageKey, "결과 미확인");
            SessionState.SetBool(FinishKey, false);
            SessionState.SetBool(BatchKey, false);
            SessionState.SetBool(StartedKey, false);
            if (batch) EditorApplication.Exit(result);
            else Debug.Log($"[WARD SHIFT 규칙 검사 종료]\n- {message.Replace("\n", "\n- ")}");
        }

        private static bool IsBatchTestCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-executeMethod"
                    && args[i + 1] == "WardShift.Editor.WardShiftManaRiftRulePlayModeCheck.RunMainGameInBatch")
                    return true;
            return false;
        }

        private static void StartBatchCommandLineCheck()
        {
            if (!SessionState.GetBool(StartedKey, false)) StartCheck(true);
        }
    }
}
