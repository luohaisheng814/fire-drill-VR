using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 火焰血量显示（世界空间，悬在火焰上方，始终朝向玩家）：
    ///   火条 + 数字「HP 100 / 100」，随灭火进度实时下降；火焰扑灭后淡出隐藏。
    /// 数据直接读 FireSource.progress01（0 = 满血，1 = 熄灭），不额外维护一份进度。
    /// </summary>
    [DisallowMultipleComponent]
    public class FireHealthBar : MonoBehaviour
    {
        [Header("数据源")]
        public FireSource fire;

        [Header("显示")]
        [Tooltip("血量上限（数字与百分比都按它换算）")]
        public int maxHp = 100;
        [Tooltip("血条悬在火焰瞄准点上方多少米（火焰根部 + 该高度）")]
        public float heightOffset = 1.15f;
        [Tooltip("画布缩放：420 单位 × 0.0016 ≈ 0.67 米宽，3 米内可读")]
        public float canvasScale = 0.0016f;
        [Tooltip("扑灭后血条淡出时长（秒）")]
        public float fadeOutSeconds = 0.35f;
        [Tooltip("面板标题")]
        public string titleText = "火焰强度";

        [Header("颜色阈值")]
        [Tooltip("高于该百分比：橙红")]
        public float highThreshold = 0.6f;
        [Tooltip("高于该百分比：橙黄；低于则深红")]
        public float midThreshold = 0.3f;

        /// <summary>当前血量（0~maxHp），供自检 / 探针断言。</summary>
        public int CurrentHp { get; private set; }
        public int MaxHp => maxHp;
        /// <summary>血条是否仍可见（火灭并淡出后为 false）。</summary>
        public bool Visible => _canvas != null && _canvas.gameObject.activeSelf && _group != null && _group.alpha > 0.01f;

        Canvas _canvas;
        CanvasGroup _group;
        Image _fill;
        Text _valueText;
        RectTransform _fillRect;
        float _fillWidth;
        float _fadeTimer = -1f;
        Transform _head;
        bool _hudForcedHidden;

        /// <summary>教学/开始面板阅读期间隐藏火焰血条。</summary>
        public void SetHudVisible(bool visible)
        {
            _hudForcedHidden = !visible;
            if (_canvas != null && (!visible || _hudForcedHidden)) _canvas.gameObject.SetActive(false);
        }

        /// <summary>外部（流程脚本/自检）强制刷新一次：AddComponent 当帧 Start 还没跑，先刷出 HP 100。</summary>
        public void RefreshNow() => Refresh(true);

        private void Awake()
        {
            BuildUI();
        }

        private void Start()
        {
            if (fire == null) fire = GetComponentInParent<FireSource>();
            if (fire == null) fire = GetComponent<FireSource>();
            if (_head == null && Camera.main != null) _head = Camera.main.transform;
            Refresh(true);
        }

        private void LateUpdate()
        {
            if (fire == null) return;
            if (_head == null && Camera.main != null) _head = Camera.main.transform;

            // 火灭 → 淡出；重新点燃（IgniteNow 把 progress 归零）→ 重新显示
            if (fire.IsExtinguished || fire.progress01 >= 0.999f)
            {
                if (_fadeTimer < 0f && _group != null && _group.alpha > 0.01f) _fadeTimer = 0f;
            }
            else
            {
                _fadeTimer = -1f;
                if (_group != null) _group.alpha = 1f;
                if (!_hudForcedHidden && _canvas != null && !_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);
            }

            if (_fadeTimer >= 0f && _group != null)
            {
                _fadeTimer += Time.deltaTime;
                _group.alpha = Mathf.Clamp01(1f - _fadeTimer / Mathf.Max(0.01f, fadeOutSeconds));
                if (_group.alpha <= 0.01f && _canvas != null) _canvas.gameObject.SetActive(false);
            }

            PositionAndFace();
            Refresh(false);
        }

        private void PositionAndFace()
        {
            if (_canvas == null) return;
            Vector3 aim = fire != null ? fire.AimPoint : transform.position;
            _canvas.transform.position = aim + Vector3.up * heightOffset;

            if (_head == null) return;
            Vector3 toHead = _head.position - _canvas.transform.position;
            if (toHead.sqrMagnitude < 0.0001f) return;
            _canvas.transform.rotation = Quaternion.LookRotation(-toHead.normalized, Vector3.up);
        }

        private void Refresh(bool force)
        {
            if (_valueText == null || _fill == null) return;
            if (fire == null) return;

            float remain = Mathf.Clamp01(1f - fire.progress01);
            CurrentHp = Mathf.RoundToInt(remain * Mathf.Max(1, maxHp));

            _valueText.text = "HP " + CurrentHp.ToString("000") + " / " + maxHp.ToString("000");

            if (_fillRect != null && _fillWidth > 0f)
            {
                float ratio = (float)CurrentHp / Mathf.Max(1, maxHp);
                _fillRect.sizeDelta = new Vector2(Mathf.Max(2f, _fillWidth * ratio), _fillRect.sizeDelta.y);
            }

            if (_fill != null)
            {
                float ratio = (float)CurrentHp / Mathf.Max(1, maxHp);
                if (ratio > highThreshold) _fill.color = new Color(1f, 0.34f, 0.18f, 1f);
                else if (ratio > midThreshold) _fill.color = new Color(1f, 0.62f, 0.12f, 1f);
                else _fill.color = new Color(0.88f, 0.12f, 0.10f, 1f);
            }
        }

        // ---------------- UI ----------------

        private void BuildUI()
        {
            if (_canvas != null) return;

            var go = new GameObject("FireHealthCanvas");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 150;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(420f, 132f);
            rt.localScale = Vector3.one * canvasScale;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            // 底板
            var bg = new GameObject("BG", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var bgImg = bg.GetComponent<Image>();
            bgImg.color = new Color(0.04f, 0.05f, 0.07f, 0.86f);
            bgImg.raycastTarget = false;
            Stretch(bg.GetComponent<RectTransform>());

            // 标题
            var title = MakeText(go.transform, titleText, 24, new Color(1f, 0.82f, 0.35f), new Vector2(230f, 30f),
                new Vector2(14f, -8f), TextAnchor.MiddleLeft);
            title.raycastTarget = false;

            // 条背景
            var barBg = new GameObject("BarBG", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(go.transform, false);
            var barBgImg = barBg.GetComponent<Image>();
            barBgImg.color = new Color(0.16f, 0.05f, 0.04f, 1f);
            barBgImg.raycastTarget = false;
            var barRt = barBg.GetComponent<RectTransform>();
            barRt.anchorMin = new Vector2(0f, 1f);
            barRt.anchorMax = new Vector2(0f, 1f);
            barRt.pivot = new Vector2(0f, 1f);
            barRt.anchoredPosition = new Vector2(14f, -44f);
            _fillWidth = 392f;
            barRt.sizeDelta = new Vector2(_fillWidth, 30f);

            // 条填充（左对齐，按比例改宽度）
            var fill = new GameObject("BarFill", typeof(RectTransform), typeof(Image));
            fill.transform.SetParent(barBg.transform, false);
            _fill = fill.GetComponent<Image>();
            _fill.color = new Color(1f, 0.34f, 0.18f, 1f);
            _fill.raycastTarget = false;
            _fillRect = fill.GetComponent<RectTransform>();
            _fillRect.anchorMin = new Vector2(0f, 0f);
            _fillRect.anchorMax = new Vector2(0f, 1f);
            _fillRect.pivot = new Vector2(0f, 0.5f);
            _fillRect.anchoredPosition = Vector2.zero;
            _fillRect.sizeDelta = new Vector2(_fillWidth, 0f);

            // 数字
            _valueText = MakeText(go.transform, "HP 100 / 100", 26, Color.white, new Vector2(240f, 36f),
                new Vector2(-16f, 10f), TextAnchor.MiddleRight);
            var vrt = _valueText.GetComponent<RectTransform>();
            vrt.anchorMin = new Vector2(1f, 0f);
            vrt.anchorMax = new Vector2(1f, 0f);
            vrt.pivot = new Vector2(1f, 0f);
        }

        private static Text MakeText(Transform parent, string content, int size, Color color,
            Vector2 sizeDelta, Vector2 anchoredPos, TextAnchor anchor)
        {
            var t = new GameObject("Text", typeof(RectTransform), typeof(Text));
            t.transform.SetParent(parent, false);
            var text = t.GetComponent<Text>();
            text.text = content;
            text.fontSize = size;
            text.color = color;
            text.font = UIFont.Resolve();
            if (text.font != null) text.material = text.font.material;
            text.alignment = anchor;
            text.horizontalOverflow = HorizontalWrapMode.Overflow;
            text.verticalOverflow = VerticalWrapMode.Overflow;
            var rt = t.GetComponent<RectTransform>();
            rt.anchorMin = new Vector2(0f, 1f);
            rt.anchorMax = new Vector2(0f, 1f);
            rt.pivot = new Vector2(0f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchoredPos;
            return text;
        }

        private static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }
    }
}
