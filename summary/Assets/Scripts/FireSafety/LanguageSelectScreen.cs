using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 41 轮：程序启动时先弹出的语言选择页（中文 / 한국어 / English）。
    /// 选完才放开始界面，之后全项目 UI 文案都由 DrillLocale 按所选语言渲染。
    /// 世界空间画布，挂在相机正前方，鼠标/手柄射线都能点。
    /// </summary>
    [DefaultExecutionOrder(-500)]
    public class LanguageSelectScreen : MonoBehaviour
    {
        public static LanguageSelectScreen Instance { get; private set; }

        [Header("布局")]
        public float distanceFromCamera = 1.15f;
        public float verticalOffset = -0.02f;
        public float canvasScale = 0.001f;
        public Vector2 panelSize = new Vector2(900f, 620f);

        [Header("文案")]
        public string titleCn = "选择语言";
        public string titleEn = "Choose your language";
        public string titleKo = "언어를 선택하세요";
        public string subCn = "选择后，演练中的题目、提示与知识墙都会用该语言显示";
        public string subEn = "All questions, prompts and knowledge boards will use this language";
        public string subKo = "선택한 언어로 모든 문제·안내·지식 안내판이 표시됩니다";

        [Header("行为")]
        [Tooltip("弹语言页时先把开始界面藏起来，选完再放出来")]
        public bool hideStartScreen = true;
        [Tooltip("只在本次运行弹一次（切场景/重开演练不再拦截）")]
        public bool showOnlyOncePerRun = true;

        public bool IsOpen { get; private set; }
        public DrillLocale.Lang SelectedLanguage => DrillLocale.Language;

        [Tooltip("语言选项数量（中文 / 한국어 / English，仅供自检核对）")]
        public int OptionCount = 3;

        static bool _shownThisRun;

        Canvas _canvas;
        Transform _panel;
        readonly List<Button> _buttons = new List<Button>();

        public int ButtonCount => _buttons.Count;

        void Awake()
        {
            Instance = this;
            if (showOnlyOncePerRun && _shownThisRun) return;
            _shownThisRun = true;

            if (hideStartScreen)
            {
                var start = FindScreen();
                if (start != null) start.SetVisible(false);
            }
            Build();
            IsOpen = true;
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        static StartScreen FindScreen()
        {
            if (StartScreen.Instance != null) return StartScreen.Instance;
            return FindObjectOfType<StartScreen>();
        }

        void Build()
        {
            var go = new GameObject("LanguageSelectCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 400;
            var scaler = go.GetComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 1f;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = panelSize;
            rt.localScale = new Vector3(canvasScale, canvasScale, canvasScale);

            // 背板
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image), typeof(VerticalLayoutGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.04f, 0.07f, 0.12f, 0.96f);
            img.raycastTarget = true;
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 16f;
            layout.padding = new RectOffset(40, 40, 36, 36);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = panelSize;
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            _panel = panel.transform;

            AddText(DrillLocale.T(titleCn, titleEn, titleKo), 54, new Color(1f, 0.85f, 0.28f), 92f);
            AddText(DrillLocale.T(subCn, subEn, subKo), 22, new Color(0.78f, 0.86f, 0.96f), 96f);

            AddLanguageButton(DrillLocale.Lang.Chinese, "中文", "简体中文 · 消防演练", new Color(0.72f, 0.24f, 0.20f));
            AddLanguageButton(DrillLocale.Lang.Korean, "한국어", "한국어 · 소방 훈련", new Color(0.20f, 0.42f, 0.62f));
            AddLanguageButton(DrillLocale.Lang.English, "English", "English · Fire drill", new Color(0.16f, 0.52f, 0.32f));
        }

        void AddLanguageButton(DrillLocale.Lang lang, string label, string hint, Color color)
        {
            var go = new GameObject("Btn_" + lang, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(_panel, false);
            var img = go.GetComponent<Image>();
            img.color = color;
            img.raycastTarget = true;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSize.x - 80f, 96f);

            var text = new GameObject("Label", typeof(RectTransform), typeof(Text));
            text.transform.SetParent(go.transform, false);
            var t = text.GetComponent<Text>();
            t.text = label + "    " + hint;
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.fontSize = 32;
            t.fontStyle = FontStyle.Bold;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            var trt = text.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;

            var feedback = go.AddComponent<ButtonFeedback>();
            feedback.Init(color);
            btn.onClick.AddListener(() => Choose(lang));
            _buttons.Add(btn);
        }

        void AddText(string content, int size, Color color, float height)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(_panel, false);
            var t = go.GetComponent<Text>();
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Truncate;
            t.raycastTarget = false;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(panelSize.x - 80f, height);
        }

        /// <summary>选语言：写入 PlayerPrefs → 刷新字体缓存 → 关掉语言页 → 放出开始界面。</summary>
        public void Choose(DrillLocale.Lang lang)
        {
            DrillLocale.Language = lang;
            DrillLocale.NotifyChanged();
            UIFont.Invalidate();

            Close();

            var start = FindScreen();
            if (start != null)
            {
                start.RefreshLanguage();
                start.SetVisible(true);
            }
            Debug.Log("[Lang] 已选择语言：" + DrillLocale.LanguageName(lang));
        }

        public void Close()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void LateUpdate()
        {
            ForcePlace();
        }

        /// <summary>把语言页摆到主相机正前方（也供验收脚本在截图前主动调用一次）。</summary>
        public void ForcePlace()
        {
            if (!IsOpen || _canvas == null) return;
            var cam = Camera.main;
            if (cam == null) cam = FindObjectOfType<Camera>();
            if (cam == null) return;
            if (!_canvas.gameObject.activeSelf) _canvas.gameObject.SetActive(true);
            _canvas.transform.position = cam.transform.position
                                        + cam.transform.forward * distanceFromCamera
                                        + Vector3.up * verticalOffset;
            _canvas.transform.rotation = cam.transform.rotation;
        }
    }
}
