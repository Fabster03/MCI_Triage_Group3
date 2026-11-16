using MciTriage.Core;
using Unity.XR.CoreUtils;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace MciTriage.Runtime
{
    /// <summary>
    /// Builds simple world-space uGUI elements in code, so the placeholder UI needs
    /// no prefabs. Coordinates are in canvas pixels measured from the top-left corner.
    /// </summary>
    public static class UiKit
    {
        private static Font _font;

        public static Font Font => _font != null ? _font : (_font = Resources.GetBuiltinResource<Font>("LegacyRuntime.ttf"));

        public static readonly Color PanelColor = new Color(0.08f, 0.09f, 0.11f, 0.94f);
        public static readonly Color ButtonColor = new Color(0.24f, 0.27f, 0.32f, 1f);
        public static readonly Color TextColor = new Color(0.95f, 0.95f, 0.95f, 1f);
        public static readonly Color MutedText = new Color(0.7f, 0.72f, 0.76f, 1f);

        /// <summary>World-space canvas usable by XR rays (TrackedDeviceGraphicRaycaster) and the mouse (GraphicRaycaster).</summary>
        public static Canvas CreateWorldCanvas(string name, Transform parent, Vector2 sizePx, float metersPerPixel)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler),
                typeof(GraphicRaycaster), typeof(TrackedDeviceGraphicRaycaster));
            go.transform.SetParent(parent, false);

            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.worldCamera = SceneCamera.Get();

            var rt = (RectTransform)go.transform;
            rt.sizeDelta = sizePx;
            rt.localScale = Vector3.one * metersPerPixel;

            go.GetComponent<CanvasScaler>().dynamicPixelsPerUnit = 4f; // sharper text in world space
            return canvas;
        }

        public static RectTransform Place(RectTransform rt, float x, float y, float w, float h)
        {
            rt.anchorMin = rt.anchorMax = new Vector2(0, 1);
            rt.pivot = new Vector2(0, 1);
            rt.anchoredPosition = new Vector2(x, -y);
            rt.sizeDelta = new Vector2(w, h);
            return rt;
        }

        public static Image Panel(Transform parent, float x, float y, float w, float h, Color color)
        {
            var go = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, w, h);
            var image = go.GetComponent<Image>();
            image.color = color;
            return image;
        }

        public static Text Label(Transform parent, float x, float y, float w, float h, string text, int size,
            TextAnchor anchor = TextAnchor.MiddleLeft, FontStyle style = FontStyle.Normal, Color? color = null)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            Place((RectTransform)go.transform, x, y, w, h);
            var t = go.GetComponent<Text>();
            t.font = Font;
            t.text = text;
            t.fontSize = size;
            t.fontStyle = style;
            t.alignment = anchor;
            t.color = color ?? TextColor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            return t;
        }

        public static Button Button(Transform parent, float x, float y, float w, float h, string label, int size,
            UnityAction onClick, Color? background = null, Color? textColor = null)
        {
            var image = Panel(parent, x, y, w, h, background ?? ButtonColor);
            image.gameObject.name = "Button " + label;
            var button = image.gameObject.AddComponent<Button>();
            var colors = button.colors;
            colors.highlightedColor = new Color(1.15f, 1.15f, 1.15f, 1f);
            colors.pressedColor = new Color(0.75f, 0.75f, 0.75f, 1f);
            button.colors = colors;
            if (onClick != null) button.onClick.AddListener(onClick);

            var text = Label(image.transform, 0, 0, w, h, label, size, TextAnchor.MiddleCenter, FontStyle.Bold, textColor);
            var rt = (RectTransform)text.transform;
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.pivot = new Vector2(0.5f, 0.5f);
            rt.anchoredPosition = Vector2.zero;
            rt.sizeDelta = Vector2.zero;
            return button;
        }

        public static string FormatTime(double seconds)
        {
            if (double.IsInfinity(seconds)) return "--:--";
            var s = Mathf.Max(0, Mathf.CeilToInt((float)seconds));
            return $"{s / 60}:{s % 60:00}";
        }

        /// <summary>Rotates <paramref name="t"/> around Y so its UI front faces the camera.</summary>
        public static void FaceCamera(Transform t)
        {
            var cam = SceneCamera.Get();
            if (cam == null) return;
            var dir = t.position - cam.transform.position;
            dir.y = 0;
            if (dir.sqrMagnitude > 1e-4f) t.rotation = Quaternion.LookRotation(dir);
        }
    }

    public static class TriageColors
    {
        public static Color Get(TriageCategory c)
        {
            switch (c)
            {
                case TriageCategory.Immediate: return new Color(0.86f, 0.12f, 0.12f);
                case TriageCategory.Delayed: return new Color(0.96f, 0.80f, 0.10f);
                case TriageCategory.Minor: return new Color(0.16f, 0.66f, 0.22f);
                case TriageCategory.Expectant: return new Color(0.06f, 0.06f, 0.06f);
                default: return Color.white;
            }
        }

        public static Color TextOn(TriageCategory c) =>
            c == TriageCategory.Delayed ? Color.black : Color.white;
    }

    /// <summary>Finds the XR rig camera (falls back to Camera.main, e.g. without headset).</summary>
    public static class SceneCamera
    {
        private static Camera _camera;

        public static Camera Get()
        {
            if (_camera != null && _camera.isActiveAndEnabled) return _camera;
            var origin = Object.FindFirstObjectByType<XROrigin>();
            _camera = origin != null && origin.Camera != null ? origin.Camera : Camera.main;
            return _camera;
        }
    }
}
