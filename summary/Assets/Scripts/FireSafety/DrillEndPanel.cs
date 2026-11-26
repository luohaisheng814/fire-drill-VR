using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 42 轮：第 ⑦ 步的结束状态面板——「到达安全地点并报告」。
    /// 与 EndingScreen（倒计时到点 / 生命值耗尽的失败页）分开：
    /// 这里不会清掉场景里仍在燃烧的火（玩家可能是「不灭火直接疏散」），
    /// 只展示报告结论与行为回顾（警报、疏散判断、灭火操作、路线选择），供后续训练复盘。
    /// </summary>
    public class DrillEndPanel : MonoBehaviour
    {
        public static DrillEndPanel Instance { get; private set; }

        [Header("文案")]
        public string titleCn = "演练完成 · 到达安全地点并报告";
        public string titleEn = "DRILL COMPLETE · REPORTED AT ASSEMBLY";
        public string titleKo = "훈련 완료 · 집결지 보고";
        public string hintCn = "行为回顾（可用于后续训练）：";
        public string hintEn = "Behaviour review (for follow-up training):";
        public string hintKo = "행동 리뷰(후속 훈련용):";
        public string retryCn = "重新演练";
        public string retryEn = "RESTART";
        public string retryKo = "다시 훈련";
        public string closeCn = "留在集合点";
        public string closeEn = "STAY AT ASSEMBLY";
        public string closeKo = "집결지 대기";

        [Header("面板")]
        public float distanceFromCamera = 1.50f;
        public float verticalOffset = -0.05f;
        public float panelScale = 0.00095f;

        public bool IsOpen { get; private set; }
        public string LastRecord { get; private set; } = "";

        Canvas _canvas;
        Text _title, _body, _foot;
        Button _retryBtn, _closeBtn;

        void Awake() { Instance = this; }
        void OnDestroy() { if (Instance == this) Instance = null; }

        public void Show(string record)
        {
            LastRecord = record ?? "";
            IsOpen = true;
            if (_canvas == null) Build();
            else _canvas.gameObject.SetActive(true);
            Refresh();
            Debug.Log("[End] 结束面板已弹出（到达集合点并报告）");
        }

        public void Hide()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        /// <summary>探针用：把面板摆到相机正前方（世界空间面板换机位后会停在旧位置）。</summary>
        public void ForcePlace()
        {
            if (_canvas == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            _canvas.transform.position = cam.transform.position
                                        + cam.transform.forward * distanceFromCamera
                                        + Vector3.up * verticalOffset;
            _canvas.transform.rotation = cam.transform.rotation;
        }

        void LateUpdate()
        {
            if (!IsOpen) return;
            ForcePlace();
        }

        void Refresh()
        {
            if (_title != null) _title.text = DrillLocale.T(titleCn, titleEn, titleKo);
            if (_body != null) _body.text = LastRecord;
            if (_foot != null)
                _foot.text = DrillLocale.T(hintCn, hintEn, hintKo) + "\n"
                           + DrillLocale.T("提示：绕行路线与向 119 求助的判断，将在专家审查后补充。",
                                           "Note: detour routes and 119 rescue escalation will follow expert review.",
                                           "참고: 우회 경로와 119 구조 요청 판단은 전문가 검토 후 보완됩니다.");
            SetButtonLabel(_retryBtn, DrillLocale.T(retryCn, retryEn, retryKo));
            SetButtonLabel(_closeBtn, DrillLocale.T(closeCn, closeEn, closeKo));
        }

        static void SetButtonLabel(Button b, string label)
        {
            if (b == null) return;
            var t = b.GetComponentInChildren<Text>();
            if (t != null) t.text = label;
        }

        void Build()
        {
            var go = new GameObject("DrillEndCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            go.transform.SetParent(transform, false);
            _canvas = go.GetComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 460;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 820);
            rt.localScale = new Vector3(panelScale, panelScale, panelScale);

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.03f, 0.07f, 0.06f, 0.995f);
            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(1000, 820);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;

            _title = MakeText(panel.transform, "", 42, new Color(0.52f, 0.98f, 0.62f),
                              new Vector2(0, -22), new Vector2(930, 60));
            _body = MakeText(panel.transform, "", 25, new Color(0.93f, 0.96f, 1f),
                             new Vector2(0, -96), new Vector2(910, 300), TextAnchor.UpperLeft);
            _foot = MakeText(panel.transform, "", 20, new Color(0.78f, 0.86f, 1f),
                             new Vector2(0, -410), new Vector2(910, 110), TextAnchor.UpperLeft);

            _retryBtn = MakeButton(panel.transform, "", new Vector2(-250, -560), new Vector2(430, 84),
                                   new Color(0.12f, 0.36f, 0.22f));
            _retryBtn.onClick.AddListener(() =>
            {
                var scene = SceneManager.GetActiveScene();
                if (scene.buildIndex >= 0) SceneManager.LoadScene(scene.buildIndex);
                else SceneManager.LoadScene(scene.name);
            });

            _closeBtn = MakeButton(panel.transform, "", new Vector2(250, -560), new Vector2(430, 84),
                                   new Color(0.16f, 0.22f, 0.34f));
            _closeBtn.onClick.AddListener(Hide);
        }

        Text MakeText(Transform parent, string content, int size, Color color,
                      Vector2 anchored, Vector2 sizeDelta,
                      TextAnchor anchor = TextAnchor.MiddleCenter)
        {
            var go = new GameObject("Text", typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var t = go.GetComponent<Text>();
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.text = content;
            t.fontSize = size;
            t.color = color;
            t.alignment = anchor;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.verticalOverflow = VerticalWrapMode.Overflow;
            t.raycastTarget = false;
            t.lineSpacing = 1.2f;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchored;
            return t;
        }

        Button MakeButton(Transform parent, string label, Vector2 anchored, Vector2 sizeDelta, Color bg)
        {
            var go = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = bg;
            var btn = go.GetComponent<Button>();
            btn.targetGraphic = img;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchored;

            var textGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            textGo.transform.SetParent(go.transform, false);
            var t = textGo.GetComponent<Text>();
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.text = label;
            t.fontSize = 26;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = Vector2.zero;
            trt.offsetMax = Vector2.zero;
            return btn;
        }
    }
}
