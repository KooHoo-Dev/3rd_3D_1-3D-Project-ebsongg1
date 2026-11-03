using UnityEngine;
using UnityEngine.UI;

namespace WardShift
{
    /// <summary>타이틀 왼쪽에 시설 그림자를 자연스럽게 겹치는 미세 질감 그라데이션.</summary>
    [DisallowMultipleComponent]
    public sealed class TitleBackdropGraphic : MaskableGraphic
    {
        [Header("패널 색상")]
        [SerializeField] private Color panelTint = new Color(0.025f, 0.029f, 0.04f, 1f);
        [SerializeField, Range(0f, 1f)] private float panelOpacity = 0.84f;

        [Header("페이드와 비네트")]
        [SerializeField, Range(0f, 1f)] private float fadeStart = 0.29f;
        [SerializeField, Range(0f, 1f)] private float fadeEnd = 0.59f;
        [SerializeField, Range(0f, 0.35f)] private float vignetteStrength = 0.12f;

        [Header("미세 질감")]
        [SerializeField, Range(0f, 0.15f)] private float grainStrength = 0.045f;
        [SerializeField, Range(8, 64)] private int horizontalSegments = 40;
        [SerializeField, Range(6, 48)] private int verticalSegments = 24;

        protected override void Awake()
        {
            base.Awake();
            raycastTarget = false;
        }

        protected override void OnPopulateMesh(VertexHelper vertexHelper)
        {
            vertexHelper.Clear();
            Rect rect = GetPixelAdjustedRect();
            int columns = Mathf.Max(2, horizontalSegments);
            int rows = Mathf.Max(2, verticalSegments);

            for (int y = 0; y <= rows; y++)
            {
                float v = y / (float)rows;
                for (int x = 0; x <= columns; x++)
                {
                    float u = x / (float)columns;
                    Color vertexColor = EvaluateColor(u, v);
                    vertexHelper.AddVert(
                        new Vector3(rect.xMin + rect.width * u, rect.yMin + rect.height * v),
                        vertexColor,
                        new Vector2(u, v));
                }
            }

            int rowStride = columns + 1;
            for (int y = 0; y < rows; y++)
            {
                for (int x = 0; x < columns; x++)
                {
                    int bottomLeft = y * rowStride + x;
                    int bottomRight = bottomLeft + 1;
                    int topLeft = bottomLeft + rowStride;
                    int topRight = topLeft + 1;
                    vertexHelper.AddTriangle(bottomLeft, topLeft, topRight);
                    vertexHelper.AddTriangle(bottomLeft, topRight, bottomRight);
                }
            }
        }
#if UNITY_EDITOR
        protected override void OnValidate()
        {
            base.OnValidate();
            fadeEnd = Mathf.Max(fadeStart + 0.01f, fadeEnd);
            SetVerticesDirty();
        }
#endif
        private Color EvaluateColor(float u, float v)
        {
            float fade = 1f - SmoothStep(fadeStart, fadeEnd, u);
            float edge = Mathf.Abs(v - 0.5f) * 2f;
            float vignette = 1f - vignetteStrength * (1f - edge * edge);
            float grain = (Hash(u, v) - 0.5f) * grainStrength;
            Color color = panelTint;
            color.a *= panelOpacity * fade * vignette * (1f + grain);
            float luminanceVariation = 1f + grain * 0.24f;
            color.r *= luminanceVariation;
            color.g *= luminanceVariation;
            color.b *= luminanceVariation;
            return color;
        }

        private static float SmoothStep(float edge0, float edge1, float value)
        {
            float t = Mathf.Clamp01((value - edge0) / Mathf.Max(0.001f, edge1 - edge0));
            return t * t * (3f - 2f * t);
        }

        private static float Hash(float u, float v)
        {
            int x = Mathf.RoundToInt(u * 127f);
            int y = Mathf.RoundToInt(v * 127f);
            unchecked
            {
                uint hash = (uint)(x * 374761393 + y * 668265263);
                hash = (hash ^ (hash >> 13)) * 1274126177u;
                hash ^= hash >> 16;
                return (hash & 0x00ffffffu) / 16777215f;
            }
        }
    }
}
