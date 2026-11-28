using System.Collections;
using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 35 轮：玩家视角的即时提示条（世界空间，跟随相机）。
    /// 用于「太远了 / 太近了 / 被挡住 / 没拔销」等短反馈，桌面和头显都可见。
    /// 同时仍会把同一句话推给桌面底部 HUD，避免玩家只看 HUD 时漏掉。
    /// </summary>
    public class DrillToast : MonoBehaviour
    {
        public static DrillToast Instance { get; private set; }

        public float distance = 1.35f;
        public float verticalOffset = -0.20f;
        public float defaultSeconds = 3.2f;

        Canvas _canvas;
        CanvasGroup _group;
        Text _text;
        Image _accent;
        float _until;
        Coroutine _fade;

        public static void Show(string chinese, string english = null, float seconds = -1f)
        {
            if (!Application.isPlaying) return;
            Ensure();
            if (Instance == null) return;
            Instance.ShowInternal(DrillLocale.T(chinese, english), seconds);
        }

        public static void ShowRaw(string message, float seconds = -1f)
        {
            if (!Application.isPlaying) return;
            Ensure();
            if (Instance == null) return;
            Instance.ShowInternal(message, seconds);
        }

        static void Ensure()
        {
            if (Instance != null) return;
            var go = new GameObject("DrillToast");
            Instance = go.AddComponent<DrillToast>();
            Instance.Build();
        }

        void Awake()
        {
            if (Instance != null && Instance != this) { Destroy(gameObject); return; }
            Instance = this;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        void Build()
        {
            var go = new GameObject("ToastCanvas", typeof(RectTransform), typeof(Canvas), typeof(CanvasGroup));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 500;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1200f, 240f);
            rt.localScale = Vector3.one * 0.001f;
            _group = go.GetComponent<CanvasGroup>();
            _group.blocksRaycasts = false;
            _group.interactable = false;

            var bg = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bg.transform.SetParent(go.transform, false);
            var img = bg.GetComponent<Image>();
            img.color = new Color(0.03f, 0.06f, 0.10f, 0.94f);
            img.raycastTarget = false;
            Stretch(bg.GetComponent<RectTransform>());

            var accentGo = new GameObject("Accent", typeof(RectTransform), typeof(Image));
            accentGo.transform.SetParent(go.transform, false);
            _accent = accentGo.GetComponent<Image>();
            _accent.color = new Color(0.20f, 0.86f, 1f, 1f);
            _accent.raycastTarget = false;
            var art = accentGo.GetComponent<RectTransform>();
            art.anchorMin = new Vector2(0f, 0f);
            art.anchorMax = new Vector2(0f, 1f);
            art.pivot = new Vector2(0f, 0.5f);
            art.sizeDelta = new Vector2(14f, 0f);
            art.anchoredPosition = Vector2.zero;

            var textGo = new GameObject("Text", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            _text = textGo.GetComponent<Text>();
            _text.font = UIFont.Resolve();
            if (_text.font != null) _text.material = _text.font.material;
            _text.fontSize = 44;
            _text.fontStyle = FontStyle.Bold;
            _text.color = new Color(1f, 0.97f, 0.88f, 1f);
            _text.alignment = TextAnchor.MiddleLeft;
            _text.horizontalOverflow = HorizontalWrapMode.Wrap;
            _text.verticalOverflow = VerticalWrapMode.Overflow;
            _text.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(40f, 16f);
            trt.offsetMax = new Vector2(-32f, -16f);

            _canvas.gameObject.SetActive(false);
        }

        static void Stretch(RectTransform rt)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = Vector2.zero;
            rt.offsetMax = Vector2.zero;
        }

        void ShowInternal(string message, float seconds)
        {
            // 第 50 轮：答题 / 结果页阅读时需要完整看清 5 题讲解，烟雾伤害等世界提示不要压在面板上。
            var quiz = FindFirstObjectByType<ExtinguisherQuizPanel>();
            if (quiz != null && quiz.IsOpen) return;

            if (message == null) message = "";
            _text.text = message;
            _accent.color = AccentFor(message);
            _canvas.gameObject.SetActive(true);
            _until = Time.unscaledTime + (seconds > 0f ? seconds : defaultSeconds);

            if (_fade != null) StopCoroutine(_fade);
            _group.alpha = 1f;
            _fade = StartCoroutine(FollowAndFade());
        }

        static Color AccentFor(string message)
        {
            if (message.Contains("太近") || message.Contains("too close") || message.Contains("Too close"))
                return new Color(1f, 0.58f, 0.22f);
            if (message.Contains("太远") || message.Contains("too far") || message.Contains("Too far"))
                return new Color(1f, 0.80f, 0.22f);
            if (message.Contains("挡") || message.Contains("block") || message.Contains("Block"))
                return new Color(1f, 0.35f, 0.28f);
            if (message.Contains("合适") || message.Contains("Good") || message.Contains("perfect"))
                return new Color(0.28f, 0.88f, 0.42f);
            return new Color(0.30f, 0.72f, 1f);
        }

        IEnumerator FollowAndFade()
        {
            while (Time.unscaledTime < _until)
            {
                Camera cam = Camera.main;
                if (cam != null && _canvas != null)
                {
                    Transform c = cam.transform;
                    Vector3 pos = c.position + c.forward * distance + c.up * verticalOffset;
                    _canvas.transform.position = pos;
                    _canvas.transform.rotation = Quaternion.LookRotation(pos - c.position, Vector3.up);
                }
                yield return null;
            }

            float t = 0f;
            const float fade = 0.22f;
            while (t < fade && _group != null)
            {
                t += Time.unscaledDeltaTime;
                _group.alpha = 1f - Mathf.Clamp01(t / fade);
                yield return null;
            }
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            _fade = null;
        }

        /// <summary>立即收起提示条（打开答题面板等新 UI 时调用，避免旧提示压在新面板上）。</summary>
        public static void HideNow()
        {
            if (Instance == null) return;
            if (Instance._fade != null) { Instance.StopCoroutine(Instance._fade); Instance._fade = null; }
            if (Instance._canvas != null) Instance._canvas.gameObject.SetActive(false);
        }

        /// <summary>自检/探针用：当前提示条文字。</summary>
        public string CurrentText => _text != null ? _text.text : "";

        /// <summary>自检/探针用：是否正在显示。</summary>
        public bool IsVisible => _canvas != null && _canvas.gameObject.activeSelf && _group != null && _group.alpha > 0.05f;
    }
}
