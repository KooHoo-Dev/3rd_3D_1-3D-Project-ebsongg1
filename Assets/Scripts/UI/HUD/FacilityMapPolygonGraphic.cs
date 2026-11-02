using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>시설 배치도의 바닥 구역을 UI 메시로 채웁니다.</summary>
    [DisallowMultipleComponent]
    public sealed class FacilityMapPolygonGraphic : MaskableGraphic
    {
        private readonly List<Vector2> vertices = new List<Vector2>();

        public void SetVertices(IReadOnlyList<Vector2> points)
        {
            vertices.Clear();
            if (points != null)
            {
                for (int i = 0; i < points.Count; i++)
                {
                    Vector2 point = points[i];
                    if (vertices.Count == 0 || (vertices[vertices.Count - 1] - point).sqrMagnitude > 0.0001f)
                        vertices.Add(point);
                }
            }

            if (vertices.Count > 2 && (vertices[0] - vertices[vertices.Count - 1]).sqrMagnitude <= 0.0001f)
                vertices.RemoveAt(vertices.Count - 1);

            SetVerticesDirty();
        }

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            if (vertices.Count < 3) return;

            for (int i = 0; i < vertices.Count; i++)
            {
                UIVertex vertex = UIVertex.simpleVert;
                vertex.position = vertices[i];
                vertex.color = color;
                vertexHelper.AddVert(vertex);
            }

            List<int> remaining = new List<int>(vertices.Count);
            for (int i = 0; i < vertices.Count; i++) remaining.Add(i);
            float orientation = SignedArea(vertices) >= 0f ? 1f : -1f;
            int safety = vertices.Count * vertices.Count;

            while (remaining.Count > 3 && safety-- > 0)
            {
                bool clippedEar = false;
                for (int i = 0; i < remaining.Count; i++)
                {
                    int previous = remaining[(i + remaining.Count - 1) % remaining.Count];
                    int current = remaining[i];
                    int next = remaining[(i + 1) % remaining.Count];
                    Vector2 a = vertices[previous];
                    Vector2 b = vertices[current];
                    Vector2 c = vertices[next];
                    if (Cross(b - a, c - b) * orientation <= 0.0001f) continue;

                    bool containsVertex = false;
                    for (int j = 0; j < remaining.Count; j++)
                    {
                        int candidate = remaining[j];
                        if (candidate == previous || candidate == current || candidate == next) continue;
                        if (!PointInTriangle(vertices[candidate], a, b, c, orientation)) continue;
                        containsVertex = true;
                        break;
                    }
                    if (containsVertex) continue;

                    vertexHelper.AddTriangle(previous, current, next);
                    remaining.RemoveAt(i);
                    clippedEar = true;
                    break;
                }

                if (!clippedEar) return;
            }

            if (remaining.Count == 3)
                vertexHelper.AddTriangle(remaining[0], remaining[1], remaining[2]);
        }

        private static float SignedArea(IReadOnlyList<Vector2> points)
        {
            float area = 0f;
            for (int i = 0; i < points.Count; i++)
            {
                Vector2 a = points[i];
                Vector2 b = points[(i + 1) % points.Count];
                area += a.x * b.y - b.x * a.y;
            }
            return area * 0.5f;
        }

        private static float Cross(Vector2 a, Vector2 b) => a.x * b.y - a.y * b.x;

        private static bool PointInTriangle(Vector2 point, Vector2 a, Vector2 b, Vector2 c,
            float orientation)
        {
            const float epsilon = 0.0001f;
            return Cross(b - a, point - a) * orientation >= -epsilon
                && Cross(c - b, point - b) * orientation >= -epsilon
                && Cross(a - c, point - c) * orientation >= -epsilon;
        }
    }
}
