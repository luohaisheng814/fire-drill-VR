using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 28 轮：灭火器瓶身上方的「灭火剂类型」小标签。
    /// 玩家走到瓶前一眼就能看出这是二氧化碳、干粉还是水基 ——
    /// 三种灭火剂现在喷射形态也不同，标签必须能对上号。
    /// </summary>
    public class ExtinguisherKindTag : MonoBehaviour
    {
        [Header("内容")]
        public ExtinguisherKind kind = ExtinguisherKind.PowderABC;

        [Header("摆放")]
        [Tooltip("标签相对瓶身的本地位置")]
        public Vector3 localOffset = new Vector3(0f, 0.66f, 0f);
        [Tooltip("世界尺寸（米）")]
        public Vector2 sizeMeters = new Vector2(0.72f, 0.30f);
        [Tooltip("标题字号（画布像素）")]
        public int fontSize = 48;
        [Tooltip("朝相机方向的让位距离（米），避免标签插进背后墙面")]
        public float pushFromWall = 0.10f;

        [Header("行为")]
        [Tooltip("拿在手上时隐藏，避免贴脸挡视线")]
        public bool hideWhenHeld = true;

        Transform _root;
        Canvas _canvas;
        ToggleGrab _grab;
        Camera _cam;

        void Awake()
        {
            _grab = GetComponentInParent<ToggleGrab>();
            Build();
        }

        void OnDestroy()
        {
            if (_root != null)
            {
                if (Application.isPlaying) Destroy(_root.gameObject);
                else DestroyImmediate(_root.gameObject);
            }
        }

        /// <summary>由装配脚本设置类型后重建标签文案与配色。</summary>
        public void ApplyKind(ExtinguisherKind newKind)
        {
            kind = newKind;
            if (_canvas != null) DestroyTag();
            Build();
        }

        void Build()
        {
            var name = "KindTag_" + ExtinguisherSfx.KindToken(kind);
            var go = new GameObject(name, typeof(RectTransform));
            // 不挂在瓶身下面：瓶体常带非等比缩放（压扁/拉高），会把标签文字一起压歪，
            // 这里直接放进场景根，LateUpdate 每帧按瓶身锚点摆世界坐标（见 basePos）。
            go.transform.SetParent(null, false);
            go.transform.position = transform.TransformPoint(localOffset);
            go.transform.localRotation = Quaternion.identity;
            _root = go.transform;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 300;
            _canvas = canvas;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000f, Mathf.Round(1000f * sizeMeters.y / sizeMeters.x));
            float scale = sizeMeters.x / 1000f;
            rt.localScale = new Vector3(scale, scale, scale);

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var img = bgGo.GetComponent<Image>();
            Color band = ExtinguisherKindInfo.BandColor(kind);
            img.color = new Color(band.r * 0.22f, band.g * 0.22f, band.b * 0.22f, 0.92f);
            img.raycastTarget = false;
            Stretch(bgGo.GetComponent<RectTransform>());

            // 左侧色条：与瓶身色带同色，远处也能靠颜色分辨
            var stripGo = new GameObject("Strip", typeof(RectTransform), typeof(Image));
            stripGo.transform.SetParent(go.transform, false);
            var strip = stripGo.GetComponent<Image>();
            strip.color = band;
            strip.raycastTarget = false;
            var stripRt = stripGo.GetComponent<RectTransform>();
            stripRt.anchorMin = new Vector2(0f, 0f);
            stripRt.anchorMax = new Vector2(0f, 1f);
            stripRt.pivot = new Vector2(0f, 0.5f);
            stripRt.sizeDelta = new Vector2(rt.sizeDelta.y * 0.18f, 0f);
            stripRt.anchoredPosition = Vector2.zero;
            stripRt.offsetMin = new Vector2(0f, 0f);
            stripRt.offsetMax = new Vector2(stripRt.sizeDelta.x, 0f);

            // 两行：第一行类型名（大），第二行适用火源（小）——1.5 米外也能读清
            MakeRow(go.transform, "Name", ExtinguisherKindInfo.Name(kind), fontSize, FontStyle.Bold,
                    new Color(0.96f, 0.97f, 1f), TextAnchor.MiddleLeft,
                    new Vector2(0f, 0.42f), new Vector2(1f, 1f));
            MakeRow(go.transform, "Suitable", "适用：" + ExtinguisherKindInfo.SuitableFor(kind),
                    Mathf.Max(18, fontSize - 14), FontStyle.Normal,
                    new Color(0.86f, 0.92f, 1f), TextAnchor.MiddleLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 0.42f));
        }

        void MakeRow(Transform parent, string name, string content, int size, FontStyle style,
                     Color color, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.font = UIFont.Resolve();
            if (txt.font != null) txt.material = txt.font.material;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = anchor;
            txt.horizontalOverflow = HorizontalWrapMode.Overflow;   // 宁可溢出也不吞字
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            float pad = 1000f * 0.095f;      // 左侧让开色条
            rt.offsetMin = new Vector2(pad, 2f);
            rt.offsetMax = new Vector2(-8f, -2f);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void LateUpdate()
        {
            if (_root == null) return;

            bool hide = hideWhenHeld && _grab != null && _grab.IsHeld;
            if (_root.gameObject.activeSelf == hide) _root.gameObject.SetActive(!hide);
            if (hide) return;

            if (_cam == null) _cam = Camera.main;
            if (_cam == null) return;

            // 只绕 Y 轴朝向玩家：标签始终水平不歪，抬头低头都看得清
            Vector3 basePos = transform.TransformPoint(localOffset);
            Vector3 toCam = _cam.transform.position - basePos;
            toCam.y = 0f;
            if (toCam.sqrMagnitude <= 0.0001f) return;

            // 往玩家方向让出一点，别和背后的墙面/告示牌贴在一起
            _root.position = basePos + toCam.normalized * pushFromWall;
            _root.rotation = Quaternion.LookRotation(-toCam.normalized, Vector3.up);
        }

        void DestroyTag()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
            _root = null; _canvas = null;
        }
    }
}
