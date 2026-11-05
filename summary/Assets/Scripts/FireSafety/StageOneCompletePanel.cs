using System.Collections;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 阶段一完成面板：火焰扑灭（⑨ 完成）后自动暂停倒计时并弹出，提供两个选择：
    ///   · 立刻结算：停表并回到开始/结算面板；
    ///   · 继续往下：关闭面板，倒计时从暂停处继续走。
    /// 面板按米制尺寸出现在玩家前方，鼠标可直接点按钮（桌面输入里已做了 UI 命中）。
    /// </summary>
    public class StageOneCompletePanel : MonoBehaviour
    {
        public static StageOneCompletePanel Instance { get; private set; }

        [Header("引用（留空自动查找）")]
        public DrillCountdown countdown;
        public StartScreen startScreen;

        [Header("文案")]
        public string title = "阶段一完成";
        public string buttonSettlement = "立刻结算";
        public string buttonContinue = "继续往下";

        [Header("位置")]
        public float distanceFromCamera = 1.2f;
        public float verticalOffset = 0f;

        [Header("事件")]
        public UnityEvent onSettlement = new UnityEvent();
        public UnityEvent onContinue = new UnityEvent();

        public bool IsOpen { get; private set; }

        Canvas _canvas;
        CanvasGroup _group;
        Text _summaryText;
        AudioSource _src;

        private void Awake()
        {
            Instance = this;
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            _src = GetComponent<AudioSource>();
            BuildUI();
        }

        private void Start()
        {
            var mgr = FireDrillManager.Instance;
            if (mgr != null)
            {
                mgr.onDrillCompleted.RemoveListener(OnDrillCompleted);
                mgr.onDrillCompleted.AddListener(OnDrillCompleted);
                if (mgr.IsCompleted) Show();
            }
        }

        private void OnDestroy()
        {
            var mgr = FireDrillManager.Instance;
            if (mgr != null) mgr.onDrillCompleted.RemoveListener(OnDrillCompleted);
            if (Instance == this) Instance = null;
        }

        private void OnDrillCompleted() => Show();

        /// <summary>弹出面板：暂停倒计时。</summary>
        public void Show()
        {
            if (IsOpen) return;
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (countdown != null) countdown.Pause();
            if (_canvas == null) BuildUI();

            RefreshSummary();
            PlaceInFrontOfCamera();
            _canvas.gameObject.SetActive(true);
            if (Application.isPlaying)
            {
                _group.alpha = 0f;
                UIFX.Fade(this, _group, 0f, 1f, 0.25f);
            }
            else _group.alpha = 1f;
            IsOpen = true;
            Play("Sounds/SFX/quiz_correct");
            Debug.Log("[StageOne] 阶段一完成面板已弹出，倒计时暂停。");
        }

        /// <summary>立刻结算：停表 + 回到开始/结算面板。</summary>
        public void Settlement()
        {
            Hide();
            if (countdown != null) countdown.StopAndReset();
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            if (startScreen != null)
                startScreen.ShowSettlement(SettlementText());
            onSettlement?.Invoke();
            Debug.Log("[StageOne] 选择立刻结算。");
        }

        /// <summary>继续往下：倒计时从暂停处继续。</summary>
        public void Continue()
        {
            Hide();
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (countdown != null) countdown.Resume();
            // 第 2 阶段：灭火完成后引导玩家沿地面箭头走到大厅
            if (GuidanceArrows.Instance != null) GuidanceArrows.Instance.BeginLobbyGuidance();
            onContinue?.Invoke();
            Debug.Log("[StageOne] 选择继续往下，倒计时继续，引导箭头指向大厅。");
        }

        private void Hide()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        private void RefreshSummary()
        {
            if (_summaryText == null) return;
            string time = countdown != null
                ? "倒计时已暂停 · 剩余 " + Format(countdown.Remaining)
                : "倒计时已暂停";
            _summaryText.text =
                "火焰已扑灭，拿起 → 拔销 → 压把 → 喷射 的完整流程已完成。\n" + time + "。\n\n" +
                "请选择：立刻结算，或继续往下进入第二阶段。";
        }

        private string SettlementText()
        {
            float used = 0f;
            if (countdown != null) used = Mathf.Max(0f, countdown.durationSeconds - countdown.Remaining);
            return "演练结算\n\n" +
                   "阶段一（灭火器操作流程）已完成：拿起灭火器 → 拔出保险销 → 按下压把 → 粒子喷射扑灭火焰。\n" +
                   "用时 " + Format(used) + "，剩余 " + Format(countdown != null ? countdown.Remaining : 0f) + "。\n\n" +
                   "如需重新体验，点击下方「重新演练」。";
        }

        private static string Format(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return $"{s / 60:00}:{s % 60:00}";
        }

        // ---------------- UI ----------------

        private void BuildUI()
        {
            var go = new GameObject("Canvas_StageOne");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 280;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 560);
            rt.localScale = Vector3.one * 0.0009f;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.12f, 0.95f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 14f;
            layout.padding = new RectOffset(30, 30, 26, 26);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(940, 500);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            AddText(panel.transform, title, 46, new Color(0.45f, 0.95f, 0.55f), 66f);
            _summaryText = AddText(panel.transform, "", 26, Color.white, 190f);

            var btnRow = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            btnRow.transform.SetParent(panel.transform, false);
            var hl = btnRow.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 24f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = false;
            var hrt = btnRow.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(880, 88);
            var le = btnRow.AddComponent<LayoutElement>();
            le.preferredHeight = 88f;

            var settleBtn = AddButton(btnRow.transform, buttonSettlement, 34, new Color(0.72f, 0.42f, 0.12f, 1f), 84f);
            settleBtn.onClick.AddListener(Settlement);
            var continueBtn = AddButton(btnRow.transform, buttonContinue, 34, new Color(0.12f, 0.55f, 0.30f, 1f), 84f);
            continueBtn.onClick.AddListener(Continue);

            AddText(panel.transform, "鼠标点击按钮选择（VR 可用手柄射线点按）", 22,
                new Color(0.72f, 0.72f, 0.72f), 40f);

            go.SetActive(false);
        }

        private Text AddText(Transform parent, string content, int size, Color color, float height)
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
            text.alignment = TextAnchor.MiddleCenter;
            text.raycastTarget = false;
            var l = t.AddComponent<LayoutElement>();
            l.preferredHeight = height;
            var trt = t.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, height);
            return text;
        }

        private Button AddButton(Transform parent, string label, int size, Color bg, float height)
        {
            var b = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(parent, false);
            var img = b.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var btn = b.GetComponent<Button>();
            btn.targetGraphic = img;
            var l = b.AddComponent<LayoutElement>();
            l.preferredHeight = height;
            l.flexibleWidth = 1f;

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(b.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.text = label;
            t.fontSize = size;
            t.color = Color.white;
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = Vector2.zero;
            lrt.offsetMax = Vector2.zero;
            return btn;
        }

        private void PlaceInFrontOfCamera()
        {
            Camera cam = Camera.main;
            Transform c = cam != null ? cam.transform : transform;
            Vector3 pos = c.position + c.forward * distanceFromCamera + c.up * verticalOffset;
            _canvas.transform.position = pos;
            _canvas.transform.rotation = Quaternion.LookRotation(pos - c.position, Vector3.up);
        }

        private void Play(string path)
        {
            var clip = Resources.Load<AudioClip>(path);
            if (clip == null) return;
            if (_src == null) _src = GetComponent<AudioSource>();
            if (_src != null) _src.PlayOneShot(clip, 0.7f);
        }
    }
}
