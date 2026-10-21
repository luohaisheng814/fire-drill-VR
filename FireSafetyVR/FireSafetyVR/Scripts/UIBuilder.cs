using UnityEngine;
using UnityEngine.UI;

namespace FireSafetyVR
{

    /// <summary>
    /// 纯代码生成 UI（不需要任何预制体、不依赖 TextMeshPro）。
    /// 供 GameManager、QuizPanel 生成开场面板 / HUD / 问答面板 / 结算面板。
    /// </summary>
    public static class UIBuilder
    {
        /// <summary>
        /// 中文字体：把任意 .ttf/.otf 中文字体放进 Assets/Resources/Fonts/
        /// 并命名为 ChineseFont（扩展名随意），这里会自动加载，中文就不会显示成方块。
        /// 也可以在任意脚本 Awake 里手动赋值覆盖。
        /// </summary>
        public static Font chineseFont;

        static Font ResolveFont()
        {
            if (chineseFont != null) return chineseFont;
            chineseFont = Resources.Load<Font>("Fonts/ChineseFont");
            if (chineseFont == null)
                chineseFont = Resources.GetBuiltinResource<Font>("Arial.ttf");
            return chineseFont;
        }

        public static Canvas EnsureCanvas(Canvas canvas, string name, Vector3 worldPos, float scale = 0.01f)
        {
            if (canvas != null) return canvas;

            var go = new GameObject(name);
            canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 100;

            var rt = canvas.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 700);
            rt.localScale = Vector3.one * scale;

            go.transform.position = worldPos;

            // VR 里让面板始终大致朝向玩家
            var bb = go.AddComponent<Billboard>();
            bb.smooth = 4f;
            bb.onlyYAxis = false;

            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            // 若已安装 XR Interaction Toolkit，请手动为该 Canvas 追加
            // "Tracked Device Graphic Raycaster" 组件，才能用手柄射线点击按钮。

            return canvas;
        }

        public static RectTransform Panel(Transform parent, string name, Vector2 size, Color bg)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;

            var layout = go.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10;
            layout.padding = new RectOffset(20, 20, 20, 20);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = size;
            return rt;
        }

        public static Text Text(Transform parent, string content, int fontSize, Color color,
            float height = 60f, TextAnchor align = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);

            var t = go.GetComponent<Text>();
            t.text = content;
            t.fontSize = fontSize;
            t.color = color;
            t.alignment = align;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.font = ResolveFont();
            if (t.font != null) t.material = t.font.material;

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);
            return t;
        }

        public static Button Button(Transform parent, string label, int fontSize,
            UnityEngine.Events.UnityAction onClick, Color? bg = null, float height = 70f)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);

            var img = go.GetComponent<Image>();
            img.color = bg ?? new Color(0.13f, 0.35f, 0.60f, 1f);
            img.raycastTarget = true;

            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            if (onClick != null) btn.onClick.AddListener(onClick);

            var le = go.AddComponent<LayoutElement>();
            le.preferredHeight = height;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(rt.sizeDelta.x, height);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(go.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.text = label;
            t.fontSize = fontSize;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.font = ResolveFont();
            if (t.font != null) t.material = t.font.material;

            var trt = labelGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            return btn;
        }

        /// <summary>把面板贴到世界坐标某点（例如灭火器上方），并让它朝向玩家。</summary>
        public static void PlaceInWorld(RectTransform rt, Vector3 worldPos)
        {
            var canvas = rt.GetComponentInParent<Canvas>();
            if (canvas == null) return;
            canvas.transform.position = worldPos;
        }
    }
}
