using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.SceneManagement;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 结束页（엔딩 화면）：整场演练的最终结果。
    ///   · 成功：走到大厅集合点（GuidanceArrows.onArrivedAtLobby）→ 暂停倒计时并给出总用时/剩余时间/答题成绩；
    ///   · 失败：倒计时耗尽（DrillCountdown.onTimeExpired）→ 给出未完成原因与已完成进度；
    ///   · 按钮：重新演练（重载场景）/ 关闭。
    /// 面板出现在相机前方 1.2 米并朝向玩家，鼠标与手柄射线都可点按。
    /// </summary>
    public class EndingScreen : MonoBehaviour
    {
        public static EndingScreen Instance { get; private set; }

        [Header("引用（留空自动查找）")]
        public DrillCountdown countdown;
        public ExtinguisherQuizPanel quiz;
        public ExitDoor exitDoor;

        [Header("位置")]
        public float distanceFromCamera = 1.2f;
        public float verticalOffset = 0f;

        [Header("文案")]
        public string titleSuccess = "演练完成 · 成功撤离";
        public string titleFailure = "演练结束 · 时间到";
        public string buttonRestart = "重新演练";
        public string buttonClose = "关闭";

        [Header("事件")]
        public UnityEvent onShown = new UnityEvent();
        public UnityEvent onRestarted = new UnityEvent();

        public bool IsOpen { get; private set; }
        public bool IsSuccess { get; private set; }
        /// <summary>第 37 轮：点「关闭」后整页收成右下角小卡片，点卡片重选情景/重开。</summary>
        public bool IsMinimized => _minimized;

        Canvas _canvas;
        CanvasGroup _group;
        Text _titleText;
        Text _bodyText;
        Text _restartLabel;

        Canvas _dockCanvas;
        Button _dockButton;
        Text _dockLabel;
        bool _minimized;

        private void Awake()
        {
            Instance = this;
            Resolve();
            BuildUI();
            _canvas.gameObject.SetActive(false);
            if (_dockCanvas != null) _dockCanvas.gameObject.SetActive(false);
        }

        private void Start()
        {
            Subscribe();
        }

        private void Update()
        {
            if (!_minimized || _dockCanvas == null) return;
            PlaceDockBottomRight();
        }

        private void OnDestroy()
        {
            Unsubscribe();
            if (Instance == this) Instance = null;
        }

        private void Resolve()
        {
            if (countdown == null) countdown = FindObjectOfType<DrillCountdown>();
            if (quiz == null) quiz = FindObjectOfType<ExtinguisherQuizPanel>();
            if (exitDoor == null) exitDoor = FindObjectOfType<ExitDoor>(true);
        }

        private void Subscribe()
        {
            // 成功条件已从「到达大厅」改为「穿过安全出口门」：大厅只是第二阶段的检查点
            if (exitDoor != null)
            {
                exitDoor.onEntered.RemoveListener(OnExitDoorEntered);
                exitDoor.onEntered.AddListener(OnExitDoorEntered);
            }
            if (countdown != null)
            {
                countdown.onTimeExpired.RemoveListener(OnTimeExpired);
                countdown.onTimeExpired.AddListener(OnTimeExpired);
            }
        }

        private void Unsubscribe()
        {
            if (exitDoor != null) exitDoor.onEntered.RemoveListener(OnExitDoorEntered);
            if (countdown != null) countdown.onTimeExpired.RemoveListener(OnTimeExpired);
        }

        private void OnExitDoorEntered()
        {
            if (exitDoor != null && !exitDoor.IsUnlocked) return;
            Show(true);
        }

        private void OnTimeExpired() => Show(false);

        /// <summary>弹出结束页；success=true 为成功撤离，false 为时间到失败。</summary>
        public void Show(bool success) => Show(success, null);

        /// <summary>第 38 轮：生命值耗尽等非时间原因的失败，用自定义原因替换「时间到」。</summary>
        public void Show(bool success, string failureReason)
        {
            if (IsOpen) return;
            IsSuccess = success;
            Resolve();
            if (_canvas == null) BuildUI();

            if (success && countdown != null) countdown.Pause();

            // 结束页弹出即清掉场景里所有明火（与阶段一完成面板一致）
            FireSource.ClearAllFlames();

            if (_titleText != null)
            {
                _titleText.text = success
                    ? titleSuccess
                    : (string.IsNullOrEmpty(failureReason) ? titleFailure : "演练失败 · " + failureReason);
                _titleText.color = success
                    ? new Color(0.45f, 0.95f, 0.55f)
                    : new Color(1f, 0.45f, 0.36f);
            }
            if (_bodyText != null) _bodyText.text = BuildSummary(success, failureReason);

            PlaceInFrontOfCamera();
            _canvas.gameObject.SetActive(true);
            if (Application.isPlaying && _group != null)
            {
                _group.alpha = 0f;
                UIFX.Fade(this, _group, 0f, 1f, 0.3f);
            }
            else if (_group != null) _group.alpha = 1f;

            IsOpen = true;
            _minimized = false;
            if (_dockCanvas != null) _dockCanvas.gameObject.SetActive(false);
            Play(success ? "Sounds/SFX/quiz_correct" : "Sounds/SFX/fail_buzz", 0.7f);
            onShown?.Invoke();
            Debug.Log("[Ending] 结束页已弹出：" + (success ? "成功撤离" : "时间到失败"));
        }

        public void Hide()
        {
            IsOpen = false;
            _minimized = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (_dockCanvas != null) _dockCanvas.gameObject.SetActive(false);
        }

        /// <summary>
        /// 第 37 轮：点「关闭」不再整页消失，而是收成屏幕右下方的小卡片；
        /// 点小卡片重新加载场景，回到开始界面的三情景选择。
        /// </summary>
        public void Minimize()
        {
            Play("Sounds/SFX/ui_click", 0.5f);
            IsOpen = false;
            _minimized = true;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (_dockCanvas != null)
            {
                _dockCanvas.gameObject.SetActive(true);
                PlaceDockBottomRight();
            }
            Debug.Log("[Ending] 结束页已缩到右下角「重新演练 / 选择情景」入口");
        }

        /// <summary>重新演练：重载当前场景，回到初始状态。</summary>
        public void Restart()
        {
            Play("Sounds/SFX/ui_click", 0.7f);
            onRestarted?.Invoke();
            var scene = SceneManager.GetActiveScene();
            if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
            else SceneManager.LoadScene(scene.name);
        }

        // ---------------- 成绩 ----------------

        private string BuildSummary(bool success, string failureReason = null)
        {
            float total = countdown != null ? countdown.durationSeconds : 180f;
            float remain = countdown != null ? Mathf.Max(0f, countdown.Remaining) : 0f;
            float used = Mathf.Max(0f, total - remain);

            string quizLine;
            Resolve();
            if (quiz == null)
            {
                quiz = FindObjectOfType<ExtinguisherQuizPanel>();
                if (quiz == null)
                    quiz = GetComponentInChildren<ExtinguisherQuizPanel>(true);
            }
            if (quiz != null && quiz.QuestionCount > 0)
            {
                quizLine = "常识考核：答对 " + quiz.CorrectCount + " / " + quiz.QuestionCount + " 题（"
                           + (quiz.Passed ? "通过" : "未通过") + "，及格线 " + quiz.PassCorrectCount + " 题）";
            }
            else quizLine = "常识考核：未参与";

            int fireTotal = 0, fireDone = 0;
            foreach (var f in FindObjectsByType<FireSource>(FindObjectsSortMode.None))
            {
                fireTotal++;
                if (f.IsExtinguished) fireDone++;
            }
            string fireLine = "火源处理：" + fireDone + " / " + fireTotal + " 处已扑灭";

            var mgr = FireDrillManager.Instance;
            string stepLine = "完成步骤：" + (mgr != null
                ? FireDrillManager.StepNames[(int)mgr.CurrentStep]
                : "未知");

            var vitals = PlayerVitals.Instance != null
                ? PlayerVitals.Instance
                : FindObjectOfType<PlayerVitals>();
            string vitalLine = vitals != null
                ? "生命值：" + vitals.currentHp + " / " + vitals.maxHp
                  + (vitals.IsDead ? "（耗尽）" : "")
                : "生命值：未接入";

            string head = success
                ? "你已完成全流程：灭火 → 蹲姿穿过烟雾撤离大厅 → 处理大厅复燃 → 走进安全出口。"
                : (string.IsNullOrEmpty(failureReason)
                    ? "倒计时已耗尽，演练未完成。可重新演练再试一次。"
                    : failureReason + "，演练未完成。可重新演练再试一次。");

            return head + "\n\n" +
                   "总用时 " + Format(used) + " · 剩余 " + Format(remain) + "\n" +
                   quizLine + "\n" +
                   fireLine + "\n" +
                   vitalLine + "\n" +
                   stepLine + "\n\n" +
                   (success
                       ? "如需再练，点「重新演练」；点「关闭」可留在场景内自由观察。"
                       : "失败不影响已获得的经验，点「重新演练」重新开始。");
        }

        private static string Format(float seconds)
        {
            int s = Mathf.CeilToInt(Mathf.Max(0f, seconds));
            return (s / 60).ToString("00") + ":" + (s % 60).ToString("00");
        }

        // ---------------- UI ----------------

        private void BuildUI()
        {
            var go = new GameObject("Canvas_Ending");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 320;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 740);
            rt.localScale = Vector3.one * 0.0009f;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup));
            panel.transform.SetParent(go.transform, false);
            panel.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 0.96f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 12f;
            layout.padding = new RectOffset(30, 30, 28, 28);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(940, 700);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            _titleText = MakeText(panel.transform, titleSuccess, 46, new Color(0.45f, 0.95f, 0.55f), 70f);
            _bodyText = MakeText(panel.transform, "", 27, Color.white, 360f);
            _bodyText.alignment = TextAnchor.UpperLeft;

            var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 24f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = false;
            var hrt = row.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(880, 92);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 92f;

            var restart = MakeButton(row.transform, buttonRestart, 34, new Color(0.12f, 0.55f, 0.30f, 1f), 86f);
            _restartLabel = restart.GetComponentInChildren<Text>();
            restart.onClick.AddListener(Restart);
            var close = MakeButton(row.transform, buttonClose, 34, new Color(0.28f, 0.32f, 0.4f, 1f), 86f);
            close.onClick.AddListener(Minimize);

            MakeText(panel.transform, "手柄射线扣 Trigger 点按，桌面可用鼠标点击", 22,
                new Color(0.72f, 0.72f, 0.72f), 38f);

            BuildDock();
        }

        /// <summary>右下角常驻小卡片：整页关闭后出现，点一下重新演练并回到情景选择。</summary>
        private void BuildDock()
        {
            var go = new GameObject("Canvas_EndingDock");
            go.transform.SetParent(transform, false);
            _dockCanvas = go.AddComponent<Canvas>();
            _dockCanvas.renderMode = RenderMode.WorldSpace;
            _dockCanvas.sortingOrder = 321;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(360, 110);
            rt.localScale = Vector3.one * 0.0009f;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var b = new GameObject("Btn_RestartDock", typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(go.transform, false);
            var img = b.GetComponent<Image>();
            img.color = new Color(0.10f, 0.46f, 0.30f, 0.96f);
            img.raycastTarget = true;
            var brt = b.GetComponent<RectTransform>();
            brt.anchorMin = brt.anchorMax = new Vector2(0.5f, 0.5f);
            brt.pivot = new Vector2(0.5f, 0.5f);
            brt.sizeDelta = new Vector2(340, 92);
            _dockButton = b.GetComponent<Button>();
            _dockButton.targetGraphic = img;
            _dockButton.onClick.AddListener(Restart);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(b.transform, false);
            _dockLabel = labelGo.GetComponent<Text>();
            _dockLabel.text = DrillLocale.T("重新演练\n选择情景", "RESTART\nChoose scenario");
            _dockLabel.fontSize = 25;
            _dockLabel.color = Color.white;
            _dockLabel.font = UIFont.Resolve();
            if (_dockLabel.font != null) _dockLabel.material = _dockLabel.font.material;
            _dockLabel.alignment = TextAnchor.MiddleCenter;
            _dockLabel.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(8f, 4f);
            lrt.offsetMax = new Vector2(-8f, -4f);

            b.AddComponent<ButtonFeedback>().Init(img.color);
            go.SetActive(false);
        }

        /// <summary>把右下角小卡片放在相机前下方偏右，转头也保持在视野右下角。</summary>
        private void PlaceDockBottomRight()
        {
            Camera cam = Camera.main;
            Transform c = cam != null ? cam.transform : transform;
            Vector3 pos = c.position + c.forward * 0.82f + c.right * 0.28f + c.up * -0.19f;
            _dockCanvas.transform.position = pos;
            _dockCanvas.transform.rotation = Quaternion.LookRotation(pos - c.position, c.up);
        }

        private Text MakeText(Transform parent, string content, int size, Color color, float height)
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

        private Button MakeButton(Transform parent, string label, int size, Color bg, float height)
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
            // WorldSpace Canvas 的正面朝向 -Z：要让面板面向玩家，+Z 必须背离相机（pos - c.position）。
            // 写成 c.position - pos 会把背面转给玩家（文字镜像、按钮点不到）。
            _canvas.transform.rotation = Quaternion.LookRotation(pos - c.position, Vector3.up);
        }

        private void Play(string path, float volume)
        {
            var clip = Resources.Load<AudioClip>(path);
            if (clip != null && Application.isPlaying)
                AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }
}
