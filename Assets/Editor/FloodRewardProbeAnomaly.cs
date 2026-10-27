using UnityEngine;

namespace WardShift
{
    /// <summary>Play Mode 검사에서 진압 보상과 중복 방지를 확인하는 임시 이상현상.</summary>
    [AddComponentMenu("")]
    public sealed class FloodRewardProbeAnomaly : AnomalyBehaviour
    {
        private int stage = 1;
        private bool ruptureDuringAdvance;

        public override AnomalySpawnPoint.SurfaceType SpawnSurface => AnomalySpawnPoint.SurfaceType.Wall;
        public override bool IsHazardActive => false;
        public override int DangerStage => stage;
        public override int ResolutionReward => stage;

        public override float AdvanceHazard(float deltaTime)
        {
            if (!ruptureDuringAdvance) return 0f;
            ruptureDuringAdvance = false;
            NotifyRuptured();
            Destroy(gameObject);
            return 0f;
        }

        public void SetStage(int value) => stage = Mathf.Clamp(value, 1, 3);
        public void ResolveForTest() => NotifyResolved();
        public void RuptureForTest() => NotifyRuptured();
        public void RuptureDuringAdvanceForTest() => ruptureDuringAdvance = true;
    }
}
