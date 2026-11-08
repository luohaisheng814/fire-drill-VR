using System.Collections;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 灭火器常识问答面板（默认 5 题，每题 A/B/C 三个选项）。
    ///   · 每题限时 10 秒，超时未作答计为答错；
    ///   · 答对比例达到 passRatio（默认 60%，可改 75%）才算通过，通过后才能拿起 / 拔销 / 喷射；
    ///   · 未通过时面板关闭并广播 onFailed，可再次右键单击灭火器重新答题。
    /// 面板固定出现在玩家前方约 1.2 米处、按米制尺寸渲染，不会随灭火器模型一起放大糊住镜头。
    /// </summary>
    public class ExtinguisherQuizPanel : MonoBehaviour
    {
        [System.Serializable]
        public class QuizQuestion
        {
            [TextArea(2, 4)] public string question;
            [Tooltip("3 个选项，第 correctIndex 个为正确答案（0 起，对应 A/B/C）")]
            public string[] options = new string[3];
            public int correctIndex;
            [TextArea(1, 3)] public string explanation;
        }

        [Header("题目（默认 5 题，可增删）")]
        public List<QuizQuestion> questions = new List<QuizQuestion>();

        [Header("限时与通过条件")]
        [Tooltip("每题作答限时（秒），超时计为答错")]
        public float timePerQuestion = 10f;
        [Tooltip("通过所需答对比例：0.6 = 60%（3/5），也可设为 0.75")]
        [Range(0.3f, 1f)] public float passRatio = 0.6f;

        [Header("面板位置（米制，跟随相机的固定距离）")]
        [Tooltip("面板出现在相机前方多少米（1.15 米是一臂外最佳阅读距离，按钮也不挡手）")]
        public float distanceFromCamera = 1.15f;
        [Tooltip("面板相对视线高度的上下偏移（米）：略低于视线，读题不用仰头")]
        public float verticalOffset = -0.05f;
        [Tooltip("面板打开时若玩家走开，自动重新放到正前方（避免走到墙角还在看远处的题）")]
        public bool followCamera = true;

        [Header("行为")]
        [Tooltip("答错时显示正确答案与讲解，随后进入下一题")]
        public bool teachOnWrong = true;
        [Tooltip("每道题作答后的停留秒数")]
        public float advanceDelay = 1.3f;

        [Header("事件")]
        public UnityEvent onPassed = new UnityEvent();
        public UnityEvent onFailed = new UnityEvent();
        public UnityEvent<int> onCorrectChanged = new UnityEvent<int>();

        public bool IsOpen { get; private set; }
        public bool Passed { get; private set; }
        public bool Failed { get; private set; }
        public int CorrectCount { get; private set; }
        public int QuestionIndex => _index;
        public int QuestionCount => questions != null ? questions.Count : 0;
        public int OptionCount => 3;
        public float TimeLeft => Mathf.Max(0f, _timeLeft);
        public bool IsAnsweredThisQuestion => _answered;
        public int PassCorrectCount
        {
            get
            {
                int n = QuestionCount;
                if (n <= 0) return 0;
                return Mathf.CeilToInt(Mathf.Clamp01(passRatio) * n - 0.0001f);
            }
        }

        Canvas _canvas;
        GameObject _panelRoot;
        CanvasGroup _panelGroup;
        Text _progressText;
        Text _timerText;
        Image _timerFill;
        Text _questionText;
        Text _feedbackText;
        List<Button> _optionButtons = new List<Button>();
        List<Text> _optionLabels = new List<Text>();
        int _index;
        float _timeLeft;
        bool _answered;
        float _advanceTimer;
        bool _finished;
        float _closeTimer;
        AudioSource _src;
        Vector3 _placeCamPos;
        Vector3 _placeCamFwd = Vector3.forward;

        // ---------------- 题库 ----------------

        [Header("题库组（共 10 题，两台灭火器各 5 题，互不重复）")]
        [Tooltip("0 = A 组（Extinguisher01），1 = B 组（Extinguisher02）")]
        [Range(0, 1)] public int questionSetIndex = 0;

        /// <summary>
        /// 题库池，共 10 道：0~4 为 A 组（干粉灭火器基础），5~9 为 B 组（火灾处置进阶）。
        /// 两台的题面完全不重复，正确答案也分散在 A/B/C 三个位置。
        /// </summary>
        public static List<QuizQuestion> BuildAllQuestions()
        {
            return new List<QuizQuestion>
            {
                // ---------- A 组（Extinguisher01） ----------
                new QuizQuestion
                {
                    question = "手提式干粉灭火器的有效喷射距离大约是多少？",
                    options = new[] { "0.5 米以内", "2~3 米", "10 米以上" },
                    correctIndex = 1,
                    explanation = "干粉灭火器有效射程约 2~3 米，太远打不到火焰根部，太近容易被高温灼伤。"
                },
                new QuizQuestion
                {
                    question = "使用灭火器灭火时，正确的站位是？",
                    options = new[] { "下风方向", "紧贴火焰正前方", "上风或侧上风方向" },
                    correctIndex = 2,
                    explanation = "站在上风或侧上风方向，火焰与高温烟气才不会扑向自己。"
                },
                new QuizQuestion
                {
                    question = "拔出保险销后，压把应该怎么操作？",
                    options = new[] { "持续压住压把", "快速反复点按", "按一下就立刻松开" },
                    correctIndex = 0,
                    explanation = "压把要持续压住，灭火剂才会连续喷出；断续点按会导致喷射时有时无。"
                },
                new QuizQuestion
                {
                    question = "火场浓烟中撤离，正确的做法是？",
                    options = new[] { "直立快跑冲出烟雾", "乘电梯快速下楼", "湿毛巾捂口鼻、弯腰低姿沿墙撤离" },
                    correctIndex = 2,
                    explanation = "烟气向上聚集，低姿撤离能避开最浓的烟层；火场严禁乘电梯。"
                },
                new QuizQuestion
                {
                    question = "扑救带电设备火灾前，首先应该做什么？",
                    options = new[] { "直接对着设备喷射", "先切断电源", "打开全部门窗通风" },
                    correctIndex = 1,
                    explanation = "带电灭火有触电风险，必须先切断电源，再使用干粉灭火器扑救。"
                },

                // ---------- B 组（Extinguisher02） ----------
                new QuizQuestion
                {
                    question = "灭火器压力表的指针指在哪个区域说明压力正常？",
                    options = new[] { "绿色区域", "红色区域", "黄色区域" },
                    correctIndex = 0,
                    explanation = "指针在绿区表示压力正常；红区压力不足，黄区压力偏高，都需要报修或更换。"
                },
                new QuizQuestion
                {
                    question = "使用灭火器喷射时，喷嘴应该对准哪里？",
                    options = new[] { "火焰的最上方", "火焰的中部", "火焰的根部" },
                    correctIndex = 2,
                    explanation = "对准火焰根部左右扫射才能切断燃烧，对着火苗顶部喷几乎没有效果。"
                },
                new QuizQuestion
                {
                    question = "办公场所的灭火器至少应该多久检查一次？",
                    options = new[] { "每年一次", "每季度一次", "每月一次" },
                    correctIndex = 2,
                    explanation = "灭火器应每月检查压力表、瓶身和喷管，发现问题立即报修。"
                },
                new QuizQuestion
                {
                    question = "发现初起火灾时，第一件该做的事是什么？",
                    options = new[] { "先拍照记录现场", "立即拨打 119 报警并组织人员疏散", "先把电脑关机再撤离" },
                    correctIndex = 1,
                    explanation = "先报警、先疏散，再考虑扑救；初期处置的每几十秒都影响逃生结果。"
                },
                new QuizQuestion
                {
                    question = "灭火器使用口诀「提、拔、握、压」中的「握」指的是？",
                    options = new[] { "握住压把准备按下", "握住喷管、对准火焰根部", "双手握住瓶身" },
                    correctIndex = 1,
                    explanation = "「握」是握住喷管并瞄准火焰根部，「压」才是压下压把喷射。"
                }
            };
        }

        /// <summary>按组取 5 题：0 = A 组，1 = B 组。</summary>
        public static List<QuizQuestion> BuildQuestionSet(int setIndex)
        {
            var all = BuildAllQuestions();
            int start = Mathf.Clamp(setIndex, 0, 1) * 5;
            var set = new List<QuizQuestion>();
            for (int i = 0; i < 5; i++) set.Add(all[start + i]);
            return set;
        }

        /// <summary>兼容旧调用：返回 A 组 5 题。</summary>
        public static List<QuizQuestion> BuildDefaultQuestions() => BuildQuestionSet(0);

        /// <summary>让装配脚本把题库刷成当前组（A 组或 B 组）的 5 题。</summary>
        public void ResetToDefaultQuestions()
        {
            questions = BuildQuestionSet(questionSetIndex);
        }

        void Awake()
        {
            if (questions == null || questions.Count == 0) questions = BuildQuestionSet(questionSetIndex);
            BuildUI();
            _src = GetComponent<AudioSource>();
        }

        void Update()
        {
            if (!IsOpen) return;
            KeepInFrontOfCamera();
            AdvanceTime(Time.deltaTime);
        }

        /// <summary>玩家走出原位 / 转头过多时，把面板重新放回正前方（否则题目跑到视野外）。</summary>
        void KeepInFrontOfCamera()
        {
            if (!followCamera || _canvas == null) return;
            Camera cam = Camera.main;
            if (cam == null) return;
            if (Vector3.Distance(cam.transform.position, _placeCamPos) > 0.5f
                || Vector3.Dot(cam.transform.forward, _placeCamFwd) < 0.8f)
            {
                PlaceInFrontOfCamera();
            }
        }

        /// <summary>推进问答计时（Update 调用；自检里可用固定步长直接驱动）。</summary>
        public void AdvanceTime(float dt)
        {
            if (!IsOpen || dt <= 0f) return;

            // 已结束：停留一会儿自动收起面板
            if (_finished)
            {
                _closeTimer -= dt;
                if (_closeTimer <= 0f) Close();
                return;
            }

            // 已作答：等待进入下一题
            if (_answered)
            {
                _advanceTimer -= dt;
                if (_advanceTimer <= 0f)
                {
                    if (_index + 1 < questions.Count) ShowQuestion(_index + 1);
                    else Finish();
                }
                return;
            }

            if (questions == null || _index >= questions.Count) return;
            _timeLeft -= dt;
            RefreshTimer();
            if (_timeLeft <= 0f)
            {
                _timeLeft = 0f;
                OnTimeOut();
            }
        }

        // ---------------- 对外接口 ----------------

        /// <summary>在玩家相机前方打开问答面板（anchor 参数保留兼容，不再按灭火器缩放）。</summary>
        public void Open(Transform anchor = null)
        {
            if (Passed || IsOpen) return;
            if (questions == null || questions.Count == 0)
            {
                Debug.LogWarning("[Quiz] 没有配置题目，直接视为通过。");
                Passed = true;
                onPassed?.Invoke();
                return;
            }

            if (_canvas == null) BuildUI();
            PlaceInFrontOfCamera();
            _canvas.gameObject.SetActive(true);
            _panelRoot.SetActive(true);
            if (Application.isPlaying)
            {
                _panelGroup.alpha = 0f;
                UIFX.Fade(this, _panelGroup, 0f, 1f, 0.25f);
                UIFX.ScaleIn(this, _panelRoot.transform, 0.9f, 1f, 0.28f, 1.05f);
            }
            else _panelGroup.alpha = 1f;

            _index = 0;
            CorrectCount = 0;
            Failed = false;
            _answered = false;
            _advanceTimer = 0f;
            _finished = false;
            _closeTimer = 0f;
            IsOpen = true;
            ShowQuestion(0);
        }

        public void Close()
        {
            IsOpen = false;
            _finished = false;
            _answered = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        public void ResetQuiz()
        {
            Passed = false;
            Failed = false;
            IsOpen = false;
            CorrectCount = 0;
            _index = 0;
            _answered = false;
            _advanceTimer = 0f;
            _finished = false;
            _closeTimer = 0f;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        /// <summary>键盘 / 鼠标作答（0 起，对应 A/B/C）。仅当面板打开且该选项可点时生效。</summary>
        public bool AnswerOption(int optionIndex)
        {
            if (!IsOpen || _answered) return false;
            if (_optionButtons == null || optionIndex < 0 || optionIndex >= _optionButtons.Count) return false;

            var btn = _optionButtons[optionIndex];
            if (btn == null || !btn.gameObject.activeSelf || !btn.interactable) return false;

            OnOptionClicked(optionIndex);
            return true;
        }

        // ---------------- UI ----------------

        void BuildUI()
        {
            var go = new GameObject("Canvas_Quiz");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 250;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 780);
            rt.localScale = Vector3.one * 0.0009f;      // 1000 单位 ≈ 0.9 米宽
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();

            var panel = new GameObject("QuizPanel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup), typeof(CanvasGroup));
            panel.transform.SetParent(go.transform, false);
            var img = panel.GetComponent<Image>();
            img.color = new Color(0.05f, 0.07f, 0.12f, 0.95f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(26, 26, 24, 24);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            var prt = panel.GetComponent<RectTransform>();
            prt.sizeDelta = new Vector2(940, 720);
            prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
            prt.pivot = new Vector2(0.5f, 0.5f);
            prt.anchoredPosition = Vector2.zero;
            _panelGroup = panel.GetComponent<CanvasGroup>();
            _panelRoot = panel;

            AddText(panel.transform, "灭火器常识问答", 42, new Color(1f, 0.85f, 0.2f), 62f);

            var infoRow = new GameObject("InfoRow", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            infoRow.transform.SetParent(panel.transform, false);
            var hl = infoRow.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 12f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = false;
            var hrt = infoRow.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(880, 40);
            var le0 = infoRow.AddComponent<LayoutElement>();
            le0.preferredHeight = 40f;

            _progressText = AddTextTo(infoRow.transform, "", 28, new Color(0.6f, 0.8f, 1f), 40f);
            _timerText = AddTextTo(infoRow.transform, "", 28, new Color(1f, 0.9f, 0.4f), 40f);

            // 计时条
            var barBg = new GameObject("TimerBar", typeof(RectTransform), typeof(Image));
            barBg.transform.SetParent(panel.transform, false);
            barBg.GetComponent<Image>().color = new Color(0.18f, 0.2f, 0.26f, 1f);
            barBg.GetComponent<Image>().raycastTarget = false;
            var bgr = barBg.GetComponent<RectTransform>();
            bgr.sizeDelta = new Vector2(880, 10);
            var bgle = barBg.AddComponent<LayoutElement>();
            bgle.preferredHeight = 10f;

            var fillGo = new GameObject("Fill", typeof(RectTransform), typeof(Image));
            fillGo.transform.SetParent(barBg.transform, false);
            _timerFill = fillGo.GetComponent<Image>();
            _timerFill.color = new Color(0.25f, 0.75f, 0.95f, 1f);
            _timerFill.type = Image.Type.Filled;
            _timerFill.fillMethod = Image.FillMethod.Horizontal;
            _timerFill.fillOrigin = (int)Image.OriginHorizontal.Left;
            _timerFill.raycastTarget = false;
            var frt = fillGo.GetComponent<RectTransform>();
            frt.anchorMin = Vector2.zero;
            frt.anchorMax = Vector2.one;
            frt.offsetMin = Vector2.zero;
            frt.offsetMax = Vector2.zero;

            _questionText = AddText(panel.transform, "", 32, Color.white, 88f);

            var optionsHost = new GameObject("Options", typeof(RectTransform), typeof(VerticalLayoutGroup));
            optionsHost.transform.SetParent(panel.transform, false);
            var ol = optionsHost.GetComponent<VerticalLayoutGroup>();
            ol.spacing = 8f;
            ol.padding = new RectOffset(10, 10, 2, 2);
            var ort = optionsHost.GetComponent<RectTransform>();
            ort.sizeDelta = new Vector2(880, 232);
            _optionButtons.Clear();
            _optionLabels.Clear();
            for (int i = 0; i < 3; i++)
            {
                int idx = i;
                var b = AddButton(optionsHost.transform, "", 30, new Color(0.16f, 0.38f, 0.62f, 1f), 64f);
                _optionButtons.Add(b);
                _optionLabels.Add(b.GetComponentInChildren<Text>());
                b.onClick.AddListener(() => OnOptionClicked(idx));
            }

            _feedbackText = AddText(panel.transform, "", 26, Color.cyan, 64f);
            AddText(panel.transform,
                "每题 10 秒 · 超时计为答错 · 答对 " + PassCorrectCount + " / " + QuestionCount + " 题即可拿起灭火器",
                20, new Color(0.72f, 0.72f, 0.72f), 34f);

            go.SetActive(false);
        }

        Text AddText(Transform parent, string content, int size, Color color, float height)
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
            var le = t.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var trt = t.GetComponent<RectTransform>();
            trt.sizeDelta = new Vector2(trt.sizeDelta.x, height);
            return text;
        }

        Text AddTextTo(Transform parent, string content, int size, Color color, float height)
        {
            var text = AddText(parent, content, size, color, height);
            var le = text.GetComponent<LayoutElement>();
            if (le != null) le.flexibleWidth = 1f;
            return text;
        }

        Button AddButton(Transform parent, string label, int size, Color bg, float height)
        {
            var b = new GameObject("Btn", typeof(RectTransform), typeof(Image), typeof(Button));
            b.transform.SetParent(parent, false);
            var img = b.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
            var btn = b.GetComponent<Button>();
            btn.targetGraphic = img;
            var le = b.AddComponent<LayoutElement>();
            le.preferredHeight = height;
            var brt = b.GetComponent<RectTransform>();
            brt.sizeDelta = new Vector2(brt.sizeDelta.x, height);

            var labelGo = new GameObject("Label", typeof(RectTransform), typeof(Text));
            labelGo.transform.SetParent(b.transform, false);
            var t = labelGo.GetComponent<Text>();
            t.fontSize = size;
            t.color = Color.white;
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.alignment = TextAnchor.MiddleLeft;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false;
            var lrt = labelGo.GetComponent<RectTransform>();
            lrt.anchorMin = Vector2.zero;
            lrt.anchorMax = Vector2.one;
            lrt.offsetMin = new Vector2(16f, 0f);
            lrt.offsetMax = new Vector2(-8f, 0f);
            return btn;
        }

        /// <summary>把面板放到相机正前方固定距离处，并面向玩家。</summary>
        void PlaceInFrontOfCamera()
        {
            Camera cam = Camera.main;
            Transform c = cam != null ? cam.transform : transform;
            Vector3 pos = c.position + c.forward * distanceFromCamera + c.up * verticalOffset;
            _canvas.transform.position = pos;
            _canvas.transform.rotation = Quaternion.LookRotation(pos - c.position, Vector3.up);
            _placeCamPos = c.position;
            _placeCamFwd = c.forward;
        }

        // ---------------- 答题流程 ----------------

        void ShowQuestion(int index)
        {
            if (index >= questions.Count) { Finish(); return; }

            var q = questions[index];
            _index = index;
            _answered = false;
            _timeLeft = Mathf.Max(1f, timePerQuestion);
            _progressText.text = "第 " + (index + 1) + " / " + questions.Count + " 题";
            _questionText.text = q.question;
            _questionText.color = Color.white;
            _feedbackText.text = "";
            RefreshTimer();

            for (int i = 0; i < _optionButtons.Count; i++)
            {
                bool valid = q.options != null && i < q.options.Length && !string.IsNullOrEmpty(q.options[i]);
                _optionButtons[i].gameObject.SetActive(valid);
                if (valid) _optionLabels[i].text = OptionLetter(i) + ". " + q.options[i];
                _optionButtons[i].interactable = true;
                _optionButtons[i].image.color = new Color(0.16f, 0.38f, 0.62f, 1f);
            }
        }

        void RefreshTimer()
        {
            if (_timerText != null)
                _timerText.text = "剩余 " + Mathf.CeilToInt(Mathf.Max(0f, _timeLeft)) + " 秒";

            if (_timerFill != null)
            {
                float k = timePerQuestion > 0f ? Mathf.Clamp01(_timeLeft / timePerQuestion) : 0f;
                _timerFill.fillAmount = k;
                _timerFill.color = k > 0.5f
                    ? new Color(0.25f, 0.75f, 0.95f, 1f)
                    : (k > 0.25f ? new Color(1f, 0.75f, 0.25f, 1f) : new Color(0.9f, 0.25f, 0.25f, 1f));
            }
        }

        static string OptionLetter(int i) => i == 0 ? "A" : (i == 1 ? "B" : (i == 2 ? "C" : "D"));

        void OnOptionClicked(int optionIndex)
        {
            if (!IsOpen || _answered) return;
            if (_index >= questions.Count) return;

            var q = questions[_index];
            _answered = true;

            for (int i = 0; i < _optionButtons.Count; i++) _optionButtons[i].interactable = false;

            if (optionIndex == q.correctIndex)
            {
                CorrectCount++;
                onCorrectChanged?.Invoke(CorrectCount);
                _optionButtons[optionIndex].image.color = new Color(0.15f, 0.65f, 0.30f, 1f);
                _feedbackText.text = "回答正确！" + (string.IsNullOrEmpty(q.explanation) ? "" : q.explanation);
                _feedbackText.color = Color.green;
                Play("Sounds/SFX/quiz_correct");
            }
            else
            {
                _optionButtons[optionIndex].image.color = new Color(0.75f, 0.2f, 0.2f, 1f);
                if (q.correctIndex >= 0 && q.correctIndex < _optionButtons.Count)
                    _optionButtons[q.correctIndex].image.color = new Color(0.15f, 0.65f, 0.30f, 1f);
                _feedbackText.text = "回答错误。" + (teachOnWrong && !string.IsNullOrEmpty(q.explanation) ? q.explanation : "");
                _feedbackText.color = teachOnWrong ? new Color(1f, 0.8f, 0.4f) : Color.red;
                Play("Sounds/SFX/quiz_wrong");
            }

            ScheduleAdvance();
        }

        void OnTimeOut()
        {
            if (_answered) return;
            _answered = true;
            var q = _index < questions.Count ? questions[_index] : null;

            for (int i = 0; i < _optionButtons.Count; i++) _optionButtons[i].interactable = false;
            if (q != null && q.correctIndex >= 0 && q.correctIndex < _optionButtons.Count)
                _optionButtons[q.correctIndex].image.color = new Color(0.15f, 0.65f, 0.30f, 1f);

            _feedbackText.text = "超时！本题计为答错。" +
                (q != null && !string.IsNullOrEmpty(q.explanation) ? q.explanation : "");
            _feedbackText.color = new Color(1f, 0.55f, 0.35f);
            Play("Sounds/SFX/quiz_wrong");
            ScheduleAdvance();
        }

        void ScheduleAdvance()
        {
            _advanceTimer = Mathf.Max(0.01f, advanceDelay);
        }

        void Finish()
        {
            int total = QuestionCount;
            bool pass = total > 0 && CorrectCount >= PassCorrectCount;
            Passed = pass;
            Failed = !pass;

            _progressText.text = "答题结束";
            _questionText.text = pass
                ? "通过！答对 " + CorrectCount + " / " + total + " 题，已达到 " + Mathf.RoundToInt(passRatio * 100f) + "% 的要求。"
                : "未通过：答对 " + CorrectCount + " / " + total + " 题，需要至少 " + PassCorrectCount +
                  " 题（" + Mathf.RoundToInt(passRatio * 100f) + "%）。右键单击灭火器可重新答题。";
            _questionText.color = pass ? new Color(0.5f, 0.95f, 0.55f) : new Color(1f, 0.45f, 0.45f);
            _feedbackText.text = "";
            for (int i = 0; i < _optionButtons.Count; i++) _optionButtons[i].gameObject.SetActive(false);

            if (pass)
            {
                Play("Sounds/SFX/quiz_correct");
                onPassed?.Invoke();
            }
            else
            {
                Play("Sounds/SFX/quiz_wrong");
                onFailed?.Invoke();
            }

            // 结果停留一会儿再自动收起（AdvanceTime 里推进，便于自检）
            _finished = true;
            _closeTimer = 1.2f;
        }

        void Play(string resourcePath)
        {
            var clip = Resources.Load<AudioClip>(resourcePath);
            if (clip == null) return;
            if (_src == null) _src = GetComponent<AudioSource>();
            if (_src != null) _src.PlayOneShot(clip, 0.7f);
            else AudioSource.PlayClipAtPoint(clip, transform.position, 0.7f);
        }
    }
}
