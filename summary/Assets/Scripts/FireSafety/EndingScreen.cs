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

        Canvas _canvas;
        CanvasGroup _group;
        Text _titleText;
        Text _bodyText;
        Text _restartLabel;

        private void Awake()
        {
            Instance = this;
            Resolve();
            BuildUI();
            _canvas.gameObject.SetActive(false);
        }

        private void Start()
        {
            Subscribe();
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
        }

        private void Subscribe()
        {
            if (GuidanceArrows.Instance != null)
            {
                GuidanceArrows.Instance.onArrivedAtLobby.RemoveListener(OnArrivedAtLobby);
                GuidanceArrows.Instance.onArrivedAtLobby.AddListener(OnArrivedAtLobby);
            }
            if (countdown != null)
            {
                countdown.onTimeExpired.RemoveListener(OnTimeExpired);
                countdown.onTimeExpired.AddListener(OnTimeExpired);
            }
        }

        private void Unsubscribe()
        {
            if (GuidanceArrows.Instance != null)
                GuidanceArrows.Instance.onArrivedAtLobby.RemoveListener(OnArrivedAtLobby);
            if (countdown != null) countdown.onTimeExpired.RemoveListener(OnTimeExpired);
        }

        private void OnArrivedAtLobby() => Show(true);
        private void OnTimeExpired() => Show(false);

        /// <summary>弹出结束页；success=true 为成功撤离，false 为时间到失败。</summary>
        public void Show(bool success)
        {
            if (IsOpen) return;
            IsSuccess = success;
            Resolve();
            if (_canvas == null) BuildUI();

            if (success && countdown != null) countdown.Pause();

            if (_titleText != null)
            {
                _titleText.text = success ? titleSuccess : titleFailure;
                _titleText.color = success
                    ? new Color(0.45f, 0.95f, 0.55f)
                    : new Color(1f, 0.45f, 0.36f);
            }
            if (_bodyText != null) _bodyText.text = BuildSummary(success);

            PlaceInFrontOfCamera();
            _canvas.gameObject.SetActive(true);
            if (Application.isPlaying && _group != null)
            {
                _group.alpha = 0f;
                UIFX.Fade(this, _group, 0f, 1f, 0.3f);
            }
            else if (_group != null) _group.alpha = 1f;

            IsOpen = true;
            Play(success ? "Sounds/SFX/quiz_correct" : "Sounds/SFX/fail_buzz", 0.7f);
            onShown?.Invoke();
            Debug.Log("[Ending] 结束页已弹出：" + (success ? "成功撤离" : "时间到失败"));
        }

        public void Hide()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
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

        private string BuildSummary(bool success)
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

            string head = success
                ? "你已完成全流程：拿起 → 拔销 → 压把 → 喷射扑灭 → 沿箭头撤离到大厅。"
                : "倒计时已耗尽，演练未完成。可重新演练再试一次。";

            return head + "\n\n" +
                   "总用时 " + Format(used) + " · 剩余 " + Format(remain) + "\n" +
                   quizLine + "\n" +
                   fireLine + "\n" +
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
            close.onClick.AddListener(Hide);

            MakeText(panel.transform, "手柄射线扣 Trigger 点按，桌面可用鼠标点击", 22,
                new Color(0.72f, 0.72f, 0.72f), 38f);
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
