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
        public StageTwoFlow stageTwo;

        [Header("文案")]
        public string title = "阶段一完成";
        public string buttonSettlement = "立刻结算";
        public string buttonContinue = "继续往下";

        [Header("新流程控制")]
        [Tooltip("全部火源扑灭时是否自动弹出；正式演练由 DrillScenario 接管阶段推进，阶段一不弹本面板")]
        public bool autoShow = true;
        [Tooltip("自定义汇总文案（留空使用默认文案）")]
        public string summaryOverride = "";
        [System.NonSerialized]
        [Tooltip("「继续往下」的替代回调（操作演练 → 进入正式演练）")]
        public System.Action continueOverride;

        [Header("位置")]
        [Tooltip("面板离相机的距离（米）；太近会顶满视野、按钮被桌面 HUD 压住，1.55 米更稳")]
        public float distanceFromCamera = 1.55f;
        public float verticalOffset = 0.08f;

        [Header("事件")]
        public UnityEvent onSettlement = new UnityEvent();
        public UnityEvent onContinue = new UnityEvent();

        public bool IsOpen { get; private set; }

        Canvas _canvas;
        CanvasGroup _group;
        Text _summaryText;
        Text _titleText;
        Text _settleLabel;
        Text _continueLabel;
        Button _settleButton;
        Button _continueButton;
        AudioSource _src;

        private void Awake()
        {
            Instance = this;
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            _src = GetComponent<AudioSource>();
            BuildUI();
            SubscribeManager();
        }

        private void Start()
        {
            SubscribeManager();
            var mgr = FireDrillManager.Instance;
            if (mgr != null && mgr.IsCompleted) Show();
        }

        private void SubscribeManager()
        {
            var mgr = FireDrillManager.Instance ?? FindObjectOfType<FireDrillManager>();
            if (mgr == null) return;
            mgr.onDrillCompleted.RemoveListener(OnDrillCompleted);
            mgr.onDrillCompleted.AddListener(OnDrillCompleted);
        }

        private void UnsubscribeManager()
        {
            var mgr = FireDrillManager.Instance ?? FindObjectOfType<FireDrillManager>();
            if (mgr != null) mgr.onDrillCompleted.RemoveListener(OnDrillCompleted);
        }

        private void OnDestroy()
        {
            UnsubscribeManager();
            if (Instance == this) Instance = null;
        }

        /// <summary>演练全部结束（⑨ 完成）：按 autoShow 决定是否弹出面板。</summary>
        private void OnDrillCompleted()
        {
            if (!autoShow) return;
            Show();
        }

        /// <summary>
        /// 修改面板文案（操作演练完成 → 进入正式演练）。
        /// 面板 UI 在 Awake 时就建好了，只改字段不会更新已有 Text，必须同步刷新引用。
        /// </summary>
        public void SetWording(string titleText, string summary, string continueLabel, string settlementLabel)
        {
            title = titleText;
            summaryOverride = summary;
            buttonContinue = continueLabel;
            buttonSettlement = settlementLabel;
            ApplyWording();
        }

        private void ApplyWording()
        {
            if (_titleText != null) _titleText.text = title;
            if (_continueLabel != null) _continueLabel.text = buttonContinue;
            if (_settleLabel != null) _settleLabel.text = buttonSettlement;
            if (_summaryText != null && !string.IsNullOrEmpty(summaryOverride))
                _summaryText.text = summaryOverride;
        }

        /// <summary>外部（DrillScenario）关闭面板。</summary>
        public void HidePanel()
        {
            Hide();
        }

        /// <summary>弹出面板：暂停倒计时。</summary>
        public void Show()
        {
            if (IsOpen) return;
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (countdown != null) countdown.Pause();
            if (_canvas == null) BuildUI();

            // 面板弹出的同一帧就把火焰彻底清掉：截图里「面板已弹、火还在烧」的问题根因在此
            FireSource.ClearAllFlames();

            RefreshSummary();
            ApplyWording();
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

        /// <summary>继续往下：操作演练走自定义回调；正式流程则倒计时继续并进入第二阶段。</summary>
        public void Continue()
        {
            if (continueOverride != null)
            {
                var cb = continueOverride;
                continueOverride = null;
                Hide();
                cb();
                onContinue?.Invoke();
                Debug.Log("[StageOne] 选择「" + buttonContinue + "」（自定义回调）。");
                return;
            }

            Hide();
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (countdown != null) countdown.Resume();
            // 第 2 阶段：烟雾 + 安全出口箭头 + 蹲姿潜行，交给 StageTwoFlow 编排
            if (stageTwo == null) stageTwo = FindObjectOfType<StageTwoFlow>();
            if (stageTwo != null) stageTwo.Begin();
            else if (GuidanceArrows.Instance != null) GuidanceArrows.Instance.BeginLobbyGuidance();
            onContinue?.Invoke();
            Debug.Log("[StageOne] 选择继续往下，倒计时继续，进入第二阶段（烟雾 + 安全出口箭头）。");
        }

        private void Hide()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        private void RefreshSummary()
        {
            if (_summaryText == null) return;
            if (!string.IsNullOrEmpty(summaryOverride))
            {
                _summaryText.text = summaryOverride;
                return;
            }
            string time = countdown != null
                ? "倒计时已暂停 · 剩余 " + Format(countdown.Remaining)
                : "倒计时已暂停";
            _summaryText.text =
                "火焰已扑灭，拿起 → 拔销 → 压把 → 喷射 的完整流程已完成。\n" + time + "。\n\n" +
                "请选择：立刻结算；或继续往下进入第二阶段——烟雾中蹲下潜行到大厅，" +
                "处理大厅复燃火源后走进「安全出口 EXIT」通关。";
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
            rt.sizeDelta = new Vector2(1000, 600);
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
            prt.sizeDelta = new Vector2(940, 540);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            _titleText = AddText(panel.transform, title, 46, new Color(0.45f, 0.95f, 0.55f), 66f);
            _summaryText = AddText(panel.transform, "", 26, Color.white, 230f);

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
            _settleButton = settleBtn;
            _settleLabel = settleBtn.GetComponentInChildren<Text>();

            var continueBtn = AddButton(btnRow.transform, buttonContinue, 34, new Color(0.12f, 0.55f, 0.30f, 1f), 84f);
            continueBtn.onClick.AddListener(Continue);
            _continueButton = continueBtn;
            _continueLabel = continueBtn.GetComponentInChildren<Text>();

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
