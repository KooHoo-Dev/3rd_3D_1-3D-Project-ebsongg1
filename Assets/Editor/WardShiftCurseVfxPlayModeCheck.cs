using System;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using WardShift;
using Object = UnityEngine.Object;

namespace WardShift.Editor
{
    /// <summary>저주 패치 VFX 수명, 정화 광선 추적, 생성된 패치만의 조기 진압을 검사합니다.</summary>
    [InitializeOnLoad]
    public static class WardShiftCurseVfxPlayModeCheck
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string RunKey = "WardShift.CurseVfxCheck.Running";
        private const string FinishKey = "WardShift.CurseVfxCheck.Finish";
        private const string ResultKey = "WardShift.CurseVfxCheck.Result";
        private const string MessageKey = "WardShift.CurseVfxCheck.Message";
        private const string StartedKey = "WardShift.CurseVfxCheck.Started";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static double startedAt;

        static WardShiftCurseVfxPlayModeCheck()
        {
            EditorApplication.playModeStateChanged -= OnPlayModeChanged;
            EditorApplication.playModeStateChanged += OnPlayModeChanged;
            EditorApplication.update -= Tick;
            if (SessionState.GetBool(RunKey, false) && EditorApplication.isPlaying)
                EditorApplication.update += Tick;
            if (SessionState.GetBool(FinishKey, false) && !EditorApplication.isPlayingOrWillChangePlaymode)
                EditorApplication.delayCall += FinishInEditMode;
            if (IsBatchTestCommandLine() && !SessionState.GetBool(StartedKey, false))
                EditorApplication.delayCall += () => StartCheck(true);
        }

        [MenuItem("Tools/WARD SHIFT/Curse VFX Play Mode Check")]
        public static void RunFromMenu() => StartCheck(false);

        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool batch)
        {
            if (SessionState.GetBool(StartedKey, false) || EditorApplication.isPlayingOrWillChangePlaymode) return;
            SessionState.SetBool(StartedKey, true);
            if (batch) EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            if (Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include) == null)
            {
                Debug.LogError("WARD SHIFT curse VFX check: MainGame AnomalySpawner was not found.");
                SessionState.SetBool(StartedKey, false);
                if (batch) EditorApplication.Exit(1);
                return;
            }

            SessionState.SetBool(RunKey, true);
            SessionState.SetBool(FinishKey, false);
            SessionState.SetInt(ResultKey, 1);
            SessionState.SetString(MessageKey, string.Empty);
            EditorApplication.EnterPlaymode();
        }

        private static void OnPlayModeChanged(PlayModeStateChange state)
        {
            if (state == PlayModeStateChange.EnteredPlayMode && SessionState.GetBool(RunKey, false))
            {
                startedAt = EditorApplication.timeSinceStartup;
                EditorApplication.update -= Tick;
                EditorApplication.update += Tick;
            }
            else if (state == PlayModeStateChange.EnteredEditMode && SessionState.GetBool(FinishKey, false))
            {
                FinishInEditMode();
            }
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            if (EditorApplication.timeSinceStartup - startedAt > 20d)
            {
                Finish(false, "MainGame did not finish initializing within 20 seconds.");
                return;
            }

            DayFlowManager day = Object.FindFirstObjectByType<DayFlowManager>();
            MagicCaster caster = Object.FindFirstObjectByType<MagicCaster>();
            if (day != null && day.State == DayState.Preparing)
            {
                DayBriefingController briefing = Object.FindFirstObjectByType<DayBriefingController>();
                if (briefing != null && GetField<bool>(briefing, "isVisible"))
                    Invoke(briefing, "OnStartClicked");
            }
            if (day == null || caster == null || day.State != DayState.Playing) return;

            EditorApplication.update -= Tick;
            try
            {
                RunChecks(caster);
                Finish(true, "actual AreaDamageShadow patch appearance replacement/scaling, VFX isolation, cancel/resume/complete lifecycle, moving/retargeted beam, raycast isolation, and early resolution passed.");
            }
            catch (Exception exception)
            {
                Finish(false, exception.ToString());
            }
        }

        private static void RunChecks(MagicCaster caster)
        {
            float savedTimeScale = Time.timeScale;
            CursorLockMode savedCursorLock = Cursor.lockState;
            GameObject idlePrefab = null;
            GameObject purifyingPrefab = null;
            GameObject purifiedPrefab = null;
            GameObject beamPrefab = null;
            GameObject areaDamageShadowPrefab = null;
            GameObject originObject = null;
            GameObject patchAObject = null;
            GameObject patchBObject = null;
            GameObject assetPatchObject = null;
            GameObject anomalyObject = null;
            GameObject completionEffect = null;
            SealBeamVFX beamVfx = null;
            GameObject beamVfxObject = null;

            MagicType savedMagic = GetField<MagicType>(caster, "selectedMagic");
            MagicTarget savedCastingTarget = GetField<MagicTarget>(caster, "castingTarget");
            bool savedInputBlocked = GetField<bool>(caster, "inputBlocked");
            bool savedHasFocus = GetField<bool>(caster, "hasFocus");
            bool savedInputEnabled = GetField<bool>(caster, "inputEnabled");
            GameObject savedContinuousBeamPrefab = null;
            Transform savedBeamOrigin = null;
            Camera savedBeamCamera = null;

            try
            {
                Time.timeScale = 1f;
                Cursor.lockState = CursorLockMode.Locked;
                idlePrefab = CreateParticleVfx("TestVFX_Idle");
                purifyingPrefab = CreateParticleVfx("TestVFX_Purifying");
                purifiedPrefab = CreateParticleVfx("TestVFX_Purified");
                beamPrefab = CreateLineVfx("TestVFX_PurifyBeam");
                areaDamageShadowPrefab = AssetDatabase.LoadAssetAtPath<GameObject>(
                    "Assets/Assets/Effects/MagicArsenal/Effects/Prefabs/AreaDamage/AreaDamageShadow.prefab");
                Assert(areaDamageShadowPrefab != null, "AreaDamageShadow prefab is missing from the project.");

                TestPatch a = CreateTestPatch("VfxCheck_PatchA", idlePrefab, purifyingPrefab, purifiedPrefab);
                TestPatch b = CreateTestPatch("VfxCheck_PatchB", idlePrefab, purifyingPrefab, purifiedPrefab);
                patchAObject = a.Patch.gameObject;
                patchBObject = b.Patch.gameObject;
                GameObject idleA = GetField<GameObject>(a.Patch, "idleVfxInstance");
                GameObject idleB = GetField<GameObject>(b.Patch, "idleVfxInstance");
                Assert(idleA != null && idleB != null && idleA != idleB,
                    "Simultaneous patches must own separate idle VFX instances.");
                Assert(idleA.transform.IsChildOf(a.Patch.transform) && idleB.transform.IsChildOf(b.Patch.transform),
                    "Idle VFX must follow its owning patch.");
                Assert(idleA == GetField<GameObject>(a.Patch, "patchAppearanceInstance")
                    && !a.Visual.enabled,
                    "A prefab used as both patch appearance and IdleVFX must replace the circle without duplication.");
                AssertVfxIsNonInteractive(idleA);

                TestPatch assetPatch = CreateTestPatch("VfxCheck_AreaDamageShadow",
                    areaDamageShadowPrefab, purifyingPrefab, purifiedPrefab, null,
                    areaDamageShadowPrefab, 0.5f, 0.075f);
                assetPatchObject = assetPatch.Patch.gameObject;
                assetPatchObject.transform.position = new Vector3(9000f, 9000f, 9000f);
                GameObject actualAppearance = GetField<GameObject>(assetPatch.Patch, "patchAppearanceInstance");
                BoxCollider actualAimCollider = assetPatch.Patch.AimCollider as BoxCollider;
                Assert(actualAppearance != null
                    && actualAppearance == GetField<GameObject>(assetPatch.Patch, "idleVfxInstance")
                    && !assetPatch.Visual.enabled && actualAimCollider != null,
                    "The actual AreaDamageShadow asset must replace the primitive patch and share its idle instance.");
                Assert(Quaternion.Angle(actualAppearance.transform.localRotation,
                    areaDamageShadowPrefab.transform.localRotation) < 0.01f,
                    "The replacement patch must preserve the AreaDamageShadow prefab's authored orientation.");
                Assert(actualAppearance.transform.localScale.x < 0.05f,
                    "The large VFX asset must be reduced to the patch-sized appearance.");
                AssertVfxIsNonInteractive(actualAppearance);
                float initialAimWidth = actualAimCollider.size.x;
                Assert(initialAimWidth <= 1.1f,
                    "The patch target collider must stay independent of the large VFX footprint.");
                Physics.SyncTransforms();
                Ray centerRay = new Ray(assetPatchObject.transform.position + Vector3.up * 5f, Vector3.down);
                Assert(Physics.Raycast(centerRay, out RaycastHit centerHit, 10f)
                    && centerHit.collider == actualAimCollider
                    && centerHit.collider.GetComponentInParent<MagicTarget>() == assetPatch.Target,
                    "The first Raycast hit at the patch center must be its MagicTarget collider.");
                Ray outerVfxRay = new Ray(assetPatchObject.transform.position
                    + Vector3.right * 1.5f + Vector3.up * 5f, Vector3.down);
                Assert(!Physics.Raycast(outerVfxRay, 10f),
                    "The oversized VFX must not create an invisible aim collider outside the target footprint.");
                assetPatch.Target.AdvanceCast(MagicType.Purify, 0.1f);
                Invoke(assetPatch.Patch, "LateUpdate");
                Assert(actualAppearance.transform.localScale.x
                    < GetField<Vector3>(assetPatch.Patch, "initialPatchAppearanceScale").x,
                    "The actual AreaDamageShadow asset must shrink during purification.");
                Assert(Mathf.Approximately(actualAimCollider.size.x, initialAimWidth),
                    "The targetable visual footprint must not shrink during the cast.");
                assetPatch.Target.CancelCast();

                a.Target.AdvanceCast(MagicType.Purify, 0.1f);
                Assert(a.Target.IsCasting, "The test cast should start without completing.");
                Assert(GetField<GameObject>(a.Patch, "purifyingVfxInstance") != null
                    && GetField<GameObject>(b.Patch, "purifyingVfxInstance") == null,
                    "Only the patch whose MagicTarget is casting may play PurifyingVFX.");
                a.Target.CancelCast();
                Assert(GetField<GameObject>(a.Patch, "purifyingVfxInstance") == null,
                    "Canceling a patch cast must stop PurifyingVFX.");

                beamVfx = SealBeamVFX.Active;
                if (beamVfx == null)
                {
                    beamVfxObject = new GameObject("VfxCheck_SealBeamVFX");
                    beamVfx = beamVfxObject.AddComponent<SealBeamVFX>();
                }
                savedContinuousBeamPrefab = GetField<GameObject>(beamVfx, "continuousPurifyBeamPrefab");
                savedBeamOrigin = GetField<Transform>(beamVfx, "beamOrigin");
                savedBeamCamera = GetField<Camera>(beamVfx, "playerCamera");
                originObject = new GameObject("VfxCheck_BeamOrigin");
                originObject.transform.position = new Vector3(0.3f, 1.4f, 0.2f);
                SetField(beamVfx, "continuousPurifyBeamPrefab", beamPrefab);
                SetField(beamVfx, "beamOrigin", originObject.transform);
                SetField(caster, "selectedMagic", MagicType.Purify);
                SetField(caster, "inputBlocked", false);
                SetField(caster, "hasFocus", true);
                SetField(caster, "inputEnabled", true);
                SetField(caster, "castingTarget", a.Target);
                a.Target.AdvanceCast(MagicType.Purify, 0.1f);
                Assert(a.Target.IsCasting, "Patch A did not resume its cast.");
                Invoke(caster, "UpdatePurifyBeamVfx");
                object beamState = GetField<object>(beamVfx, "continuousPurifyBeam");
                Assert(beamState != null, "A real Purify cast should start a continuous beam.");
                Invoke(beamVfx, "LateUpdate");
                AssertContinuousBeam(beamState, originObject.transform.position, a.Patch.transform.position);
                AssertVfxIsNonInteractive(GetField<GameObject>(beamState, "Root"));

                originObject.transform.position += new Vector3(0.2f, 0.1f, 0.3f);
                a.Patch.transform.position += new Vector3(0.1f, 0f, 0.2f);
                Invoke(beamVfx, "LateUpdate");
                AssertContinuousBeam(beamState, originObject.transform.position, a.Patch.transform.position);

                b.Target.AdvanceCast(MagicType.Purify, 0.1f);
                Assert(b.Target.IsCasting, "Patch B did not start casting.");
                SetField(caster, "castingTarget", b.Target);
                Invoke(caster, "UpdatePurifyBeamVfx");
                Assert(GetField<object>(beamVfx, "continuousPurifyBeam") == beamState
                    && GetField<Transform>(beamState, "FollowTarget") == b.Patch.transform,
                    "Switching the casting target must retarget the same continuous beam.");
                Invoke(beamVfx, "LateUpdate");
                AssertContinuousBeam(beamState, originObject.transform.position, b.Patch.transform.position);
                SetField(caster, "selectedMagic", MagicType.Seal);
                Invoke(caster, "UpdatePurifyBeamVfx");
                Assert(GetField<object>(beamVfx, "continuousPurifyBeam") == null,
                    "Changing away from Purify must stop the continuous beam.");
                b.Target.CancelCast();
                a.Target.CancelCast();

                a.Target.AdvanceCast(MagicType.Purify, 0.1f);
                Assert(a.Target.IsCasting, "Patch A did not resume after cancel.");
                Invoke(a.Patch, "LateUpdate");
                Assert(a.Visual.transform.localScale.x < a.InitialVisualScale.x,
                    "Purification must continue shrinking the original patch visual.");
                Assert(GetField<GameObject>(a.Patch, "patchAppearanceInstance").transform.localScale.x
                        < GetField<Vector3>(a.Patch, "initialPatchAppearanceScale").x,
                    "The replacement patch prefab must shrink with purification progress.");
                Assert(a.Target.AdvanceCast(MagicType.Purify, 0.4f), "Patch A did not complete purification.");
                Assert(a.Patch.IsPurified
                    && GetField<GameObject>(a.Patch, "idleVfxInstance") == null
                    && GetField<GameObject>(a.Patch, "purifyingVfxInstance") == null
                    && GetField<GameObject>(a.Patch, "patchAppearanceInstance") == null
                    && !a.Visual.enabled,
                    "Completion must clear both continuous patch effects.");
                completionEffect = GameObject.Find("TestVFX_Purified(Clone)");
                Assert(completionEffect != null && completionEffect.transform.parent == null,
                    "PurifiedVFX must be detached so it survives patch deactivation or destruction.");

                b.Patch.StopEffectsForDayEnd();
                Assert(GetField<GameObject>(b.Patch, "idleVfxInstance") == null,
                    "Day-end cleanup must remove persistent patch VFX.");

                TestEarlyResolution(idlePrefab, purifyingPrefab, purifiedPrefab, out anomalyObject);

                Object.Destroy(patchAObject);
                Object.Destroy(patchBObject);
                Assert(completionEffect != null && completionEffect.transform.parent == null,
                    "Destroying a completed patch must not remove its detached completion VFX.");
            }
            finally
            {
                if (beamVfx != null)
                {
                    beamVfx.StopContinuousPurifyBeam();
                    if (savedContinuousBeamPrefab != null || beamVfxObject == null)
                        SetField(beamVfx, "continuousPurifyBeamPrefab", savedContinuousBeamPrefab);
                    if (beamVfxObject == null)
                    {
                        SetField(beamVfx, "beamOrigin", savedBeamOrigin);
                        SetField(beamVfx, "playerCamera", savedBeamCamera);
                    }
                }
                SetField(caster, "selectedMagic", savedMagic);
                SetField(caster, "castingTarget", savedCastingTarget);
                SetField(caster, "inputBlocked", savedInputBlocked);
                SetField(caster, "hasFocus", savedHasFocus);
                SetField(caster, "inputEnabled", savedInputEnabled);
                Time.timeScale = savedTimeScale;
                Cursor.lockState = savedCursorLock;

                DestroyIfPresent(originObject);
                DestroyIfPresent(beamVfxObject);
                DestroyIfPresent(assetPatchObject);
                DestroyIfPresent(patchAObject);
                DestroyIfPresent(patchBObject);
                DestroyIfPresent(anomalyObject);
                DestroyIfPresent(idlePrefab);
                DestroyIfPresent(purifyingPrefab);
                DestroyIfPresent(purifiedPrefab);
                DestroyIfPresent(beamPrefab);
            }
        }

        private static void TestEarlyResolution(GameObject idlePrefab, GameObject purifyingPrefab,
            GameObject purifiedPrefab, out GameObject anomalyObject)
        {
            anomalyObject = new GameObject("VfxCheck_EarlyResolve");
            anomalyObject.SetActive(false);
            CurseAnomaly anomaly = anomalyObject.AddComponent<CurseAnomaly>();
            TestPatch first = CreateTestPatch("VfxCheck_EarlyPatch1", idlePrefab, purifyingPrefab, purifiedPrefab,
                anomalyObject.transform);
            TestPatch reserved = CreateTestPatch("VfxCheck_EarlyPatch2", idlePrefab, purifyingPrefab, purifiedPrefab,
                anomalyObject.transform);
            SetField(anomaly, "patches", new[] { first.Patch, reserved.Patch });
            anomalyObject.SetActive(true);

            SetField(anomaly, "hasSpawnPointConfiguration", true);
            SetField(anomaly, "hasPlacedPatches", true);
            SetField(anomaly, "registeredPatches", new[] { true, true });
            SetField(anomaly, "selectedPatchCount", 2);
            SetField(anomaly, "spawnedPatchCount", 1);
            Invoke(anomaly, "RefreshPatchAvailability");

            Assert(anomaly.SelectedPatchCount == 2 && GetField<int>(anomaly, "spawnedPatchCount") == 1,
                "The early-resolution fixture must retain one scheduled patch.");
            Assert(GetField<GameObject>(first.Patch, "idleVfxInstance") != null,
                "The active anomaly patch did not start its idle VFX.");
            Invoke(anomaly, "OnDayFailed", "VFX cleanup check");
            Assert(GetField<GameObject>(first.Patch, "idleVfxInstance") == null,
                "The DAY FAILED callback must clear persistent effects from its patches.");
            Assert(first.Target.CompleteInstantly(MagicType.Purify), "The currently spawned patch did not resolve.");
            Assert(GetField<bool>(anomaly, "isResolved"),
                "Purifying all currently spawned patches must resolve the anomaly early.");
            Assert(anomaly.AdvanceHazard(100f) == 0f && GetField<int>(anomaly, "spawnedPatchCount") == 1,
                "An early-resolved anomaly must not create its still-scheduled patch.");
        }

        private static TestPatch CreateTestPatch(string name, GameObject idlePrefab,
            GameObject purifyingPrefab, GameObject purifiedPrefab, Transform parent = null,
            GameObject appearancePrefab = null, float appearanceAimRadius = 1.5f,
            float appearanceScale = 1f)
        {
            GameObject root = new GameObject(name);
            root.SetActive(false);
            if (parent != null) root.transform.SetParent(parent, false);
            root.AddComponent<BoxCollider>();
            MagicTarget target = root.AddComponent<MagicTarget>();
            GameObject visualObject = GameObject.CreatePrimitive(PrimitiveType.Cylinder);
            visualObject.name = "Patch_Visual";
            visualObject.transform.SetParent(root.transform, false);
            visualObject.transform.localScale = new Vector3(1.3f, 0.02f, 1.3f);
            CursePatch patch = root.AddComponent<CursePatch>();

            SetField(target, "requiredMagic", MagicType.Purify);
            SetField(target, "castDuration", 0.5f);
            SetField(target, "useTimingCast", false);
            SetField(target, "allowHeldPurifyChain", true);
            SetField(target, "deactivateOnComplete", false);
            SetField(patch, "purificationTarget", target);
            SetField(patch, "patchRenderer", visualObject.GetComponent<Renderer>());
            SetField(patch, "aimCollider", root.GetComponent<Collider>());
            SetField(patch, "patchAppearancePrefab", appearancePrefab != null ? appearancePrefab : idlePrefab);
            SetField(patch, "patchAppearanceScale", appearanceScale);
            SetField(patch, "idleVfxPrefab", idlePrefab);
            SetField(patch, "patchAppearanceAimRadius", appearanceAimRadius);
            SetField(patch, "purifyingVfxPrefab", purifyingPrefab);
            SetField(patch, "purifiedVfxPrefab", purifiedPrefab);
            SetField(patch, "purifiedVfxLifetime", 1f);
            root.SetActive(true);

            return new TestPatch
            {
                Patch = patch,
                Target = target,
                Visual = visualObject.GetComponent<Renderer>(),
                InitialVisualScale = visualObject.transform.localScale
            };
        }

        private static GameObject CreateParticleVfx(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.transform.position = new Vector3(10000f, 10000f, 10000f);
            prefab.AddComponent<ParticleSystem>().Stop(true, ParticleSystemStopBehavior.StopEmittingAndClear);
            prefab.AddComponent<BoxCollider>();
            return prefab;
        }

        private static GameObject CreateLineVfx(string name)
        {
            GameObject prefab = new GameObject(name);
            prefab.transform.position = new Vector3(10000f, 10000f, 10000f);
            LineRenderer line = prefab.AddComponent<LineRenderer>();
            line.positionCount = 2;
            line.useWorldSpace = true;
            line.startWidth = 0.1f;
            line.endWidth = 0.1f;
            prefab.AddComponent<BoxCollider>();
            return prefab;
        }

        private static void AssertContinuousBeam(object beamState, Vector3 origin, Vector3 target)
        {
            GameObject root = GetField<GameObject>(beamState, "Root");
            Assert(root != null, "Continuous beam root is missing.");
            Vector3 direction = target - origin;
            Assert(Vector3.Distance(root.transform.position, origin) < 0.001f
                && Vector3.Distance(GetField<Vector3>(beamState, "Start"), origin) < 0.001f
                && Vector3.Distance(GetField<Vector3>(beamState, "End"), target) < 0.001f
                && (direction.sqrMagnitude < 0.000001f
                    || Quaternion.Angle(root.transform.rotation, Quaternion.LookRotation(direction)) < 0.01f),
                "The continuous beam must follow its origin and face the current target.");
        }

        private static void AssertVfxIsNonInteractive(GameObject effect)
        {
            Assert(effect != null && effect.layer == LayerMask.NameToLayer("Ignore Raycast"),
                "VFX must use the Ignore Raycast layer.");
            foreach (Collider collider in effect.GetComponentsInChildren<Collider>(true))
                Assert(!collider.enabled, "VFX 3D colliders must be disabled.");
            foreach (Collider2D collider in effect.GetComponentsInChildren<Collider2D>(true))
                Assert(!collider.enabled, "VFX 2D colliders must be disabled.");
        }

        private static void Invoke(object target, string name, params object[] args)
        {
            MethodInfo method = target.GetType().GetMethod(name, PrivateInstance);
            if (method == null) throw new MissingMethodException(target.GetType().Name, name);
            method.Invoke(target, args);
        }

        private static T GetField<T>(object target, string name)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            return (T)field.GetValue(target);
        }

        private static void SetField(object target, string name, object value)
        {
            FieldInfo field = target.GetType().GetField(name, PrivateInstance | BindingFlags.Public);
            if (field == null) throw new MissingFieldException(target.GetType().Name, name);
            field.SetValue(target, value);
        }

        private static void DestroyIfPresent(GameObject target)
        {
            if (target != null) Object.Destroy(target);
        }

        private static void Assert(bool condition, string message)
        {
            if (!condition) throw new InvalidOperationException(message);
        }

        private static void Finish(bool success, string message)
        {
            Debug.Log($"WARD SHIFT curse VFX check {(success ? "PASSED" : "FAILED")}: {message}");
            SessionState.SetBool(RunKey, false);
            SessionState.SetBool(FinishKey, true);
            SessionState.SetInt(ResultKey, success ? 0 : 1);
            SessionState.SetString(MessageKey, message);
            EditorApplication.update -= Tick;
            EditorApplication.isPlaying = false;
        }

        private static void FinishInEditMode()
        {
            if (!SessionState.GetBool(FinishKey, false)) return;
            int result = SessionState.GetInt(ResultKey, 1);
            string message = SessionState.GetString(MessageKey, "unknown result");
            SessionState.SetBool(FinishKey, false);
            SessionState.SetBool(StartedKey, false);
            if (IsBatchTestCommandLine()) EditorApplication.Exit(result);
            else Debug.Log($"WARD SHIFT curse VFX check finished: {message}");
        }

        private static bool IsBatchTestCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-executeMethod"
                    && args[i + 1] == "WardShift.Editor.WardShiftCurseVfxPlayModeCheck.RunMainGameInBatch")
                    return true;
            return false;
        }

        private sealed class TestPatch
        {
            public CursePatch Patch;
            public MagicTarget Target;
            public Renderer Visual;
            public Vector3 InitialVisualScale;
        }
    }
}
