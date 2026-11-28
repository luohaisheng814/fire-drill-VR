using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 28 轮：门外广场上的「消防演练知识墙」。
    /// 三块立式展板 + 一块地面喷涂字，玩家从安全出口走出来抬头就能复习：
    /// 灭火剂怎么选、灭火器四步、疏散与集合要点。
    /// 第 41 轮：跟随语言选择（中 / 英 / 韩）重建文案。
    /// 全部用世界空间 Canvas 在运行时构建（中文字体走 UIFont），不依赖预制体。
    /// </summary>
    public class PlazaKnowledgeBoard : MonoBehaviour
    {
        [Header("位置")]
        [Tooltip("知识墙中心（东侧矮墙前，展板朝西对着从安全出口走出来的玩家）")]
        public Vector3 boardCenter = new Vector3(13.40f, 0f, 0f);
        [Tooltip("整面墙的朝向（绕 Y 轴）：90 = 正面朝西（玩家出走廊后正对它）")]
        public float facingYaw = 90f;
        [Tooltip("展板沿墙从左到右的排列顺序（默认 true：按 1→2→3 从玩家左侧排到右侧）")]
        public bool reverseOrder = true;
        [Tooltip("单块展板尺寸（米）")]
        public Vector2 panelSize = new Vector2(2.00f, 1.55f);
        [Tooltip("展板底边离地高度（米）")]
        public float panelBottom = 0.62f;
        [Tooltip("展板间距（米）")]
        public float panelGap = 0.16f;

        [Header("地面字")]
        public Vector3 floorTextCenter = new Vector3(10.90f, 0.02f, 1.55f);
        public Vector2 floorTextSize = new Vector2(4.40f, 0.72f);
        [Tooltip("地面字朝向（绕 Y 轴）：90 = 字头朝东，玩家朝东走时正读")]
        public float floorTextYaw = 90f;

        [Header("内容（当前语言，运行时填充）")]
        public string wallTitle = "办公室消防演练 · 安全知识";
        public string[] panelTitles = { "一、灭火器要对路", "二、灭火器四步", "三、疏散与集合" };
        [System.NonSerialized] public string[][] panelLines;   // 代码内初始化（交错数组不支持序列化）
        public string floorText = "灭火器四步：提 · 拔 · 握 · 压　｜　疏散：低姿 · 湿毛巾 · 到集合点";

        Transform _root;
        readonly System.Collections.Generic.List<Text> _texts = new System.Collections.Generic.List<Text>();

        /// <summary>自检用：已经生成的文字块数量（标题 + 每块展板 5 行 + 地面字）。</summary>
        public int TextCount => _texts.Count;

        /// <summary>自检用：取三块展板的要点（始终返回中文原文，避免切换语言后断言失效）。</summary>
        public string[][] PanelLines()
        {
            return ChineseLines();
        }

        void Awake()
        {
            ApplyLanguageContent();
            Build();
        }

        void OnEnable() { DrillLocale.Changed += Rebuild; }
        void OnDisable() { DrillLocale.Changed -= Rebuild; }

        void OnDestroy()
        {
            DrillLocale.Changed -= Rebuild;
            DestroyRoot();
        }

        /// <summary>语言变化：清掉旧展板，按新语言重建成同样的位置布局。</summary>
        void Rebuild()
        {
            if (!isActiveAndEnabled) return;
            DestroyRoot();
            ApplyLanguageContent();
            Build();
        }

        void DestroyRoot()
        {
            _texts.Clear();
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
            _root = null;
        }

        static string[][] ChineseLines()
        {
            return new[]
            {
                new[]
                {
                    "电气设备火 → 二氧化碳 / 干粉（先断电）",
                    "纸张·木材（A 类）→ 水基 / ABC 干粉",
                    "油锅·小家电（F 类）→ 湿化学 / 干粉",
                    "※ 油火严禁直接泼水，会爆燃飞溅",
                    "喷射对准火焰根部，不是火苗顶端"
                },
                new[]
                {
                    "① 拔：拔掉保险销（喷嘴不要对人）",
                    "② 握：握住喷管，对准火焰根部",
                    "③ 压：压下压把，左右横扫推进",
                    "④ 扫：由近到远、由上到下覆盖燃烧面",
                    "※ 背风站位、背后留好退路；只灭初期小火，火大立即撤离打 119"
                },
                new[]
                {
                    "① 先按墙上手动报警按钮，并大声提醒周围的人「着火了」",
                    "② 判断逃生方向：最近的安全出口，走楼梯、绝不乘电梯",
                    "③ 低姿沿墙快速撤离，不折返取物、不贪恋财物",
                    "④ 撤到空旷安全地带后拨打 119（韩国火警/急救电话）",
                    "⑤ 在集合点清点人数并报告；初期小火且受过训练才用灭火器"
                }
            };
        }

        static string[] EnglishLines(int i)
        {
            switch (i)
            {
                case 0:
                    return new[]
                    {
                        "Electrical fire → CO₂ / dry powder (cut power first)",
                        "Paper / wood (Class A) → water-based / ABC powder",
                        "Cooking oil / appliances (Class F) → wet chemical / powder",
                        "※ Never pour water on oil fires — violent splashing",
                        "Aim at the base of the flames, not the tips"
                    };
                case 1:
                    return new[]
                    {
                        "① Pull the safety pin (never point the nozzle at people)",
                        "② Grip the hose and aim at the base of the fire",
                        "③ Squeeze the lever, sweep side to side",
                        "④ Sweep close-to-far, top-to-bottom over the burning surface",
                        "※ Stand with your back to the wind and keep an escape route behind you; only for a small incipient fire"
                    };
                default:
                    return new[]
                    {
                        "① Press the wall-mounted manual call point and shout “Fire!”",
                        "② Decide the way out: nearest exit, stairs only, never the lift",
                        "③ Stay low and evacuate fast; never go back for belongings",
                        "④ Call 119 (Korean fire & rescue) once you are safely outside",
                        "⑤ Count heads at the assembly point; use an extinguisher only on a small, incipient fire"
                    };
            }
        }

        static string[] KoreanLines(int i)
        {
            switch (i)
            {
                case 0:
                    return new[]
                    {
                        "전기 화재 → 이산화탄소 / 분말 (먼저 전원 차단)",
                        "종이·목재(A급) → 수계 / ABC 분말",
                        "식용유·소형 가전(F급) → 습식 화학 / 분말",
                        "※ 기름 화재에 물을 붓지 마세요 — 폭발적 비산",
                        "불꽃 끝이 아니라 불꽃 밑동을 향해 분사"
                    };
                case 1:
                    return new[]
                    {
                        "① 안전핀을 뽑는다 (노즐을 사람에게 겨누지 않는다)",
                        "② 노즐을 잡고 불꽃 밑동을 향한다",
                        "③ 손잡이를 움켜쥔다",
                        "④ 분말을 골고루 뿌리며 가까운 곳에서 먼 곳으로, 위에서 아래로 덮는다",
                        "※ 바람을 등지고 문(퇴로)을 등지고 서기; 초기 소화에만 사용하고 불이 크면 즉시 대피 후 119"
                    };
                default:
                    return new[]
                    {
                        "① 벽면 수동 발신기를 먼저 누르고 “불이야!”라고 외친다",
                        "② 대피 방향 결정: 가장 가까운 비상구, 계단 이용, 엘리베이터 절대 금지",
                        "③ 낮은 자세로 신속히 대피, 물건을 가지러 돌아가지 않는다",
                        "④ 안전한 곳에 대피한 후 119에 신고한다",
                        "⑤ 집결지에서 인원을 확인한다; 초기 소화는 불이 작고 교육을 받은 경우에만"
                    };
            }
        }

        /// <summary>把三套语言的文案按当前语言写进序列化字段（重建时再调一次）。</summary>
        void ApplyLanguageContent()
        {
            var lang = DrillLocale.Language;

            wallTitle = DrillLocale.T("办公室消防演练 · 安全知识",
                                      "Office Fire Drill · Safety Facts",
                                      "사무실 소방 훈련 · 안전 상식");

            panelTitles = new[]
            {
                DrillLocale.T("一、灭火器要对路", "1 · Match the extinguisher", "1 · 소화기 종류 선택"),
                DrillLocale.T("二、灭火器四步", "2 · Four steps of use", "2 · 소화기 사용 4단계"),
                DrillLocale.T("三、疏散与集合", "3 · Evacuate & assemble", "3 · 대피와 집결")
            };

            var zh = ChineseLines();
            var lines = new string[3][];
            for (int i = 0; i < 3; i++)
            {
                lines[i] = lang == DrillLocale.Lang.Korean ? KoreanLines(i)
                         : lang == DrillLocale.Lang.English ? EnglishLines(i)
                         : zh[i];
            }
            panelLines = lines;

            floorText = DrillLocale.T(
                "灭火器四步：提 · 拔 · 握 · 压　｜　疏散：低姿 · 湿毛巾 · 到集合点",
                "Extinguisher: Pull · Pin · Aim · Squeeze  |  Evacuate: low · wet towel · assembly point",
                "소화기: 들기 · 뽑기 · 겨냥 · 누르기  |  대피: 낮은 자세 · 습한 수건 · 집결지");
        }

        void Build()
        {
            var rootGo = new GameObject("PlazaKnowledgeWall");
            rootGo.transform.SetParent(transform, false);
            rootGo.transform.position = boardCenter;
            rootGo.transform.rotation = Quaternion.Euler(0f, facingYaw, 0f);
            _root = rootGo.transform;

            // 展板沿墙排开：本地 +X 就是「从玩家左边数到右边」（reverseOrder 已处理方向）
            int n = Mathf.Clamp(panelTitles != null ? panelTitles.Length : 0, 1, 3);
            float totalW = n * panelSize.x + (n - 1) * panelGap;
            float startX = totalW * 0.5f - panelSize.x * 0.5f;

            for (int i = 0; i < n; i++)
            {
                float x = reverseOrder ? startX - i * (panelSize.x + panelGap)
                                       : -startX + i * (panelSize.x + panelGap);
                Vector3 local = new Vector3(x, panelBottom + panelSize.y * 0.5f, 0f);
                BuildPanel("KnowledgePanel_" + (i + 1), local,
                           panelTitles[i],
                           panelLines != null && i < panelLines.Length ? panelLines[i] : new string[0],
                           i);
            }

            BuildWallTitle(new Vector3(0f, panelBottom + panelSize.y + 0.42f, 0f));
            BuildFloorText();
        }

        void BuildPanel(string name, Vector3 pos, string title, string[] lines, int index)
        {
            var panel = MakeCanvas(name, pos, panelSize, Quaternion.identity).gameObject;
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.05f, 0.16f, 0.20f, 1.00f);
            bg.raycastTarget = false;

            // 顶部标题条
            var headGo = new GameObject("Head", typeof(RectTransform), typeof(Image));
            headGo.transform.SetParent(panel.transform, false);
            var head = headGo.GetComponent<Image>();
            head.color = new Color(0.10f, 0.42f, 0.28f, 1.00f);
            head.raycastTarget = false;
            var headRt = headGo.GetComponent<RectTransform>();
            headRt.anchorMin = new Vector2(0f, 0.78f);
            headRt.anchorMax = new Vector2(1f, 1f);
            headRt.offsetMin = Vector2.zero;
            headRt.offsetMax = Vector2.zero;

            AddText(panel.transform, "Title", title, 54, FontStyle.Bold,
                    new Color(1f, 0.93f, 0.62f), TextAnchor.MiddleCenter,
                    new Vector2(0f, 0.78f), new Vector2(1f, 1f), new Vector2(16f, 2f));

            AddText(panel.transform, "Body", string.Join("\n", lines), 38, FontStyle.Normal,
                    new Color(0.94f, 0.97f, 0.96f), TextAnchor.UpperLeft,
                    new Vector2(0f, 0f), new Vector2(1f, 0.78f), new Vector2(18f, 10f));

            // 立柱：让展板看起来是立在地上的，而不是浮在空中
            for (int k = 0; k < 2; k++)
            {
                var post = GameObject.CreatePrimitive(PrimitiveType.Cube);
                post.name = name + "_Post_" + (k + 1);
                post.transform.SetParent(_root, false);
                float offset = (k == 0 ? -1f : 1f) * (panelSize.x * 0.5f - 0.16f);
                post.transform.localPosition = new Vector3(pos.x + offset, panelBottom * 0.5f, 0.02f);
                post.transform.localScale = new Vector3(0.08f, panelBottom, 0.08f);
                var col = post.GetComponent<Collider>();
                if (col != null) Destroy(col);
                var r = post.GetComponent<Renderer>();
                if (r != null)
                {
                    var mat = new Material(Shader.Find("Universal Render Pipeline/Lit") ?? Shader.Find("Standard"));
                    mat.color = new Color(0.32f, 0.30f, 0.28f);
                    r.sharedMaterial = mat;
                }
            }
        }

        void BuildWallTitle(Vector3 pos)
        {
            var size = new Vector2(3.6f, 0.32f);
            var panel = MakeCanvas("KnowledgeWallTitle", pos, size, Quaternion.identity).gameObject;
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.78f, 0.16f, 0.12f, 0.96f);
            bg.raycastTarget = false;
            AddText(panel.transform, "Title", wallTitle, 40, FontStyle.Bold,
                    new Color(1f, 0.97f, 0.90f), TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, new Vector2(8f, 2f));
        }

        void BuildFloorText()
        {
            var panel = MakeCanvas("KnowledgeFloorText", Vector3.zero, floorTextSize,
                                   Quaternion.Euler(90f, floorTextYaw, 0f)).gameObject;
            // 地面字用世界坐标定位，不跟展板一起转
            panel.transform.position = floorTextCenter;
            panel.transform.rotation = Quaternion.Euler(90f, floorTextYaw, 0f);
            // 地面喷涂：深绿底 + 白字，远看像刷在地砖上的一条标语带
            var bg = panel.AddComponent<Image>();
            bg.color = new Color(0.06f, 0.28f, 0.18f, 0.90f);
            bg.raycastTarget = false;
            AddText(panel.transform, "Text", floorText, 34, FontStyle.Bold,
                    new Color(0.96f, 1f, 0.94f), TextAnchor.MiddleCenter,
                    Vector2.zero, Vector2.one, new Vector2(10f, 2f));
        }

        /// <summary>建一块世界空间画布（本地 1000×H 像素，按 sizeMeters 缩放），pos 为父级本地坐标。</summary>
        RectTransform MakeCanvas(string name, Vector3 localPos, Vector2 sizeMeters, Quaternion rot)
        {
            var go = new GameObject(name, typeof(RectTransform));
            go.transform.SetParent(_root, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = rot;

            var canvas = go.AddComponent<Canvas>();
            canvas.renderMode = RenderMode.WorldSpace;
            canvas.sortingOrder = 300;
            go.AddComponent<CanvasScaler>();

            var rt = go.GetComponent<RectTransform>();
            float px = 1000f;
            float py = px * sizeMeters.y / Mathf.Max(0.0001f, sizeMeters.x);
            rt.sizeDelta = new Vector2(px, py);
            float s = sizeMeters.x / px;
            rt.localScale = new Vector3(s, s, s);
            return rt;
        }

        void AddText(Transform parent, string name, string content, int size, FontStyle style,
                     Color color, TextAnchor anchor, Vector2 anchorMin, Vector2 anchorMax, Vector2 padding)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(Text));
            go.transform.SetParent(parent, false);
            var txt = go.GetComponent<Text>();
            txt.font = UIFont.Resolve();
            if (txt.font != null) txt.material = txt.font.material;
            txt.text = content;
            txt.fontSize = size;
            txt.fontStyle = style;
            txt.color = color;
            txt.alignment = anchor;
            txt.horizontalOverflow = HorizontalWrapMode.Wrap;
            txt.verticalOverflow = VerticalWrapMode.Overflow;
            txt.raycastTarget = false;
            txt.lineSpacing = 1.30f;

            var rt = go.GetComponent<RectTransform>();
            rt.anchorMin = anchorMin;
            rt.anchorMax = anchorMax;
            rt.offsetMin = new Vector2(padding.x, padding.y);
            rt.offsetMax = new Vector2(-padding.x, -padding.y);
            _texts.Add(txt);
        }
    }
}

