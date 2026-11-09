using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 头显内世界空间提示条：
    ///   · 只在头显连接（或探针虚拟模式）时显示，桌面模式完全隐藏，交给屏幕 HUD
    ///   · 固定在视线下方约 20 cm、前方 0.95 m，跟随头部平滑移动
    ///   · 一行连接状态 + 一行当前操作提示 + 一行手柄键位速查
    /// </summary>
    [DisallowMultipleComponent]
    public class VRDrillHud : MonoBehaviour
    {
        [Header("跟随")]
        public Transform head;
        [Tooltip("沿视线前方的距离（米）")]
        public float distance = 0.9f;
        [Tooltip("相对视线向下的偏移（米）")]
        public float verticalOffset = -0.4f;
        [Tooltip("跟随平滑（越大越跟手）")]
        public float followSpeed = 8f;
        [Tooltip("面板缩放：1 单位 = 1 米")]
        public float canvasScale = 0.00038f;

        [Header("文案")]
        public string title = "头显操作提示";
        public string keyLine = "左摇杆移动 · 右摇杆瞬转45° · Grip 抓取 · Trigger 拔销/压把/喷射 · A 开始/翻页 · B 换题 · 左摇杆按下蹲/站";

        [Header("淡出")]
        [Tooltip("无新提示时保持全亮的时长（秒）")]
        public float holdSeconds = 5f;
        [Tooltip("淡出到的最低透明度（标题与键位保留）")]
        public float idleAlpha = 0.55f;

        public string CurrentHint { get; private set; } = "";

        /// <summary>被模态面板（问答/完成/结算）暂时压暗：整块 HUD 淡到透明，避免和新面板叠在一起。</summary>
        public bool suppressed { get; set; }

        Canvas _canvas;
        CanvasGroup _group;
        Text _statusText;
        Text _hintText;
        Text _keyText;
        Transform _head;
        float _lastHintTime = -999f;
        bool _visible;

        /// <summary>本帧是否有头显（供 VR 输入层判断）。</summary>
        public bool Visible => _visible;

        void Awake()
        {
            ResolveHead();
            BuildUI();
        }

        void OnEnable()
        {
            ResolveHead();
            SetVisible(false);
        }

        void ResolveHead()
        {
            _head = head != null ? head : (Camera.main != null ? Camera.main.transform : null);
        }

        void BuildUI()
        {
            var go = new GameObject("Canvas_VRHud");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 320;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1500, 430);
            rt.localScale = Vector3.one * canvasScale;

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(CanvasGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.04f, 0.06f, 0.10f, 0.86f);
            img.raycastTarget = false;
            var prt = panel.GetComponent<RectTransform>();
            prt.anchorMin = Vector2.zero;
            prt.anchorMax = Vector2.one;
            prt.offsetMin = Vector2.zero;
            prt.offsetMax = Vector2.zero;
            _group = panel.GetComponent<CanvasGroup>();
            _group.alpha = 1f;

            _statusText = MakeText(panel.transform, title, 40, new Color(0.45f, 0.95f, 1f), 84f, TextAnchor.MiddleCenter);
            _hintText = MakeText(panel.transform, "", 36, Color.white, 150f, TextAnchor.MiddleCenter);
            _keyText = MakeText(panel.transform, keyLine, 26, new Color(0.75f, 0.82f, 0.9f), 160f, TextAnchor.MiddleCenter);

            // 纵向排列
            float y = 0f;
            foreach (var t in new[] { _statusText, _hintText, _keyText })
            {
                var trt = t.GetComponent<RectTransform>();
                trt.anchorMin = new Vector2(0f, 1f);
                trt.anchorMax = new Vector2(1f, 1f);
                trt.pivot = new Vector2(0.5f, 1f);
                trt.anchoredPosition = new Vector2(0f, -y);
                trt.offsetMin = new Vector2(28f, trt.offsetMin.y);
                trt.offsetMax = new Vector2(-28f, trt.offsetMax.y);
                y += trt.sizeDelta.y + 6f;
            }

            go.SetActive(false);
        }

        Text MakeText(Transform parent, string content, int size, Color color, float height, TextAnchor anchor)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(parent, false);
            var text = t.GetComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.font = UIFont.Resolve();
            if (text.font != null) text.material = text.font.material;
            text.horizontalOverflow = HorizontalWrapMode.Wrap;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            text.alignment = anchor;
            text.raycastTarget = false;
            var trt = t.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(1440, height);
            return text;
        }

        /// <summary>显示/隐藏整块提示（头显未连接时应为 false）。</summary>
        public void SetVisible(bool visible)
        {
            if (_canvas == null) return;
            _visible = visible;
            if (_canvas.gameObject.activeSelf != visible) _canvas.gameObject.SetActive(visible);
        }

        /// <summary>刷新状态行（例如“VR 头显已连接” / “等待头显连接”）。</summary>
        public void SetStatus(string status)
        {
            if (_statusText != null) _statusText.text = status;
        }

        /// <summary>推送一条操作提示，数秒后自动淡回键位提示。</summary>
        public void SetHint(string hint, bool sticky = false)
        {
            CurrentHint = hint;
            if (_hintText != null) _hintText.text = hint;
            _lastHintTime = sticky ? float.MaxValue : Time.time;
        }

        void LateUpdate()
        {
            if (!_visible) return;
            ResolveHead();
            if (_head == null) return;

            Vector3 desired = _head.position + _head.forward * distance + _head.up * verticalOffset;
            _canvas.transform.position = Vector3.Lerp(_canvas.transform.position, desired,
                1f - Mathf.Exp(-followSpeed * Time.deltaTime));

            // 面板始终正对玩家，只跟随偏航，避免头一低文字就糊
            Vector3 look = _canvas.transform.position - _head.position;
            look.y = 0f;
            if (look.sqrMagnitude > 0.0001f)
            {
                Quaternion target = Quaternion.LookRotation(look.normalized, Vector3.up);
                _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, target,
                    1f - Mathf.Exp(-followSpeed * Time.deltaTime));
            }

            if (_group != null)
            {
                if (suppressed || ExtinguisherQuizPanel.AnyOpen) _group.alpha = Mathf.Min(_group.alpha, 0.02f);   // 让位：立即压到不可见
                else
                {
                    float a = Time.time - _lastHintTime > holdSeconds ? idleAlpha : 1f;
                    _group.alpha = Mathf.Lerp(_group.alpha, a, 1f - Mathf.Exp(-4f * Time.deltaTime));
                }
            }
        }
    }
}
