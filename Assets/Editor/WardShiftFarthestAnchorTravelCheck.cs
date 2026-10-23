using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using StarterAssets;
using Unity.AI.Navigation;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.AI;
using WardShift;

namespace WardShift.Editor
{
    /// <summary>
    /// - 임시 경로 메시 생성
    /// - 실제 플레이어 조작으로 가장 먼 활성 생성 지점까지 이동
    /// - 도착 후 봉인 및 보상 확인
    /// </summary>
    [InitializeOnLoad]
    public static class WardShiftFarthestAnchorTravelCheck
    {
        private const string MainGamePath = "Assets/Scenes/MainGame.unity";
        private const string RunKey = "WardShift.FarthestAnchor.Running";
        private const string BatchKey = "WardShift.FarthestAnchor.Batch";
        private const string FinishKey = "WardShift.FarthestAnchor.Finish";
        private const string ResultKey = "WardShift.FarthestAnchor.Result";
        private const string MessageKey = "WardShift.FarthestAnchor.Message";
        private const string StartedKey = "WardShift.FarthestAnchor.Started";
        private static readonly BindingFlags PrivateInstance = BindingFlags.Instance | BindingFlags.NonPublic;

        private static int phase;
        private static double phaseStartedAt;
        private static double lastProgressAt;
        private static Vector3 lastPosition;
        private static AnomalySpawner spawner;
        private static DayFlowManager day;
        private static WardStatus ward;
        private static PlayerInventory inventory;
        private static Transform playerRoot;
        private static CharacterController characterController;
        private static FirstPersonController motor;
        private static StarterAssetsInputs inputs;
        private static MagicCaster caster;
        private static NavMeshSurface temporarySurface;
        private static AnomalySpawnPoint[] spawnPoints;
        private static AnomalySpawnPoint targetPoint;
        private static ManaRift targetRift;
        private static MagicTarget[] sealTargets;
        private static Vector3[] travelCorners;
        private static float travelPathLength;
        private static float travelStartedAt;
        private static float travelElapsedAtArrival;
        private static int cornerIndex;
        private static int sealIndex;
        private static int rewardAtResolution;
        private static int crystalBaseline;

        static WardShiftFarthestAnchorTravelCheck()
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

        [MenuItem("Tools/WARD SHIFT/최장 거리 이동 검사 실행")]
        public static void RunFromMenu() => StartCheck(false);

        public static void RunMainGameInBatch() => StartCheck(true);

        private static void StartCheck(bool batch)
        {
            if (SessionState.GetBool(StartedKey, false)) return;
            SessionState.SetBool(StartedKey, true);
            if (EditorApplication.isPlayingOrWillChangePlaymode)
            {
                Debug.LogError("[WARD SHIFT 최장 거리 이동 검사] 검사 전에 플레이 모드를 종료하세요.");
                if (batch) EditorApplication.Exit(1);
                return;
            }
            if (batch) EditorSceneManager.OpenScene(MainGamePath, OpenSceneMode.Single);
            if (UnityEngine.Object.FindFirstObjectByType<AnomalySpawner>(FindObjectsInactive.Include) == null)
            {
                Debug.LogError("[WARD SHIFT 최장 거리 이동 검사] AnomalySpawner를 찾을 수 없습니다.");
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
            if (spawner == null || day == null || ward == null || inventory == null || ward.Player == null)
            {
                Finish(false, "MainGame 필수 참조 누락: DAY, 플레이어, 시설 또는 AnomalySpawner");
                return;
            }

            SetPrivateField(spawner, "spawnOnStart", false);
            SetPrivateField(spawner, "spawnIntervalSeconds", 1000f);
            SetPrivateField(spawner, "firstSpawnDelaySeconds", 1000f);
            SetPrivateField(spawner, "maximumConcurrentAnomalies", 1);
            phase = 0;
            phaseStartedAt = EditorApplication.timeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
        }

        private static void Tick()
        {
            if (!EditorApplication.isPlaying || !SessionState.GetBool(RunKey, false)) return;
            try
            {
                if (EditorApplication.timeSinceStartup - phaseStartedAt > 90d)
                {
                    Finish(false, $"이동 검사 {phase}단계 시간 초과");
                    return;
                }
                switch (phase)
                {
                    case 0:
                        if (day.State != DayState.Playing) return;
                        spawner.SetSpawningEnabled(false);
                        if (spawner.ActiveAnomalyCount > 0)
                        {
                            spawner.ClearActiveAnomalies();
                            phaseStartedAt = EditorApplication.timeSinceStartup;
                            return;
                        }
                        if (EditorApplication.timeSinceStartup - phaseStartedAt < 0.15d) return;
                        if (!BuildFarthestRouteAndSpawn()) return;
                        phase = 1;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        lastProgressAt = phaseStartedAt;
                        lastPosition = playerRoot.position;
                        Debug.Log($"[WARD SHIFT 최장 거리 이동 검사] 선택 지점: {targetRift.SpawnLocationLabel}\n- 경로 길이: {travelPathLength:F1}m\n- 달리기 속도: {motor.SprintSpeed:F1}m/s");
                        break;

                    case 1:
                        FollowRoute();
                        break;

                    case 2:
                        AimAtSealTarget();
                        if (caster.AimedTarget != sealTargets[sealIndex])
                        {
                            if (EditorApplication.timeSinceStartup - phaseStartedAt > 4d)
                                Finish(false, $"{targetPoint.name} 도착 후 봉인 {sealIndex + 1} 조준 실패: 실제 조준 범위 밖이거나 시야 차단 (도착 {travelElapsedAtArrival:F2}초)");
                            return;
                        }
                        if (sealIndex == 1)
                            rewardAtResolution = targetRift.ResolutionReward;
                        sealTargets[sealIndex].AdvanceCast(MagicType.Seal, Time.deltaTime);
                        phase = 3;
                        phaseStartedAt = EditorApplication.timeSinceStartup;
                        break;

                    case 3:
                        if (targetRift == null || !targetRift.IsHazardActive)
                        {
                            Finish(false, $"봉인 {sealIndex + 1} 완료 전 ManaRift 종료 (경과 {Time.time - travelStartedAt:F2}초)");
                            return;
                        }
                        AimAtSealTarget();
                        if (caster.AimedTarget != sealTargets[sealIndex])
                        {
                            sealTargets[sealIndex].CancelCast();
                            phase = 2;
                            return;
                        }
                        sealTargets[sealIndex].AdvanceCast(MagicType.Seal, Time.deltaTime);
                        if (sealTargets[sealIndex].Progress < 0.7f) return;
                        if (sealTargets[sealIndex].ReleaseCast() != MagicCastResult.Success)
                        {
                            Finish(false, $"최장 거리 지점에서 봉인 {sealIndex + 1} 타이밍 시전 실패");
                            return;
                        }
                        sealIndex++;
                        if (sealIndex >= sealTargets.Length)
                        {
                            phase = 4;
                            phaseStartedAt = EditorApplication.timeSinceStartup;
                        }
                        else
                        {
                            phase = 2;
                            phaseStartedAt = EditorApplication.timeSinceStartup;
                        }
                        break;

                    case 4:
                        if (targetRift != null && !targetRift.IsEnded) return;
                        float elapsed = Time.time - travelStartedAt;
                        int expectedReward = Mathf.Clamp(rewardAtResolution, 0, 3);
                        if (inventory.ManaCrystals != crystalBaseline + expectedReward)
                        {
                            Finish(false, $"{targetPoint.name} 도착 후 {elapsed:F2}초에 진압했으나 DAY 보상 오류: 결정 {inventory.ManaCrystals - crystalBaseline}개, 예상 {expectedReward}개");
                            return;
                        }
                        Finish(true, $"검사 통과\n발생 구역: {targetPoint.name}\n이동 거리: {travelPathLength:F1}m\n도착 시간: {travelElapsedAtArrival:F2}초\n이동부터 진압까지: {elapsed:F2}초\n위험 단계: {rewardAtResolution}단계\n결정 보상: {expectedReward}개");
                        break;
                }
            }
            catch (Exception exception)
            {
                Finish(false, $"이동 검사 {phase}단계 예외: {exception}");
            }
        }

        private static bool BuildFarthestRouteAndSpawn()
        {
            playerRoot = ward.Player.transform;
            characterController = playerRoot.GetComponentInParent<CharacterController>();
            if (characterController != null) playerRoot = characterController.transform;
            motor = playerRoot.GetComponent<FirstPersonController>();
            inputs = playerRoot.GetComponent<StarterAssetsInputs>();
            caster = UnityEngine.Object.FindFirstObjectByType<MagicCaster>();
            spawnPoints = GetPrivateField<AnomalySpawnPoint[]>(spawner, "spawnPoints");
            if (characterController == null || motor == null || inputs == null
                || caster == null || spawnPoints == null || spawnPoints.Length == 0)
            {
                Finish(false, "플레이어 컨트롤러·입력 또는 사용 가능한 생성 지점을 찾을 수 없습니다.");
                return false;
            }

            spawner.SetSpawningEnabled(false);
            characterController.enabled = false;
            GameObject surfaceObject = new GameObject("임시 최장 거리 검사 NavMesh");
            temporarySurface = surfaceObject.AddComponent<NavMeshSurface>();
            temporarySurface.agentTypeID = 0;
            temporarySurface.collectObjects = CollectObjects.All;
            temporarySurface.useGeometry = NavMeshCollectGeometry.PhysicsColliders;
            temporarySurface.layerMask = ~0;
            temporarySurface.BuildNavMesh();
            characterController.enabled = true;

            if (!NavMesh.SamplePosition(playerRoot.position, out NavMeshHit startHit, 4f, NavMesh.AllAreas))
            {
                Finish(false, $"플레이어 시작 위치 주변에 이동 가능한 경로가 없습니다: {playerRoot.position}");
                return false;
            }

            var candidates = new List<AnchorRoute>();
            foreach (AnomalySpawnPoint point in spawnPoints)
            {
                if (point == null || point.Surface != AnomalySpawnPoint.SurfaceType.Wall) continue;
                AnchorRoute shortestRoute = null;
                foreach (float forwardDistance in new[] { 1f, 1.5f, 2f, 2.5f, 3f })
                foreach (float sideDistance in new[] { 0f, -0.75f, 0.75f })
                {
                    Vector3 candidate = point.SpawnPosition + point.transform.forward * forwardDistance
                        + point.transform.right * sideDistance;
                    candidate.y = startHit.position.y;
                    if (!NavMesh.SamplePosition(candidate, out NavMeshHit goalHit, 3f, NavMesh.AllAreas)) continue;
                    var path = new NavMeshPath();
                    if (!NavMesh.CalculatePath(startHit.position, goalHit.position, NavMesh.AllAreas, path)
                        || path.status != NavMeshPathStatus.PathComplete) continue;
                    float length = PathLength(path.corners);
                    if (shortestRoute == null || length < shortestRoute.Length)
                        shortestRoute = new AnchorRoute(point, path.corners, length);
                }
                if (shortestRoute != null) candidates.Add(shortestRoute);
            }

            if (candidates.Count == 0)
            {
                Finish(false, "임시 경로 메시에서 벽면 이상현상 생성 지점까지 이동할 수 없습니다.");
                return false;
            }
            AnchorRoute farthest = candidates.OrderByDescending(route => route.Length).First();
            targetPoint = farthest.Point;
            travelCorners = farthest.Corners;
            travelPathLength = farthest.Length;
            if (travelCorners == null || travelCorners.Length == 0)
            {
                Finish(false, "최장 거리 생성 지점 경로에 코너 정보가 없습니다.");
                return false;
            }

            int selectedPointIndex = Array.IndexOf(spawnPoints, targetPoint);
            SetPrivateField(spawner, "randomizePoint", false);
            SetPrivateField(spawner, "selectedPointIndex", selectedPointIndex);
            spawner.SetSpawningEnabled(true);
            spawner.SpawnRift();
            spawner.SetSpawningEnabled(false);
            targetRift = spawner.ActiveRift;
            string expectedLocation = GetPrivateField<string>(targetPoint, "locationName");
            if (string.IsNullOrWhiteSpace(expectedLocation))
                expectedLocation = targetPoint.name.Replace("SP_", string.Empty);
            if (targetRift == null || targetRift.SpawnLocationLabel != expectedLocation
                || Vector3.Distance(targetRift.transform.position, targetPoint.SpawnPosition) > 0.05f)
            {
                Finish(false, $"최장 거리 생성 지점 {targetPoint.name}에 ManaRift 생성 실패");
                return false;
            }

            sealTargets = new[]
            {
                GetPrivateField<MagicTarget>(targetRift, "sealPoint01"),
                GetPrivateField<MagicTarget>(targetRift, "sealPoint02")
            };
            if (sealTargets.Any(target => target == null))
            {
                Finish(false, "최장 거리 ManaRift의 봉인 지점 참조가 없습니다.");
                return false;
            }
            targetRift.ResolutionCompleted += OnResolved;
            crystalBaseline = inventory.ManaCrystals;
            cornerIndex = 0;
            sealIndex = 0;
            rewardAtResolution = 0;
            travelStartedAt = Time.time;
            return true;
        }

        private static void FollowRoute()
        {
            if (targetRift == null || !targetRift.IsHazardActive)
            {
                StopPlayer();
                Finish(false, $"도착 전에 최장 거리 ManaRift 파열: 위치={playerRoot.position}, 이동 시간={Time.time - travelStartedAt:F2}초");
                return;
            }
            if (EditorApplication.timeSinceStartup - phaseStartedAt > 70d)
            {
                StopPlayer();
                Finish(false, $"플레이어가 {targetPoint.name}에 도착하지 못했습니다: 경로={travelPathLength:F1}m, 현재 위치={playerRoot.position}");
                return;
            }

            while (cornerIndex < travelCorners.Length)
            {
                Vector3 toCorner = travelCorners[cornerIndex] - playerRoot.position;
                toCorner.y = 0f;
                if (toCorner.sqrMagnitude > 0.2f * 0.2f) break;
                cornerIndex++;
            }
            if (cornerIndex >= travelCorners.Length)
            {
                StopPlayer();
                travelElapsedAtArrival = Time.time - travelStartedAt;
                AimAtSealTarget();
                phase = 2;
                phaseStartedAt = EditorApplication.timeSinceStartup;
                return;
            }

            Vector3 direction = travelCorners[cornerIndex] - playerRoot.position;
            direction.y = 0f;
            if (direction.sqrMagnitude > 0.001f)
            {
                direction.Normalize();
                playerRoot.rotation = Quaternion.LookRotation(direction, Vector3.up);
                inputs.MoveInput(Vector2.up);
                inputs.SprintInput(true);
            }

            if (Vector3.Distance(playerRoot.position, lastPosition) > 0.04f)
            {
                lastPosition = playerRoot.position;
                lastProgressAt = EditorApplication.timeSinceStartup;
            }
            else if (EditorApplication.timeSinceStartup - lastProgressAt > 2.5d)
            {
                StopPlayer();
                Finish(false, $"{targetPoint.name} 도착 전 이동 정지: 최근 이동={Vector3.Distance(playerRoot.position, lastPosition):F2}m, 현재 위치={playerRoot.position}, 경로={travelPathLength:F1}m");
            }
        }

        private static void AimAtSealTarget()
        {
            if (sealTargets == null || sealIndex < 0 || sealIndex >= sealTargets.Length) return;
            MagicTarget target = sealTargets[sealIndex];
            Collider collider = target.GetComponentInChildren<Collider>();
            Camera camera = Camera.main;
            if (target == null || collider == null || camera == null) return;
            Vector3 delta = collider.bounds.center - camera.transform.position;
            Vector3 horizontal = Vector3.ProjectOnPlane(delta, Vector3.up);
            if (horizontal.sqrMagnitude > 0.001f)
                playerRoot.rotation = Quaternion.LookRotation(horizontal.normalized, Vector3.up);
            camera.transform.rotation = Quaternion.LookRotation(delta.normalized, Vector3.up);
            if (motor.CinemachineCameraTarget != null)
            {
                float pitch = -Mathf.Asin(Mathf.Clamp(delta.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
                motor.CinemachineCameraTarget.transform.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            }
        }

        private static void OnResolved(AnomalyBehaviour anomaly)
        {
            rewardAtResolution = anomaly.ResolutionReward;
        }

        private static float PathLength(Vector3[] corners)
        {
            float length = 0f;
            for (int i = 1; i < corners.Length; i++) length += Vector3.Distance(corners[i - 1], corners[i]);
            return length;
        }

        private static void StopPlayer()
        {
            if (inputs == null) return;
            inputs.MoveInput(Vector2.zero);
            inputs.SprintInput(false);
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
            Debug.Log($"[WARD SHIFT 최장 거리 이동 검사 {(success ? "성공" : "실패")}]\n- {message.Replace("\n", "\n- ")}");
            StopPlayer();
            if (spawner != null)
            {
                spawner.SetSpawningEnabled(false);
                spawner.ClearActiveAnomalies();
            }
            if (temporarySurface != null) UnityEngine.Object.Destroy(temporarySurface.gameObject);
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
            else Debug.Log($"[WARD SHIFT 최장 거리 이동 검사 종료]\n- {message.Replace("\n", "\n- ")}");
        }

        private static bool IsBatchTestCommandLine()
        {
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
                if (args[i] == "-executeMethod"
                    && args[i + 1] == "WardShift.Editor.WardShiftFarthestAnchorTravelCheck.RunMainGameInBatch")
                    return true;
            return false;
        }

        private static void StartBatchCommandLineCheck()
        {
            if (!SessionState.GetBool(StartedKey, false)) StartCheck(true);
        }

        private sealed class AnchorRoute
        {
            public readonly AnomalySpawnPoint Point;
            public readonly Vector3[] Corners;
            public readonly float Length;
            public AnchorRoute(AnomalySpawnPoint point, Vector3[] corners, float length)
            {
                Point = point;
                Corners = corners;
                Length = length;
            }
        }
    }
}
