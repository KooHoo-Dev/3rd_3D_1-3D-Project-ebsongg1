using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using WardShift;
using Object = UnityEngine.Object;

namespace WardShift.Editor
{
    /// <summary>연쇄 봉인 입력, 감지, 제한 시간, 실패 복구와 균열 간 독립성을 검사합니다.</summary>
    [InitializeOnLoad]
    public static class WardShiftSealChainPlayModeCheck
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string RunKey = "WardShift.SealChain.Running";
        private const string BatchKey = "WardShift.SealChain.Batch";
        private const string StartedKey = "WardShift.SealChain.Started";
        private const string ResultKey = "WardShift.SealChain.Result";
        private const string MessageKey = "WardShift.SealChain.Message";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static int phase;
        private static double phaseStartedAt;
        private static bool batch;
        private static float savedTimeScale = 1f;
        private static InputSettings.UpdateMode savedInputUpdateMode;
        private static CursorLockMode savedCursorLock;
        private static ManaRift timeoutRift;
        private static float timeoutStartAge;
        private static ManaRift lifecycleRift;

        static WardShiftSealChainPlayModeCheck()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            if (SessionState.GetBool(RunKey, false) && EditorApplication.isPlaying)
                StartRuntimeCheck();
        }

        [MenuItem("Tools/WARD SHIFT/연쇄 봉인 검사 실행")]
        public static void RunFromMenu() => StartCheck(false);

        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool runBatch)
        {
            if (SessionState.GetBool(StartedKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            if (runBatch) EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include) == null)
            {
                Debug.LogError("[WARD SHIFT 연쇄 봉인 검사] MainGame에서 AnomalySpawner를 찾을 수 없습니다.");
                if (runBatch) EditorApplication.Exit(1);
                return;
            }

            batch = runBatch;
            SessionState.SetBool(StartedKey, true);
            SessionState.SetBool(BatchKey, runBatch);
            SessionState.SetBool(RunKey, true);
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetString(MessageKey, string.Empty);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
                StartRuntimeCheck();
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(StartedKey, false))
                FinishInEditMode();
        }

        private static void StartRuntimeCheck()
        {
            try
            {
                savedTimeScale = Time.timeScale;
                savedInputUpdateMode = InputSystem.settings.updateMode;
                savedCursorLock = Cursor.lockState;
                Time.timeScale = 1f;
                InputSystem.settings.updateMode = InputSettings.UpdateMode.ProcessEventsManually;
                Cursor.lockState = CursorLockMode.Locked;

                AnomalySpawner spawner = Object.FindFirstObjectByType<AnomalySpawner>();
                if (spawner == null) throw new InvalidOperationException("AnomalySpawner를 찾을 수 없습니다.");
                spawner.SetSpawningEnabled(false);
                AssertRandomOrderCanVary();
                AssertOptionalRiftVfx();
                RunQAndClickInputCheck();

                timeoutRift = SpawnRift("SealChain_Timeout");
                timeoutRift.AdvanceHazard(45f);
                timeoutStartAge = timeoutRift.Age;
                MagicTarget first = FirstSeal(timeoutRift);
                first.SetDetected(true, Color.yellow);
                Assert(timeoutRift.SealChainTimeout == 2f, "Inspector 기본 제한 시간은 2초");
                Assert(timeoutRift.TryCompleteSealClick(first), "첫 번째 봉인점 클릭으로 제한 시간 시작");
                Assert(timeoutRift.SealChainTimeRemaining > 1.9f && timeoutRift.SealChainTimeRemaining <= 2f,
                    "첫 번째 성공 직후 남은 시간이 2초");
                Time.timeScale = 0f;
                phase = 0;
                phaseStartedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static void RunQAndClickInputCheck()
        {
            Camera camera = Camera.main;
            MagicDetector detector = Object.FindFirstObjectByType<MagicDetector>();
            MagicCaster caster = Object.FindFirstObjectByType<MagicCaster>();
            PlayerInteractor interactor = Object.FindFirstObjectByType<PlayerInteractor>();
            if (camera == null || detector == null || caster == null || interactor == null)
                throw new InvalidOperationException("감지 카메라, MagicDetector, MagicCaster 또는 PlayerInteractor 누락");

            Keyboard keyboard = InputSystem.GetDevice<Keyboard>() ?? InputSystem.AddDevice<Keyboard>();
            Mouse mouse = InputSystem.GetDevice<Mouse>() ?? InputSystem.AddDevice<Mouse>();
            InputAction senseAction = GetPrivateField<InputAction>(detector, "senseAction");
            InputAction castAction = GetPrivateField<InputAction>(caster, "castAction");
            senseAction.Enable();

            ManaRift rift = SpawnRift("SealChain_Input");
            rift.transform.SetPositionAndRotation(camera.transform.position + camera.transform.forward * 2f,
                camera.transform.rotation);
            Physics.SyncTransforms();
            MagicTarget first = FirstSeal(rift);
            MagicTarget second = SecondSeal(rift);
            Assert(first.GetComponent<SealPointVisual>().CurrentState == SealPointVisual.State.Hidden
                && second.GetComponent<SealPointVisual>().CurrentState == SealPointVisual.State.Hidden,
                "Q 감지 전 두 봉인점이 숨겨짐");
            AssertMarkerVisibility(first, false, "Q 감지 전 첫 번째 파티클 마커 숨김");
            AssertMarkerVisibility(second, false, "Q 감지 전 두 번째 파티클 마커 숨김");

            QueueKeyboard(keyboard, true);
            InputSystem.Update();
            Invoke(detector, "LateUpdate");
            Assert(first.IsDetected && second.IsDetected, "Q 홀드로 기존 감지 시스템이 두 봉인점을 발견");
            Assert(first.GetComponent<SealPointVisual>().CurrentState == SealPointVisual.State.Active
                && second.GetComponent<SealPointVisual>().CurrentState == SealPointVisual.State.Discovered,
                "Q 감지 시 1번만 강조되고 2번은 순서를 드러내지 않는 색상");
            AssertMarkerVisibility(first, true, "Q 감지 시 첫 번째 파티클 마커 표시");
            AssertMarkerVisibility(second, true, "Q 감지 시 두 번째 파티클 마커 표시");

            caster.enabled = false;
            SetPrivateField(caster, "inputEnabled", true);
            SetPrivateField(caster, "hasFocus", true);
            SetPrivateField(caster, "inputBlocked", false);
            SetPrivateField(caster, "waitingForRelease", false);
            SetPrivateField(caster, "selectedMagic", MagicType.Seal);
            castAction.Enable();
            SetPrivateField(interactor, "currentTarget", first);
            int resolved = 0;
            rift.Resolved += _ => resolved++;

            QueueMouse(mouse, true);
            InputSystem.Update();
            Invoke(caster, "LateUpdate");
            Assert(first.IsCompleted && rift.IsSealChainInProgress && rift.CompletedSealCount == 1,
                "첫 번째 좌클릭 즉시 시작하고 첫 성공부터 연쇄 상태가 됨");
            Assert(second.GetComponent<SealPointVisual>().CurrentState == SealPointVisual.State.Active,
                "첫 성공 후 두 번째 봉인점 활성 강조");

            QueueKeyboard(keyboard, false);
            InputSystem.Update();
            Invoke(detector, "LateUpdate");
            Assert(!second.IsDetected && rift.IsSealChainInProgress,
                "연쇄 시작 후 Q를 놓아도 봉인 시도가 유지됨");
            AssertMarkerVisibility(second, true, "연쇄 시작 후 Q를 놓아도 다음 파티클 마커 표시 유지");

            SetPrivateField(interactor, "currentTarget", second);
            QueueMouse(mouse, true);
            InputSystem.Update();
            Invoke(caster, "LateUpdate");
            Assert(!second.IsCompleted && rift.CompletedSealCount == 1,
                "좌클릭을 누른 채 조준을 옮겨도 두 번째 봉인점은 완료되지 않음");

            QueueMouse(mouse, false);
            InputSystem.Update();
            Invoke(caster, "LateUpdate");
            QueueMouse(mouse, true);
            InputSystem.Update();
            Invoke(caster, "LateUpdate");
            Assert(second.IsCompleted && rift.CompletedSealCount == 2,
                "버튼을 놓았다 다시 누르는 두 번째 클릭으로 진압 진행");
            if (!rift.IsEnded) Invoke(rift, "LateUpdate");
            Assert(resolved == 1 && (rift == null || rift.IsEnded), "전체 진압 완료 이벤트가 한 번 발생");

            QueueMouse(mouse, false);
            QueueKeyboard(keyboard, false);
            InputSystem.Update();
            castAction.Disable();
            caster.enabled = true;
            caster.ResetForNewDay();
            Assert(senseAction.enabled, "Q 감지 액션이 활성 상태로 유지됨");
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            try
            {
                double elapsed = EditorApplication.timeSinceStartup - phaseStartedAt;
                if (phase == 0 && elapsed >= 0.25d)
                {
                    Assert(timeoutRift != null && timeoutRift.IsSealChainInProgress
                        && Mathf.Abs(timeoutRift.SealChainTimeRemaining - 2f) < 0.01f,
                        "일시정지 중 제한 시간이 진행되지 않음");
                    Time.timeScale = 1f;
                    phase = 1;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    return;
                }

                if (phase == 1 && elapsed >= 2.15d)
                {
                    MagicTarget first = FirstSeal(timeoutRift);
                    MagicTarget second = SecondSeal(timeoutRift);
                    Assert(!timeoutRift.IsSealChainInProgress && timeoutRift.CompletedSealCount == 0
                        && !first.IsCompleted && !second.IsCompleted,
                        "2초 초과 시 두 봉인점과 연쇄 진행 상태 초기화");
                    Assert(timeoutRift.Age >= timeoutStartAge && timeoutRift.DangerStage == 2,
                        "제한 시간 실패가 성장 타이머와 위험 단계를 초기화하지 않음");
                    Object.Destroy(timeoutRift.gameObject);
                    timeoutRift = SpawnRift("SealChain_WrongClick");
                    timeoutRift.AdvanceHazard(45f);
                    timeoutStartAge = timeoutRift.Age;
                    first = FirstSeal(timeoutRift);
                    MagicTarget secondWrong = SecondSeal(timeoutRift);
                    first.SetDetected(true, Color.yellow);
                    Assert(timeoutRift.TryCompleteSealClick(first), "잘못된 점 검사 준비");
                    bool acceptedWrongPoint = timeoutRift.TryCompleteSealClick(first);
                    Assert(!acceptedWrongPoint && !timeoutRift.IsSealChainInProgress
                        && timeoutRift.CompletedSealCount == 0 && !first.IsCompleted && !secondWrong.IsCompleted,
                        "연쇄 중 잘못된 봉인점 클릭은 두 점을 모두 초기화");
                    Assert(Mathf.Approximately(timeoutRift.Age, timeoutStartAge)
                        && timeoutRift.DangerStage == 2,
                        "잘못된 점 클릭은 성장 타이머와 위험 단계를 변경하지 않음");

                    first.SetDetected(true, Color.yellow);
                    Assert(timeoutRift.TryCompleteSealClick(first)
                        && timeoutRift.TryCompleteSealClick(secondWrong),
                        "실패 후 첫 번째 봉인점부터 다시 시도 가능");
                    int resolved = 0;
                    timeoutRift.Resolved += _ => resolved++;
                    Invoke(timeoutRift, "LateUpdate");
                    Assert(resolved == 1, "재시도 성공이 완료 이벤트를 한 번만 발생");
                    phase = 2;
                    phaseStartedAt = EditorApplication.timeSinceStartup;
                    return;
                }

                if (phase == 2 && elapsed >= 0.2d)
                {
                    lifecycleRift = SpawnRift("SealChain_Lifecycle");
                    MagicTarget first = FirstSeal(lifecycleRift);
                    first.SetDetected(true, Color.yellow);
                    Assert(lifecycleRift.TryCompleteSealClick(first), "수명 주기 검사 준비");
                    lifecycleRift.gameObject.SetActive(false);
                    Assert(!first.IsCompleted && lifecycleRift.CompletedSealCount == 0,
                        "균열 비활성화(라운드 종료) 시 진행 상태가 초기화됨");
                    Object.Destroy(lifecycleRift.gameObject);
                    lifecycleRift = null;

                    ManaRift firstRift = SpawnRift("SealChain_Independent_A");
                    ManaRift secondRift = SpawnRift("SealChain_Independent_B");
                    SetPrivateField(firstRift, "sealChainTimeout", 1.5f);
                    SetPrivateField(secondRift, "sealChainTimeout", 3f);
                    MagicTarget firstA = FirstSeal(firstRift);
                    MagicTarget firstB = FirstSeal(secondRift);
                    MagicTarget secondB = SecondSeal(secondRift);
                    firstA.SetDetected(true, Color.yellow);
                    firstB.SetDetected(true, Color.yellow);
                    Assert(firstRift.TryCompleteSealClick(firstA) && secondRift.TryCompleteSealClick(firstB),
                        "두 균열의 연쇄를 별도로 시작");
                    Assert(firstRift.SealChainTimeout == 1.5f && secondRift.SealChainTimeout == 3f
                        && firstRift.SealChainTimeRemaining < secondRift.SealChainTimeRemaining,
                        "제한 시간 설정과 진행 상태가 균열 인스턴스별로 분리됨");
                    Assert(!firstRift.TryCompleteSealClick(firstB) && secondRift.IsSealChainInProgress
                        && secondRift.CompletedSealCount == 1,
                        "한 균열의 실패가 다른 균열의 봉인 상태를 건드리지 않음");
                    Assert(secondRift.TryCompleteSealClick(secondB), "다른 균열은 독립적으로 완료 가능");
                    Invoke(secondRift, "LateUpdate");
                    firstA.SetDetected(true, Color.yellow);
                    Assert(firstRift.TryCompleteSealClick(firstA)
                        && firstRift.TryCompleteSealClick(SecondSeal(firstRift)),
                        "실패한 균열도 첫 점부터 독립 재시도 가능");
                    Invoke(firstRift, "LateUpdate");
                    Object.Destroy(firstRift.gameObject);
                    Object.Destroy(secondRift.gameObject);
                    Finish(true, "Q 감지, 두 번의 독립 클릭, 제한 시간, 일시정지, 실패 복구, 균열 간 독립성과 완료 중복 방지를 통과했습니다.");
                }
            }
            catch (Exception exception) { Finish(false, exception.ToString()); }
        }

        private static ManaRift SpawnRift(string label)
        {
            GameObject prefab = AssetDatabase.LoadAssetAtPath<GameObject>("Assets/Prefabs/ManaRift_Test.prefab");
            if (prefab == null) throw new InvalidOperationException("ManaRift_Test.prefab을 불러오지 못했습니다.");
            ManaRift rift = Object.Instantiate(prefab).GetComponent<ManaRift>();
            rift.name = label;
            return rift;
        }

        private static void AssertRandomOrderCanVary()
        {
            UnityEngine.Random.State savedState = UnityEngine.Random.state;
            bool point01WasFirst = false;
            bool point02WasFirst = false;
            try
            {
                UnityEngine.Random.InitState(728194);
                for (int i = 0; i < 32; i++)
                {
                    ManaRift rift = SpawnRift("SealChain_RandomOrder_" + i);
                    MagicTarget authoredFirst = GetPrivateField<MagicTarget>(rift, "sealPoint01");
                    if (rift.FirstSealPoint == authoredFirst) point01WasFirst = true;
                    else point02WasFirst = true;
                    Object.Destroy(rift.gameObject);
                }
            }
            finally
            {
                UnityEngine.Random.state = savedState;
            }
            Assert(point01WasFirst && point02WasFirst,
                "균열 생성마다 독립적으로 첫 봉인점 순서를 무작위 선택");
        }

        private static void AssertOptionalRiftVfx()
        {
            ManaRift rift = SpawnRift("SealChain_OptionalVfx");
            ManaRiftVFX vfx = rift.GetComponent<ManaRiftVFX>();
            if (vfx != null)
            {
                // 프리팹에 사용자가 연결한 효과와 별개로 빈 슬롯 조건을 만듭니다.
                vfx.enabled = false;
                SetPrivateField(vfx, "persistentRiftPrefab", null);
                SetPrivateField(vfx, "dangerStageChangePrefab", null);
                SetPrivateField(vfx, "sealChainFailurePrefab", null);
                SetPrivateField(vfx, "rupturePrefab", null);
                vfx.enabled = true;
            }
            Assert(vfx != null
                && GetPrivateField<GameObject>(vfx, "persistentRiftPrefab") == null
                && GetPrivateField<GameObject>(vfx, "dangerStageChangePrefab") == null
                && GetPrivateField<GameObject>(vfx, "sealChainFailurePrefab") == null
                && GetPrivateField<GameObject>(vfx, "rupturePrefab") == null,
                "VFX 슬롯이 비어 있어도 선택 컴포넌트가 연결됨");
            rift.AdvanceHazard(40f);
            Assert(rift.DangerStage == 2, "빈 위험 단계 VFX 슬롯에서 단계 변경 처리");
            rift.AdvanceHazard(80f);
            Assert(rift.HasOverloaded && rift.IsEnded,
                "빈 파열 VFX 슬롯에서도 균열 파열과 종료 처리");
            if (rift != null) Object.Destroy(rift.gameObject);
        }

        private static void AssertMarkerVisibility(MagicTarget point, bool visible, string message)
        {
            ParticleSystemRenderer[] markers = point.GetComponentsInChildren<ParticleSystemRenderer>(true);
            bool matches = markers.Length > 0;
            foreach (ParticleSystemRenderer marker in markers)
                matches &= marker.enabled == visible;
            MeshRenderer sphere = point.GetComponent<MeshRenderer>();
            Assert(matches && sphere != null && !sphere.enabled, message + " 및 기존 구체 숨김 유지");
        }

        private static MagicTarget FirstSeal(ManaRift rift) => rift.FirstSealPoint;

        private static MagicTarget SecondSeal(ManaRift rift) => rift.SecondSealPoint;

        private static void QueueKeyboard(Keyboard keyboard, bool pressed)
        {
            InputSystem.QueueStateEvent(keyboard, pressed ? new KeyboardState(Key.Q) : new KeyboardState());
        }

        private static void QueueMouse(Mouse mouse, bool pressed)
        {
            InputSystem.QueueStateEvent(mouse,
                pressed ? new MouseState().WithButton(MouseButton.Left, true) : new MouseState());
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException("검증 실패: " + message);
            Debug.Log("[WARD SHIFT 연쇄 봉인 검사] 통과: " + message);
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

        private static void Invoke(object target, string methodName)
        {
            MethodInfo method = target.GetType().GetMethod(methodName, PrivateInstance);
            if (method == null) throw new MissingMethodException(target.GetType().Name, methodName);
            method.Invoke(target, null);
        }

        private static void Finish(bool passed, string message)
        {
            EditorApplication.update -= Tick;
            if (timeoutRift != null) Object.Destroy(timeoutRift.gameObject);
            if (lifecycleRift != null) Object.Destroy(lifecycleRift.gameObject);
            Time.timeScale = savedTimeScale;
            InputSystem.settings.updateMode = savedInputUpdateMode;
            Cursor.lockState = savedCursorLock;
            SessionState.SetInt(ResultKey, passed ? 0 : 1);
            SessionState.SetString(MessageKey, message);
            SessionState.SetBool(RunKey, false);
            SessionState.SetBool(StartedKey, true);
            EditorApplication.ExitPlaymode();
        }

        private static void FinishInEditMode()
        {
            bool passed = SessionState.GetInt(ResultKey, 1) == 0;
            string message = SessionState.GetString(MessageKey, string.Empty);
            Debug.Log(passed ? "[WARD SHIFT 연쇄 봉인 검사] " + message
                : "[WARD SHIFT 연쇄 봉인 검사] 실패: " + message);
            bool shouldExitBatch = SessionState.GetBool(BatchKey, false);
            SessionState.SetBool(StartedKey, false);
            SessionState.SetBool(BatchKey, false);
            if (shouldExitBatch) EditorApplication.Exit(passed ? 0 : 1);
        }
    }
}
