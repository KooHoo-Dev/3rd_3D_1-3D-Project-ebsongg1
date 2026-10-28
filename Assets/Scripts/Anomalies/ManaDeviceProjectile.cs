using UnityEngine;

namespace WardShift
{
    /// <summary>짧게 조준을 보정한 뒤 직진하는 장치 마력탄. 외형과 충돌 판정은 분리합니다.</summary>
    [DisallowMultipleComponent]
    public sealed class ManaDeviceProjectile : MonoBehaviour
    {
        private ManaDeviceAnomaly source;
        private PlayerContamination player;
        private Transform deviceRoot;
        private LayerMask collisionLayers;
        private float speed;
        private float homingSeconds;
        private float radius;
        private float lifetime;
        private float damage;
        private float age;
        private Vector3 direction;
        private bool consumed;
        private Material fallbackMaterial;

        public void Initialize(ManaDeviceAnomaly owner, PlayerContamination target,
            Transform device, GameObject visualPrefab, LayerMask layers, float travelSpeed,
            float trackingSeconds, float collisionRadius, float maxLifetime,
            float contamination, Vector3 initialDirection)
        {
            source = owner;
            player = target;
            deviceRoot = device;
            collisionLayers = layers;
            speed = Mathf.Max(0.1f, travelSpeed);
            homingSeconds = Mathf.Max(0f, trackingSeconds);
            radius = Mathf.Max(0.05f, collisionRadius);
            lifetime = Mathf.Max(0.1f, maxLifetime);
            damage = Mathf.Max(0f, contamination);
            direction = initialDirection.sqrMagnitude > 0.0001f
                ? initialDirection.normalized : transform.forward;

            if (visualPrefab != null)
            {
                GameObject visual = Instantiate(visualPrefab, transform);
                visual.transform.localPosition = Vector3.zero;
                foreach (Collider collider in visual.GetComponentsInChildren<Collider>(true))
                    collider.enabled = false;
            }
            else
            {
                GameObject visual = GameObject.CreatePrimitive(PrimitiveType.Sphere);
                visual.name = "FallbackManaBoltVisual";
                visual.transform.SetParent(transform, false);
                visual.transform.localScale = Vector3.one * radius * 1.6f;
                Collider collider = visual.GetComponent<Collider>();
                if (collider != null) Destroy(collider);
                Renderer renderer = visual.GetComponent<Renderer>();
                Shader shader = Shader.Find("Universal Render Pipeline/Unlit");
                if (shader == null) shader = Shader.Find("Unlit/Color");
                if (renderer != null && shader != null)
                {
                    Material material = new Material(shader);
                    Color tint = new Color(0.75f, 0.3f, 1f);
                    if (material.HasProperty("_BaseColor")) material.SetColor("_BaseColor", tint);
                    if (material.HasProperty("_Color")) material.SetColor("_Color", tint);
                    renderer.material = material;
                    fallbackMaterial = material;
                }
                Light light = visual.AddComponent<Light>();
                light.color = new Color(0.75f, 0.3f, 1f);
                light.range = 2.5f;
                light.intensity = 2f;
            }
        }

        private void Update()
        {
            if (consumed || source == null || !source.IsHazardActive || player == null)
            {
                Destroy(gameObject);
                return;
            }
            float delta = Time.deltaTime;
            age += delta;
            if (age >= lifetime)
            {
                Destroy(gameObject);
                return;
            }
            if (age <= homingSeconds)
            {
                Vector3 toPlayer = player.transform.position + Vector3.up - transform.position;
                if (toPlayer.sqrMagnitude > 0.0001f) direction = toPlayer.normalized;
            }
            float distance = speed * delta;
            // SphereCast does not report colliders already overlapping the origin.
            foreach (Collider overlap in Physics.OverlapSphere(transform.position, radius,
                         collisionLayers, QueryTriggerInteraction.Ignore))
            {
                if (IsIgnored(overlap)) continue;
                Consume(overlap);
                return;
            }
            RaycastHit[] hits = Physics.SphereCastAll(transform.position, radius, direction,
                distance, collisionLayers, QueryTriggerInteraction.Ignore);
            Collider closest = null;
            float nearest = float.PositiveInfinity;
            foreach (RaycastHit hit in hits)
            {
                Collider collider = hit.collider;
                if (IsIgnored(collider) || hit.distance >= nearest) continue;
                closest = collider;
                nearest = hit.distance;
            }
            if (closest != null)
            {
                Consume(closest);
                return;
            }
            transform.position += direction * distance;
            transform.rotation = Quaternion.LookRotation(direction);
        }

        private bool IsIgnored(Collider collider)
        {
            return collider == null || collider.transform.IsChildOf(transform)
                || deviceRoot != null && collider.transform.IsChildOf(deviceRoot);
        }

        private void Consume(Collider collider)
        {
            consumed = true;
            PlayerContamination hitPlayer = collider.GetComponentInParent<PlayerContamination>();
            if (hitPlayer == player) source.ApplyDeviceHit(player, damage);
            Destroy(gameObject);
        }

        private void OnDestroy()
        {
            if (fallbackMaterial != null) Destroy(fallbackMaterial);
        }
    }
}
