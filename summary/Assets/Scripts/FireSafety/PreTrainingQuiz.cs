using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 事前教育（岗前消防常识教学）。
    /// 第 42 轮改造：题库按情景分三套（电气 / 纸张 / 茶水间），每套 10 题、每题 20 秒、60% 通过，
    /// 内容覆盖「职责、报警装置、出口与集合地点、灭火器类型与用法 + 本情景知识点」。
    /// 测验只是教学环节，通过与否都不再阻挡进入演练、也不作为能否疏散的条件；
    /// 未通过时面板给「重新答题」与「继续进入演练」两个按钮。
    /// 独立于灭火器问答：取瓶时的那 5 题仍保留（操作流程训练的知识确认）。
    /// </summary>
    public class PreTrainingQuiz : MonoBehaviour
    {
        public static PreTrainingQuiz Instance { get; private set; }

        [Serializable]
        public class Question
        {
            [TextArea(2, 4)] public string qCn;
            [TextArea(2, 4)] public string qEn;
            [TextArea(2, 4)] public string qKo;
            public string[] optionsCn = new string[3];
            public string[] optionsEn = new string[3];
            public string[] optionsKo = new string[3];
            public int correctIndex;
            [TextArea(1, 3)] public string expCn;
            [TextArea(1, 3)] public string expEn;
            [TextArea(1, 3)] public string expKo;
        }

        [Header("考核规则")]
        [Tooltip("通过比例：0.6 = 60%（10 题需答对 6 题）")]
        [Range(0.3f, 1f)] public float passRatio = 0.6f;
        [Tooltip("每题限时（秒），超时计为答错")]
        public float timePerQuestion = 20f;
        [Tooltip("答完一题的停留秒数（看讲解）")]
        public float advanceDelay = 1.6f;

        [Header("面板")]
        public float distanceFromCamera = 1.20f;
        public float verticalOffset = -0.03f;
        public float panelScale = 0.00085f;

        [Header("第 42 轮：教学定位")]
        [Tooltip("按情景使用内置题库（电气 / 纸张 / 茶水间各一套），覆盖旧场景里序列化的通用题库")]
        public bool useBuiltInBanks = true;
        [Tooltip("当前情景索引：0 电气 / 1 纸张 / 2 茶水间")]
        public int scenarioIndex = 0;
        [Tooltip("只作教学记录：无论是否达到 60%，都允许继续进入演练（不作为疏散条件）")]
        public bool educationalOnly = true;

        [Header("题目")]
        public List<Question> questions = new List<Question>();

        Canvas _canvas;
        Transform _panel;
        Text _titleText, _progressText, _timerText, _questionText, _feedbackText, _scoreText;
        Button[] _optionButtons;
        Text[] _optionLabels;

        int _index;
        int _correct;
        int _bankIndex = -1;
        float _questionStart;
        float _nextAllowed;
        bool _answered;
        Action _onPassed;

        // 第 45 轮：每次显示题目都把 A/B/C 三个选项重新随机排序（答案位置不再固定在原题库里）。
        // _perm[按钮序号] = 原题库里的选项下标。
        int[] _perm = new int[] { 0, 1, 2 };
        int[] _lastPerm;

        /// <summary>当前这一题的选项排列（按钮 A/B/C 依次对应的题库下标），供自检/探针核对。</summary>
        public int[] CurrentPermutation => _perm;
        /// <summary>正确答案此刻落在哪个按钮上（0=A / 1=B / 2=C）。</summary>
        public int ButtonIndexOfCorrect()
        {
            var q = QuestionCount > 0 ? questions[Mathf.Clamp(_index, 0, QuestionCount - 1)] : null;
            if (q == null) return 0;
            for (int i = 0; i < 3; i++) if (_perm[i] == q.correctIndex) return i;
            return 0;
        }

        public bool IsOpen { get; private set; }
        public bool Passed { get; private set; }
        public bool Failed { get; private set; }
        public int CorrectCount => _correct;
        public int QuestionIndex => _index;
        public int QuestionCount => questions != null ? questions.Count : 0;
        public int RequiredCorrect => Mathf.Max(1, Mathf.RoundToInt(QuestionCount * passRatio));

        void Awake()
        {
            Instance = this;
            EnsureQuestions(scenarioIndex);
        }

        void OnDestroy() { if (Instance == this) Instance = null; }

        // ---------------- 题库 ----------------

        /// <summary>第 47 轮：身份题库用哪个身份（优先取场上 DrillFlowDirector 的随机身份）。</summary>
        [Header("身份题库（第 47 轮）")]
        [Tooltip("取不到 DrillFlowDirector 时的回退身份")]
        public DrillFlowDirector.DrillRole role = DrillFlowDirector.DrillRole.Employee;

        public DrillFlowDirector.DrillRole EffectiveRole => DrillFlowDirector.Instance != null
            ? DrillFlowDirector.Instance.AssignedRole : role;

        public string RoleLabel => DrillLocale.T(RoleCn, RoleEn, RoleKo);
        public string RoleCn => EffectiveRole == DrillFlowDirector.DrillRole.ResponseLead ? "火灾初期应对负责人" : "普通员工";
        public string RoleEn => EffectiveRole == DrillFlowDirector.DrillRole.ResponseLead ? "initial response lead" : "general employee";
        public string RoleKo => EffectiveRole == DrillFlowDirector.DrillRole.ResponseLead ? "초기 대응 책임자" : "일반 직원";

        public void EnsureQuestions() { EnsureQuestions(scenarioIndex); }

        /// <summary>第 47 轮：按情景 + 身份组 5 题（4 道通用/情景 + 1 道角色题）。</summary>
        public void EnsureQuestions(int index)
        {
            if (useBuiltInBanks)
            {
                int idx = Mathf.Clamp(index, 0, PreTrainingBanks.Count - 1);
                questions = PreTrainingBanks.ForRole(idx, EffectiveRole);
                _bankIndex = idx;
                return;
            }
            if (questions != null && questions.Count >= 10) { _bankIndex = index; return; }

            questions = new List<Question>
            {
                new Question {
                    qCn = "发现火情后，第一件该做的事是什么？",
                    qEn = "What is the first thing to do when you discover a fire?",
                    qKo = "불을 발견했을 때 가장 먼저 해야 할 일은?",
                    optionsCn = new[] { "先拍照留证", "立即呼喊报警并启动报警按钮", "先去找灭火器" },
                    optionsEn = new[] { "Take a photo first", "Shout for help and trigger the alarm", "Look for an extinguisher first" },
                    optionsKo = new[] { "먼저 사진을 찍는다", "소리쳐 알리고 수동 발신기를 누른다", "먼저 소화기를 찾는다" },
                    correctIndex = 1,
                    expCn = "发现火情先报警：呼喊提醒周围人并按下手动报警按钮，同时拨打 119。早一分钟报警，少一分损失。",
                    expEn = "Alarm first: warn people around you, press the manual call point and dial 119.",
                    expKo = "먼저 경보: 주변에 알리고 수동 발신기를 누른 뒤 119에 신고합니다."
                },
                new Question {
                    qCn = "拨打 119 时，必须说清楚哪些信息？",
                    qEn = "When calling 119, what must you state clearly?",
                    qKo = "119 신고 시 반드시 알려야 하는 정보는?",
                    optionsCn = new[] { "单位地址、起火物、火势大小、有无人员被困", "只需报出自己的名字", "只报火警号码即可" },
                    optionsEn = new[] { "Address, what is burning, fire size, people trapped", "Only your name", "Only the fire station number" },
                    optionsKo = new[] { "주소, 연소 물질, 불 크기, 인원 갇힘 여부", "이름만", "소방서 번호만" },
                    correctIndex = 0,
                    expCn = "119 报警要素：详细地址（楼层/房间）、起火物、火势、有无被困人员、报警人联系方式，并派人到路口引导消防车。",
                    expEn = "Give the exact address, what is burning, fire size, trapped people and your contact, then send someone to guide the fire engine.",
                    expKo = "상세 주소, 연소 물질, 불 크기, 갇힌 인원, 연락처를 알리고 소방차를 인도할 사람을 보냅니다."
                },
                new Question {
                    qCn = "疏散时正确的优先顺序是？",
                    qEn = "What is the correct priority during evacuation?",
                    qKo = "대피 시 올바른 우선순위는?",
                    optionsCn = new[] { "先抢救贵重物品", "先救人，后救物", "先把电脑关机再走" },
                    optionsEn = new[] { "Save valuables first", "People first, property second", "Shut down computers before leaving" },
                    optionsKo = new[] { "귀중품을 먼저 챙긴다", "인명 우선, 재산은 그 다음", "컴퓨터를 끄고 나간다" },
                    correctIndex = 1,
                    expCn = "生命第一：先组织人员撤离，绝不折返取物；财物损失可以恢复，人员伤亡无法挽回。",
                    expEn = "Life first: evacuate people and never go back for belongings.",
                    expKo = "생명 우선: 인원을 먼저 대피시키고 절대 물건을 가지러 돌아가지 않습니다."
                },
                new Question {
                    qCn = "什么条件下才可以尝试初期灭火？",
                    qEn = "Under what conditions may you attempt to fight an incipient fire?",
                    qKo = "초기 소화를 시도할 수 있는 조건은?",
                    optionsCn = new[] { "火势很小、有合适灭火器、退路畅通且已报警", "只要看见火就可以扑上去", "人多围观的时候" },
                    optionsEn = new[] { "Small fire, right extinguisher, clear escape route, alarm already raised", "Any fire you can see", "When a crowd is watching" },
                    optionsKo = new[] { "불이 작고, 맞는 소화기가 있으며, 도주로가 확보되고, 이미 신고했을 때", "불만 보이면 바로 달려든다", "구경하는 사람이 많을 때" },
                    correctIndex = 0,
                    expCn = "初期灭火三前提：火势处于初期（约一张纸/一个纸箱大小）、有对路灭火器、身后有退路且已报警。任一不满足就立即撤离。",
                    expEn = "Three conditions: incipient fire, the right extinguisher, an escape route behind you and the alarm already raised.",
                    expKo = "세 가지 조건: 초기 화재, 맞는 소화기, 뒤쪽 도주로 확보와 신고 완료."
                },
                new Question {
                    qCn = "电气设备着火、还没断电时，能否用水或水基灭火器？",
                    qEn = "Before the power is cut, can you use water or a wet-chemical extinguisher on electrical fire?",
                    qKo = "전원을 차단하기 전 전기 화재에 물이나 수계 소화기를 쓸 수 있는가?",
                    optionsCn = new[] { "可以，水量小没关系", "不可以，有触电危险", "可以，只要戴手套" },
                    optionsEn = new[] { "Yes, a little water is fine", "No, risk of electric shock", "Yes, if you wear gloves" },
                    optionsKo = new[] { "된다, 물이 조금이면 괜찮다", "안 된다, 감전 위험이 있다", "된다, 장갑만 끼면 된다" },
                    correctIndex = 1,
                    expCn = "未断电时严禁用水/水基，导电会造成触电；应先切断电源，再用 CO₂ 或干粉，喷嘴与带电体保持 0.5 米以上。",
                    expEn = "Never use water before de-energizing. Cut power first, then use CO₂ or dry powder and keep 0.5 m clearance.",
                    expKo = "전원 차단 전에는 물을 절대 쓰지 않습니다. 전원을 끊은 뒤 CO₂나 분말로 0.5 m 이상 거리를 둡니다."
                },
                new Question {
                    qCn = "纸张、文件、纸箱着火属于哪类火灾？",
                    qEn = "Which fire class are paper, documents and cartons?",
                    qKo = "종이, 서류, 종이 상자는 어느 급 화재인가?",
                    optionsCn = new[] { "A 类（固体可燃物）", "B 类（液体）", "D 类（金属）" },
                    optionsEn = new[] { "Class A (solids)", "Class B (liquids)", "Class D (metals)" },
                    optionsKo = new[] { "A급(고체 가연물)", "B급(액체)", "D급(금속)" },
                    correctIndex = 0,
                    expCn = "A 类固体火灾用水基或 ABC 干粉；纸张堆叠会阴燃，喷完要拆堆检查余火，防止复燃。",
                    expEn = "Class A uses water-based or ABC powder; paper stacks smoulder, so spread and recheck after spraying.",
                    expKo = "A급은 수계 또는 ABC 분말을 사용하며, 종이 더미는 잔불 확인을 위해 헤쳐 봐야 합니다."
                },
                new Question {
                    qCn = "茶水间油锅起火，正确的处置是？",
                    qEn = "What is the correct action for a burning oil pot in the pantry?",
                    qKo = "탕비실 기름솥 화재의 올바른 대응은?",
                    optionsCn = new[] { "直接泼水降温", "先关火源，再盖锅盖或灭火毯窒息灭火", "端起油锅跑到室外" },
                    optionsEn = new[] { "Throw water on it", "Turn off the heat, then smother with a lid or fire blanket", "Carry the pot outside" },
                    optionsKo = new[] { "바로 물을 붓는다", "불을 끄고 뚜껑이나 소화포로 질식 소화한다", "솥을 들고 밖으로 뛴다" },
                    correctIndex = 1,
                    expCn = "油火严禁泼水，热油遇水会瞬间爆燃；先关火源，用锅盖/灭火毯覆盖窒息，或用湿化学（F 类）灭火器。",
                    expEn = "Never throw water on burning oil. Cut the heat and smother with a lid or blanket, or use a wet-chemical extinguisher.",
                    expKo = "기름불에 물을 붓지 않습니다. 불을 끄고 뚜껑이나 소화포로 덮어 질식 소화합니다."
                },
                new Question {
                    qCn = "浓烟中逃生，身体姿态应该是？",
                    qEn = "How should you move through heavy smoke?",
                    qKo = "짙은 연기 속에서의 이동 자세는?",
                    optionsCn = new[] { "直立快跑，越快越好", "低姿弯腰，用湿毛巾捂口鼻", "乘电梯下楼" },
                    optionsEn = new[] { "Run upright as fast as possible", "Stay low, cover nose and mouth with a wet towel", "Take the elevator" },
                    optionsKo = new[] { "똑바로 서서 빨리 달린다", "낮게 몸을 숙이고 물에 적신 수건으로 코와 입을 막는다", "엘리베이터를 탄다" },
                    correctIndex = 1,
                    expCn = "烟气向上聚集，低姿贴地移动能多吸到新鲜空气；湿毛巾可过滤部分颗粒；火灾时严禁乘电梯。",
                    expEn = "Smoke rises: stay low for fresher air, use a wet towel as a filter, and never use lifts in a fire.",
                    expKo = "연기는 위로 올라가므로 낮게 이동하고 습한 수건으로 여과하며, 화재 시 엘리베이터는 금지입니다."
                },
                new Question {
                    qCn = "干粉灭火器的标准四步是？",
                    qEn = "What are the four standard steps of a dry-powder extinguisher?",
                    qKo = "분말 소화기의 표준 4단계는?",
                    optionsCn = new[] { "提 · 拔 · 握 · 压", "摇 · 晃 · 倒 · 喷", "拔 · 压 · 提 · 握" },
                    optionsEn = new[] { "Lift · Pull · Aim · Squeeze", "Shake · Rock · Tip · Spray", "Pull · Squeeze · Lift · Aim" },
                    optionsKo = new[] { "들기 · 핀 뽑기 · 겨냥 · 압착", "흔들기 · 흔들기 · 기울기 · 분사", "뽑기 · 압착 · 들기 · 겨냥" },
                    correctIndex = 0,
                    expCn = "提（提起灭火器到上风口）→ 拔（拔掉保险销）→ 握（握喷管对准火焰根部）→ 压（压下压把左右扫射）。",
                    expEn = "Lift (carry it upwind), Pull (the pin), Aim (at the base of the flames), Squeeze (and sweep).",
                    expKo = "들기(상풍 위치로) → 핀 뽑기 → 겨냥(불꽃根部) → 압착(좌우로 훑기)."
                },
                new Question {
                    qCn = "到达安全集合点之后，应该做什么？",
                    qEn = "What should you do after reaching the assembly point?",
                    qKo = "안전 집결지에 도착한 뒤 해야 할 일은?",
                    optionsCn = new[] { "各自离开回家", "清点人数并向现场负责人报告", "返回楼内取回个人物品" },
                    optionsEn = new[] { "Go home individually", "Count people and report to the person in charge", "Go back inside for personal items" },
                    optionsKo = new[] { "각자 집으로 간다", "인원을 점검하고 현장 책임자에게 보고한다", "건물 안으로 물건을 가지러 간다" },
                    correctIndex = 1,
                    expCn = "集合点三件事：清点本部门人数、向负责人报告失联人员、留在安全区等待消防到场，绝不擅自返楼。",
                    expEn = "At the assembly point: count your team, report missing persons, and stay clear until the fire service arrives.",
                    expKo = "집결지에서 인원 점검, 실종자 보고, 소방대 도착까지 대기하며 건물로 돌아가지 않습니다."
                }
            };
        }

        // ---------------- 面板 ----------------

        public void Open(Transform anchor, Action onFinished = null, int scenario = -1)
        {
            if (scenario >= 0) scenarioIndex = scenario;
            _onPassed = onFinished;
            _index = 0;
            _correct = 0;
            _answered = false;
            Passed = Failed = false;
            IsOpen = true;
            EnsureQuestions(scenarioIndex);
            Build();
            ShowQuestion();
            Debug.Log("[PreTraining] 事前教育开始（" + PreTrainingBanks.Name(_bankIndex) + " · " + RoleCn + "）："
                      + QuestionCount + " 题，教学达标线 " + RequiredCorrect + " 题（不作为门槛）");
        }

        void Build()
        {
            if (_canvas == null)
            {
                var go = new GameObject("PreTrainingCanvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
                go.transform.SetParent(transform, false);
                _canvas = go.GetComponent<Canvas>();
                _canvas.renderMode = RenderMode.WorldSpace;
                _canvas.sortingOrder = 420;
                var rt = go.GetComponent<RectTransform>();
                rt.sizeDelta = new Vector2(1000, 1000);
                rt.localScale = new Vector3(panelScale, panelScale, panelScale);

                var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image));
                panel.transform.SetParent(go.transform, false);
                var img = panel.GetComponent<Image>();
                img.color = new Color(0.05f, 0.07f, 0.12f, 0.96f);
                var prt = panel.GetComponent<RectTransform>();
                prt.sizeDelta = new Vector2(1000, 1000);
                prt.anchorMin = prt.anchorMax = new Vector2(0.5f, 0.5f);
                prt.anchoredPosition = Vector2.zero;
                _panel = panel.transform;

                _titleText = MakeText(_panel, "事前教育 · 消防常识考核", 44, new Color(1f, 0.85f, 0.3f),
                                      new Vector2(0, -30), new Vector2(940, 64));
                _progressText = MakeText(_panel, "第 1 / 10 题", 28, new Color(0.72f, 0.82f, 1f),
                                         new Vector2(0, -102), new Vector2(440, 40), TextAnchor.MiddleLeft, new Vector2(-470, 0));
                _timerText = MakeText(_panel, "剩余 20 秒", 28, new Color(1f, 0.72f, 0.42f),
                                      new Vector2(0, -102), new Vector2(440, 40), TextAnchor.MiddleRight, new Vector2(470, 0));
                _questionText = MakeText(_panel, "", 34, Color.white,
                                         new Vector2(0, -190), new Vector2(920, 170));

                _optionButtons = new Button[3];
                _optionLabels = new Text[3];
                for (int i = 0; i < 3; i++)
                {
                    int idx = i;
                    var b = MakeButton(_panel, "A", 30, new Color(0.14f, 0.24f, 0.42f),
                                       new Vector2(0, -400 - i * 96), new Vector2(900, 82));
                    _optionButtons[i] = b;
                    _optionLabels[i] = b.GetComponentInChildren<Text>();
                    b.onClick.AddListener(() => Choose(idx));
                }

                _feedbackText = MakeText(_panel, "", 26, new Color(0.62f, 1f, 0.72f),
                                         new Vector2(0, -700), new Vector2(920, 120));
                _scoreText = MakeText(_panel, "", 24, new Color(0.8f, 0.86f, 1f),
                                      new Vector2(0, -830), new Vector2(920, 80));
            }
            else
            {
                _canvas.gameObject.SetActive(true);
            }
        }

        void ShowQuestion()
        {
            var q = questions[Mathf.Clamp(_index, 0, questions.Count - 1)];
            _titleText.text = DrillLocale.T("事前教育 · ", "Pre-drill training · ", "사전 교육 · ")
                            + PreTrainingBanks.Name(_bankIndex)
                            + " · " + RoleLabel;
            _progressText.text = DrillLocale.T("第 ", "Question ", "문제 ")
                               + (_index + 1) + " / " + QuestionCount;
            _questionText.text = DrillLocale.T(q.qCn, q.qEn, q.qKo);

            string[] opts = DrillLocale.Korean ? q.optionsKo
                            : DrillLocale.English ? q.optionsEn : q.optionsCn;

            // 第 45 轮：每题重新洗一次选项顺序，同样的题在不同时间进来选项位置也不同
            ShuffleOptionOrder();
            for (int i = 0; i < 3; i++)
            {
                int src = _perm[i];
                string label = (char)('A' + i) + ". " + (opts != null && src < opts.Length ? opts[src] : "");
                _optionLabels[i].text = label;
                // 上一轮结束页会隐藏部分按钮（达标时只留「进入演练」），重新开一套题时必须恢复三个选项
                _optionButtons[i].gameObject.SetActive(true);
                _optionButtons[i].interactable = true;
                _optionButtons[i].GetComponent<Image>().color = new Color(0.14f, 0.24f, 0.42f);
            }
            _feedbackText.text = "";
            // 第 46 轮：把课程要求的四项教学内容直接写在面板底部（职责 / 报警装置 / 出口与集合点 / 灭火器类型与用法）
            // 第 47 轮：本套固定 5 题 = 4 道通用（报警 / 出口集合 / 灭火器 / 情景）+ 1 道身份题，面板上标清楚是哪套
            string topics = DrillLocale.T("本阶段覆盖：① 职责　② 报警装置　③ 出口与集合点　④ 灭火器类型与用法\n",
                                          "This stage covers: 1 roles  2 alarm devices  3 exits & assembly point  4 extinguisher types and use\n",
                                          "이 단계 내용: ① 역할  ② 경보 장치  ③ 출구·집결지  ④ 소화기 종류와 사용법\n")
                          + DrillLocale.T("本次身份：" + RoleLabel + "｜本套 " + QuestionCount + " 题 = 通用 4 题 + "
                                          + (EffectiveRole == DrillFlowDirector.DrillRole.ResponseLead ? "负责人判断" : "员工职责") + " 1 题\n",
                                          "Role: " + RoleEn + " | " + QuestionCount + " questions = 4 shared + 1 role-specific ("
                                          + (EffectiveRole == DrillFlowDirector.DrillRole.ResponseLead ? "lead decision" : "employee duty") + ")\n",
                                          "이번 역할: " + RoleKo + " | " + QuestionCount + "문항 = 공통 4 + 역할 "
                                          + (EffectiveRole == DrillFlowDirector.DrillRole.ResponseLead ? "판단" : "임무") + " 1\n");
            _scoreText.text = topics + (educationalOnly
                ? DrillLocale.T("每题 20 秒；答对 " + RequiredCorrect + " 题（" + Mathf.RoundToInt(passRatio * 100) + "%）为教学达标，未达标也可继续进入演练",
                                "20 s each; " + RequiredCorrect + "/" + QuestionCount + " (" + Mathf.RoundToInt(passRatio * 100) + "%) is the learning target — you may continue even below it",
                                "문제당 20초, " + RequiredCorrect + "/" + QuestionCount + " (" + Mathf.RoundToInt(passRatio * 100) + "%)이 학습 목표이며 미달이어도 계속 진행할 수 있습니다")
                : DrillLocale.T("每题 20 秒，答对 " + RequiredCorrect + " 题（"
                                + Mathf.RoundToInt(passRatio * 100) + "%）即可进入演练",
                                "20 s per question, need " + RequiredCorrect + "/" + QuestionCount
                                + " (" + Mathf.RoundToInt(passRatio * 100) + "%) to pass",
                                "문제당 20초, " + RequiredCorrect + "/" + QuestionCount + " 정답 시 통과"));
            _questionStart = Time.unscaledTime;
            _answered = false;
            _nextAllowed = 0f;
        }

        /// <summary>Fisher-Yates 洗 0/1/2：保证每次 A/B/C 里正确项的位置都可能不同。</summary>
        void ShuffleOptionOrder()
        {
            _perm = RollPermutation(_lastPerm);
            _lastPerm = _perm;
        }

        /// <summary>生成一个 0/1/2 的随机排列（纯函数，便于自检直接统计分布）。</summary>
        public static int[] RollPermutation(int[] last)
        {
            int[] p = new int[] { 0, 1, 2 };
            for (int i = 2; i > 0; i--)
            {
                int j = UnityEngine.Random.Range(0, i + 1);
                int t = p[i]; p[i] = p[j]; p[j] = t;
            }
            // 极小概率会和上一次完全相同；再换一次，避免玩家看到「同一个答案位置连续出现」
            if (last != null && last.Length == 3
                && p[0] == last[0] && p[1] == last[1] && p[2] == last[2])
            {
                int t = p[1]; p[1] = p[2]; p[2] = t;
            }
            return p;
        }

        public void AnswerOption(int index) => Choose(index);

        void Choose(int index)
        {
            if (!IsOpen || _answered) return;
            if (Time.unscaledTime < _nextAllowed) return;
            _answered = true;

            var q = questions[_index];
            bool ok = index >= 0 && index < 3 && _perm[index] == q.correctIndex;
            if (ok) _correct++;

            int correctBtn = ButtonIndexOfCorrect();
            _optionButtons[correctBtn].GetComponent<Image>().color = new Color(0.16f, 0.52f, 0.28f);
            if (!ok && index >= 0 && index < 3)
                _optionButtons[index].GetComponent<Image>().color = new Color(0.62f, 0.18f, 0.16f);
            for (int i = 0; i < 3; i++) _optionButtons[i].interactable = false;

            _feedbackText.color = ok ? new Color(0.62f, 1f, 0.72f) : new Color(1f, 0.78f, 0.62f);
            _feedbackText.text = (ok ? DrillLocale.T("回答正确。", "Correct. ", "정답입니다. ")
                                     : DrillLocale.T("回答错误。", "Wrong. ", "오답입니다. "))
                               + DrillLocale.T(q.expCn, q.expEn, q.expKo);
            _scoreText.text = DrillLocale.T("当前答对 ", "Correct so far: ", "현재 정답 ")
                            + _correct + " / " + (_index + 1);

            _nextAllowed = Time.unscaledTime + advanceDelay;
            CancelInvoke(nameof(NextQuestion));
            Invoke(nameof(NextQuestion), advanceDelay);
        }

        void NextQuestion()
        {
            if (!IsOpen) return;
            _index++;
            if (_index >= QuestionCount) { Finish(); return; }
            ShowQuestion();
        }

        void Finish()
        {
            Passed = _correct >= RequiredCorrect;
            Failed = !Passed;

            string head = Passed
                ? DrillLocale.T("教学达标：" + _correct + " / " + QuestionCount + " 题（≥" + Mathf.RoundToInt(passRatio * 100) + "%）",
                                "Learning target met: " + _correct + "/" + QuestionCount + " (≥" + Mathf.RoundToInt(passRatio * 100) + "%)",
                                "학습 목표 달성: " + _correct + "/" + QuestionCount + " (≥" + Mathf.RoundToInt(passRatio * 100) + "%)")
                : DrillLocale.T("未达教学标的：" + _correct + " / " + QuestionCount + " 题（需 " + RequiredCorrect + " 题）",
                                "Below the learning target: " + _correct + "/" + QuestionCount + " (" + RequiredCorrect + " needed)",
                                "학습 목표 미달: " + _correct + "/" + QuestionCount + " (" + RequiredCorrect + "문제 필요)");
            string tail = educationalOnly
                ? DrillLocale.T("　本测验只属于事前教学，通过与否都不影响进入演练、也不是能否疏散的条件。",
                                " This quiz is part of pre-drill training only: passing is not required to start the drill or to evacuate.",
                                " 본 평가는 사전 교육일 뿐이며, 통과 여부는 훈련 시작이나 대피 조건이 아닙니다.")
                : "";
            _questionText.text = head + tail;

            for (int i = 0; i < 3; i++)
            {
                _optionButtons[i].gameObject.SetActive(i < 2);
                _optionButtons[i].interactable = true;
                _optionButtons[i].GetComponent<Image>().color = new Color(0.14f, 0.24f, 0.42f);
                _optionButtons[i].onClick.RemoveAllListeners();
            }

            _optionButtons[0].onClick.AddListener(ConfirmAndContinue);
            if (Passed)
            {
                _optionLabels[0].text = DrillLocale.T("进入演练", "START THE DRILL", "훈련 시작");
                _optionButtons[1].gameObject.SetActive(false);
            }
            else if (educationalOnly)
            {
                _optionLabels[0].text = DrillLocale.T("继续进入演练（教学记录已保存）", "CONTINUE TO THE DRILL", "계속 훈련 시작");
                _optionLabels[1].text = DrillLocale.T("重新答题", "RETAKE", "다시 응시");
                _optionButtons[1].gameObject.SetActive(true);
                _optionButtons[1].onClick.AddListener(Restart);
            }
            else
            {
                _optionLabels[0].text = DrillLocale.T("重新答题", "RETAKE", "다시 응시");
                _optionButtons[0].onClick.RemoveAllListeners();
                _optionButtons[0].onClick.AddListener(Restart);
                _optionButtons[1].gameObject.SetActive(false);
            }

            _feedbackText.text = "";
            _scoreText.text = DrillLocale.T("教学记录：答对 " + _correct + " / " + QuestionCount,
                                            "Learning record: " + _correct + "/" + QuestionCount + " correct",
                                            "학습 기록: " + _correct + "/" + QuestionCount + " 정답");
            Debug.Log("[PreTraining] 教学结束 得分=" + _correct + "/" + QuestionCount + " 达标=" + Passed
                      + " 是否作为门槛=" + (!educationalOnly));
        }

        /// <summary>结束页「进入演练 / 继续进入演练」：关闭面板并回调开始演练。</summary>
        public void ConfirmAndContinue()
        {
            Close();
            _onPassed?.Invoke();
        }

        public void Restart()
        {
            _index = 0; _correct = 0; _answered = false; Passed = Failed = false;
            for (int i = 0; i < 3; i++)
            {
                _optionButtons[i].gameObject.SetActive(true);
                _optionButtons[i].onClick.RemoveAllListeners();
                int idx = i;
                _optionButtons[i].onClick.AddListener(() => Choose(idx));
            }
            ShowQuestion();
        }

        public void Close()
        {
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
        }

        void Update()
        {
            if (!IsOpen || _answered || _index >= QuestionCount) return;
            float left = timePerQuestion - (Time.unscaledTime - _questionStart);
            if (_timerText != null)
                _timerText.text = DrillLocale.T("剩余 " + Mathf.Max(0, Mathf.CeilToInt(left)) + " 秒",
                                                Mathf.Max(0, Mathf.CeilToInt(left)) + " s left",
                                                "남은 " + Mathf.Max(0, Mathf.CeilToInt(left)) + "초");
            if (left <= 0f)
            {
                // 超时：按答错处理
                _answered = true;
                _feedbackText.color = new Color(1f, 0.78f, 0.62f);
                _feedbackText.text = DrillLocale.T("超时，本题计为答错。", "Time out — counted as wrong.", "시간 초과 — 오답 처리.");
                _scoreText.text = DrillLocale.T("当前答对 ", "Correct so far: ", "현재 정답 ") + _correct + " / " + (_index + 1);
                for (int i = 0; i < 3; i++) _optionButtons[i].interactable = false;
                Invoke(nameof(NextQuestion), advanceDelay);
            }
        }

        void LateUpdate()
        {
            if (!IsOpen || _canvas == null) return;
            var cam = Camera.main;
            if (cam == null) return;
            _canvas.transform.position = cam.transform.position
                                         + cam.transform.forward * distanceFromCamera
                                         + Vector3.up * verticalOffset;
            _canvas.transform.rotation = cam.transform.rotation;
        }

        // ---------------- 小工具 ----------------

        Text MakeText(Transform parent, string content, int size, Color color,
                      Vector2 anchored, Vector2 sizeDelta,
                      TextAnchor anchor = TextAnchor.MiddleCenter, Vector2? offset = null)
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
            t.lineSpacing = 1.15f;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = rt.anchorMax = new Vector2(0.5f, 1f);
            rt.pivot = new Vector2(0.5f, 1f);
            rt.sizeDelta = sizeDelta;
            rt.anchoredPosition = anchored + (offset ?? Vector2.zero);
            return t;
        }

        Button MakeButton(Transform parent, string label, int size, Color bg, Vector2 anchored, Vector2 sizeDelta)
        {
            var go = new GameObject("Btn_" + label, typeof(RectTransform), typeof(Image), typeof(Button));
            go.transform.SetParent(parent, false);
            var img = go.GetComponent<Image>();
            img.color = bg;
            img.raycastTarget = true;
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
            t.text = label;
            t.font = UIFont.Resolve();
            if (t.font != null) t.material = t.font.material;
            t.fontSize = size;
            t.color = Color.white;
            t.alignment = TextAnchor.MiddleCenter;
            t.horizontalOverflow = HorizontalWrapMode.Wrap;
            t.raycastTarget = false;
            var trt = textGo.GetComponent<RectTransform>();
            trt.anchorMin = Vector2.zero;
            trt.anchorMax = Vector2.one;
            trt.offsetMin = new Vector2(12f, 4f);
            trt.offsetMax = new Vector2(-12f, -4f);

            var feedback = go.AddComponent<ButtonFeedback>();
            feedback.Init(bg);
            return btn;
        }
    }
}
