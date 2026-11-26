using System.Collections.Generic;
using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 41 轮：安全出口外空地上的「情景专属消防常识」展板。
    /// 三块板分别对应电气 / 纸张 / 茶水间，内容互不重复；第 28 轮的通用知识墙保持原样不动。
    /// 语言跟随 DrillLocale（中 / 韩 / 英），切换语言会重建。
    /// </summary>
    public class ScenarioKnowledgeBoards : MonoBehaviour
    {
        [System.Serializable]
        public class BoardSpec
        {
            public string id;                       // 电气 / 纸张 / 茶水间
            public Vector3 center;                  // 广场上的位置
            public float yaw = 90f;                 // 朝西对着从出口走出来的玩家
            public Vector2 size = new Vector2(2.30f, 1.72f);
            public float bottom = 0.60f;
            public string titleCn, titleEn, titleKo;
            public string[] linesCn = new string[0];
            public string[] linesEn = new string[0];
            public string[] linesKo = new string[0];
        }

        [Header("展板")]
        public BoardSpec[] boards = new BoardSpec[0];

        [Header("排版")]
        public float titleSize = 34;
        public float lineSize = 26;

        readonly List<GameObject> _built = new List<GameObject>();
        Transform _root;
        bool _subscribed;

        public int BoardCount => boards != null ? boards.Length : 0;
        public bool IsBuilt => _root != null;

        void Awake()
        {
            EnsureDefaults();
            Build();
            DrillLocale.Changed += OnLocaleChanged;
            _subscribed = true;
        }

        void OnDestroy()
        {
            if (_subscribed) DrillLocale.Changed -= OnLocaleChanged;
            Clear();
        }

        void OnLocaleChanged() { Build(); }

        void Clear()
        {
            foreach (var go in _built) if (go != null) Destroy(go);
            _built.Clear();
            _root = null;
        }

        public void EnsureDefaults()
        {
            if (boards != null && boards.Length >= 3) return;

            boards = new[]
            {
                new BoardSpec
                {
                    id = "电气",
                    center = new Vector3(10.60f, 0f, -3.10f),
                    titleCn = "情景一 · 电气设备起火专属常识",
                    titleEn = "Scenario 1 · Electrical fire essentials",
                    titleKo = "시나리오 1 · 전기 화재 상식",
                    linesCn = new[]
                    {
                        "① 电气火灾第一动作是断电：拉总闸 / 拔插头，再谈灭火",
                        "② 未断电严禁用水与水基，导电会造成触电",
                        "③ 带电只能用 CO₂ 或干粉，喷嘴离带电体 ≥ 0.5 米",
                        "④ 配电箱、机箱冒烟先断电再开柜门，防回燃爆燃",
                        "⑤ CO₂ 不留残渣，适合电脑与精密仪器",
                        "⑥ 复电前确认内部无余火、线路绝缘完好"
                    },
                    linesEn = new[]
                    {
                        "1. Cut the power first: main breaker / unplug, then fight the fire",
                        "2. Never use water or wet chemical before de-energizing",
                        "3. Live equipment: only CO₂ or dry powder, nozzle ≥0.5 m away",
                        "4. Smoking cabinets: de-energize before opening the door",
                        "5. CO₂ leaves no residue — best for computers and instruments",
                        "6. Before re-energizing, confirm no embers and intact insulation"
                    },
                    linesKo = new[]
                    {
                        "① 전기 화재의 첫 조치는 전원 차단(차단기/플러그)",
                        "② 전원 차단 전에는 물·수계 소화기 금지(감전 위험)",
                        "③ 활선 상태에서는 CO₂ 또는 분말만, 노즐 0.5 m 이상",
                        "④ 배전함·본체에서 연기 시 전원 차단 후 문을 연다",
                        "⑤ CO₂는 잔여물이 없어 컴퓨터·정밀기기에 적합",
                        "⑥ 재통전 전 잔불과 절연 상태를 확인"
                    }
                },
                new BoardSpec
                {
                    id = "纸张",
                    center = new Vector3(10.60f, 0f, 0.05f),
                    titleCn = "情景二 · 纸张 / 固体可燃物专属常识",
                    titleEn = "Scenario 2 · Paper / Class A essentials",
                    titleKo = "시나리오 2 · 종이(A급) 화재 상식",
                    linesCn = new[]
                    {
                        "① 纸张、文件、纸箱属 A 类固体火灾，用水基或 ABC 干粉",
                        "② 纸堆是层间阴燃，火焰根部喷透后要拆堆翻查",
                        "③ 不要抱着燃烧的纸堆跑，走动带风会吹散带火纸屑",
                        "④ 先关门窗减少供风，再从上风口扇形扫射",
                        "⑤ 灭完持续浇水/翻查约 10 分钟，防阴燃复燃",
                        "⑥ 档案室优先水基，减少干粉对重要文件的污染"
                    },
                    linesEn = new[]
                    {
                        "1. Paper, files and cartons are Class A — water-based or ABC powder",
                        "2. Paper smoulders between layers: spray the base, then spread and recheck",
                        "3. Never carry a burning stack around — draughts scatter embers",
                        "4. Close doors and windows to cut air, then sweep from upwind",
                        "5. Re-wet and recheck for ~10 minutes to stop re-ignition",
                        "6. Archives: prefer water-based to avoid powder contamination"
                    },
                    linesKo = new[]
                    {
                        "① 종이·서류·상자는 A급 고체 화재, 수계 또는 ABC 분말",
                        "② 종이 더미는 층간 음연: 불꽃根部 분사 후 헤쳐 확인",
                        "③ 불붙은 종이 더미를 들고 뛰지 않는다(불티 비산)",
                        "④ 창·문을 닫아 통기를 줄이고 상풍에서 좌우로 훑는다",
                        "⑤ 소화 후 약 10분간 물을 뿌리며 재점화를 확인",
                        "⑥ 문서고는 수계를 우선해 분말 오염을 줄인다"
                    }
                },
                new BoardSpec
                {
                    id = "茶水间",
                    center = new Vector3(10.60f, 0f, 3.20f),
                    titleCn = "情景三 · 茶水间油锅专属常识",
                    titleEn = "Scenario 3 · Pantry / Class F essentials",
                    titleKo = "시나리오 3 · 탕비실 기름 화재 상식",
                    linesCn = new[]
                    {
                        "① 油锅起火先关燃气/电源，再盖锅盖或灭火毯窒息",
                        "② 严禁泼水：热油遇水瞬间爆燃，火球直冲天花板",
                        "③ 食用油火属 F 类，优先湿化学（水基）灭火器",
                        "④ 灭完别马上开盖，等油温降到 100℃ 以下再处理",
                        "⑤ 同时关掉抽油烟机与排风，油垢是蔓延路径",
                        "⑥ 火苗窜入排烟管道立即撤离并报火警，不要自行拆管"
                    },
                    linesEn = new[]
                    {
                        "1. Cut gas/power, then smother with a lid or fire blanket",
                        "2. Never pour water — hot oil explodes into a fireball",
                        "3. Cooking oil is Class F: wet chemical first",
                        "4. Do not open the lid until the oil drops below 100 °C",
                        "5. Switch off the hood and exhaust — grease is the spread path",
                        "6. If flames enter the duct, evacuate and call the fire service"
                    },
                    linesKo = new[]
                    {
                        "① 기름솥 화재는 가스/전원 차단 후 뚜껑·소화포로 질식",
                        "② 물을 붓지 않는다: 뜨거운 기름이 폭발적으로 연소",
                        "③ 식용유 화재는 F급, 수계(습식화학) 소화기 우선",
                        "④ 소화 후 바로 뚜껑을 열지 말고 100℃ 이하까지 대기",
                        "⑤ 후드·배기팬을 끄고, 기름때가 확산 경로임을 기억",
                        "⑥ 화염이 배기 덕트로 들어가면 즉시 대피·신고"
                    }
                }
            };
        }

        void Build()
        {
            Clear();
            if (boards == null) return;

            var rootGo = new GameObject("ScenarioKnowledgeBoards");
            rootGo.transform.SetParent(transform, false);
            _root = rootGo.transform;
            _built.Add(rootGo);

            for (int i = 0; i < boards.Length; i++) BuildOne(boards[i], i);
            Debug.Log("[ScenarioKnowledge] 已生成 " + boards.Length + " 块情景专属展板");
        }

        void BuildOne(BoardSpec spec, int index)
        {
            var go = new GameObject("ScenarioBoard_" + (string.IsNullOrEmpty(spec.id) ? index.ToString() : spec.id),
                                    typeof(RectTransform), typeof(Canvas), typeof(CanvasScaler));
            go.transform.SetParent(_root, false);
            go.transform.position = spec.center;
            go.transform.rotation = Quaternion.Euler(0f, spec.yaw, 0f);
            var canvas = go.GetComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 310;
            var rt = go.GetComponent<RectTransform>();
            float px = 1000f;
            float py = px * spec.size.y / Mathf.Max(0.0001f, spec.size.x);
            rt.sizeDelta = new Vector2(px, py);
            float s = spec.size.x / px;
            rt.localScale = new Vector3(s, s, s);

            var bg = go.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.15f, 0.19f, 1f);
            bg.raycastTarget = false;

            var head = new GameObject("Head", typeof(RectTransform), typeof(Image));
            head.transform.SetParent(go.transform, false);
            var himg = head.GetComponent<Image>();
            himg.color = index == 1 ? new Color(0.62f, 0.38f, 0.12f, 1f) : new Color(0.12f, 0.42f, 0.30f, 1f);
            himg.raycastTarget = false;
            var hrt = head.GetComponent<RectTransform>();
            hrt.anchorMin = new Vector2(0f, 0.80f);
            hrt.anchorMax = new Vector2(1f, 1f);
            hrt.offsetMin = Vector2.zero;
            hrt.offsetMax = Vector2.zero;

            AddText(go.transform, "Title", DrillLocale.T(spec.titleCn, spec.titleEn, spec.titleKo),
                    (int)titleSize, new Color(1f, 0.93f, 0.62f), TextAnchor.MiddleCenter,
                    new Vector2(0f, 0.80f), new Vector2(1f, 1f));

            string[] lines = DrillLocale.Korean ? spec.linesKo
                             : DrillLocale.English ? spec.linesEn : spec.linesCn;
            AddText(go.transform, "Body", string.Join("\n", lines ?? new string[0]),
                    (int)lineSize, new Color(0.94f, 0.97f, 0.96f), TextAnchor.UpperLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 0.80f));

            for (int k = 0; k < 2; k++)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = "ScenarioBoard_Post_" + k;
                post.transform.SetParent(_root, false);
                post.transform.position = spec.center + new Vector3(0f, spec.bottom * 0.5f,
                    (k == 0 ? -1f : 1f) * (spec.size.x * 0.5f - 0.16f));
                post.transform.localRotation = Quaternion.Euler(0f, spec.yaw, 0f);
                post.transform.localScale = new Vector3(0.08f, spec.bottom, 0.08f);
                var col = post.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var r = post.GetComponent<Renderer>();
                if (r != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    mat.color = new Color(0.32f, 0.30f, 0.28f);
                    r.sharedMaterial = mat;
                }
                _built.Add(post);
            }
        }

        void AddText(Transform parent, string name, string content, int size, Color color,
                     TextAnchor anchor, Vector2 aMin, Vector2 aMax)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
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
            t.lineSpacing = 1.25f;
            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = aMin;
            rt.anchorMax = aMax;
            rt.offsetMin = new Vector2(20f, 12f);
            rt.offsetMax = new Vector2(-20f, -12f);
        }
    }
}
