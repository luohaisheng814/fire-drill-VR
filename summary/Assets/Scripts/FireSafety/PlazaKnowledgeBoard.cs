using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 28 轮：门外广场上的「消防演练知识墙」。
    /// 三块立式展板 + 一块地面喷涂字，玩家从安全出口走出来抬头就能复习：
    /// 灭火剂怎么选、灭火器四步、疏散与集合要点。
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

        [Header("内容")]
        public string wallTitle = "办公室消防演练 · 安全知识";
        public string[] panelTitles = { "一、灭火器要对路", "二、灭火器四步", "三、疏散与集合" };
        [System.NonSerialized] public string[][] panelLines;   // 代码内初始化（交错数组不支持序列化）
        public string floorText = "灭火器四步：提 · 拔 · 握 · 压　｜　疏散：低姿 · 湿毛巾 · 到集合点";

        Transform _root;
        readonly System.Collections.Generic.List<Text> _texts = new System.Collections.Generic.List<Text>();

        /// <summary>自检用：已经生成的文字块数量（标题 + 每块展板 5 行 + 地面字）。</summary>
        public int TextCount => _texts.Count;

        /// <summary>自检用：取三块展板的要点（编辑模式 Awake 不跑，这里保证内容已初始化）。</summary>
        public string[][] PanelLines()
        {
            EnsureContent();
            return panelLines;
        }

        void Awake()
        {
            EnsureContent();
            Build();
        }

        void OnDestroy()
        {
            if (_root == null) return;
            if (Application.isPlaying) Destroy(_root.gameObject);
            else DestroyImmediate(_root.gameObject);
        }

        void EnsureContent()
        {
            if (panelLines != null && panelLines.Length == 3) return;
            panelLines = new[]
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
                    "① 提：提起灭火器，站到上风口",
                    "② 拔：拔掉保险销（喷嘴别对人）",
                    "③ 握：握紧喷管，对准火焰根部",
                    "④ 压：压下压把，左右扫射推进",
                    "※ 灭完留守观察，防止复燃"
                },
                new[]
                {
                    "低姿弯腰、湿毛巾捂口鼻，别吸浓烟",
                    "沿绿色「安全出口」标志撤离，不乘电梯",
                    "到集合点清点人数，拨打 119 报警",
                    "门打不开先试门温，别盲目开门",
                    "※ 撤离时不折返取物，生命第一"
                }
            };
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
