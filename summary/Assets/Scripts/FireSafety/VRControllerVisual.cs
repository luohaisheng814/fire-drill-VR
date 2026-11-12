using UnityEngine;

namespace FireSafety
{
    /// <summary>
    /// 头显里可见的手柄模型（视觉代理）。
    ///
    /// 真机接上后，如果手上什么都没有，玩家会「看不见自己的手在哪、抓没抓住」——
    /// 尤其在浓烟和火光里，这是最影响操作的体验问题。这里在运行时用三个基础几何拼一个
    /// 手柄轮廓（握把 + 前球 + 顶部条），挂在手部位姿节点下，跟随头显真实位姿移动。
    ///
    /// 设计约束：
    ///   · 全部基础几何体去掉 Collider，并放 Ignore Raycast 层 —— 否则会挡住自己的抓取射线 / 被自己抓到；
    ///   · 只在「头显接管」（VRDrillInput.DrillInputActive）时显示；桌面键鼠用 DesktopHand，不需要它；
    ///   · 材质走 URP/Lit + 自发光，浓烟与强火光下仍可辨认；无头显批处理也能正常生成（不依赖设备）。
    /// </summary>
    [DisallowMultipleComponent]
    public class VRControllerVisual : MonoBehaviour
    {
        public enum Shape { Grip, Ring }

        [Header("外观")]
        public HandSide side = HandSide.Right;
        [Tooltip("握把颜色（深灰）")]
        public Color bodyColor = new Color(0.15f, 0.16f, 0.19f);
        [Tooltip("发光点缀色（左手青 / 右手橙）")]
        public Color accentColor = new Color(1f, 0.55f, 0.2f);
        [Tooltip("自发光强度（浓烟里也看得见）")]
        public float emission = 0.55f;

        [Header("行为")]
        [Tooltip("桌面键鼠模式也显示（一般关闭）")]
        public bool showInDesktop = false;

        /// <summary>模型是否已生成。</summary>
        public bool Built { get; private set; }
        /// <summary>当前是否可见。</summary>
        public bool Visible { get; private set; }
        /// <summary>生成的几何体数量（自检断言用）。</summary>
        public int PartCount { get; private set; }
        /// <summary>模型根节点（自检与探针定位用）。</summary>
        public GameObject ModelRoot { get; private set; }

        const int IgnoreRaycastLayer = 2;
        Transform _holder;

        void Awake()
        {
            Build();
        }

        void OnEnable()
        {
            if (!Built) Build();
        }

        void Update()
        {
            SetVisible(showInDesktop || VRDrillInput.DrillInputActive);
        }

        /// <summary>生成手柄模型（幂等，可重复调用）。</summary>
        public void Build()
        {
            if (Built && ModelRoot != null) return;

            if (_holder == null)
            {
                var existing = transform.Find("ControllerModel");
                _holder = existing != null ? existing : new GameObject("ControllerModel").transform;
                _holder.SetParent(transform, false);
            }
            _holder.localPosition = Vector3.zero;
            _holder.localRotation = Quaternion.identity;
            ModelRoot = _holder.gameObject;

            // 清掉上一次的残留（重复装配时不叠加）
            for (int i = _holder.childCount - 1; i >= 0; i--)
            {
                var c = _holder.GetChild(i).gameObject;
                if (Application.isPlaying) Destroy(c); else DestroyImmediate(c);
            }

            PartCount = 0;
            var body = MakeMaterial(bodyColor, 0f);
            var accent = MakeMaterial(accentColor, emission);

            // 握把：沿 Z 轴的胶囊（直径 0.024，长 0.11）
            AddPrimitive(PrimitiveType.Capsule, "Grip", _holder,
                new Vector3(0f, 0f, 0.015f), Quaternion.Euler(90f, 0f, 0f),
                new Vector3(0.024f, 0.055f, 0.024f), body);

            // 前球：手柄顶端（射线从这里发出）
            AddPrimitive(PrimitiveType.Sphere, "Front", _holder,
                new Vector3(0f, 0.012f, 0.085f), Quaternion.identity,
                new Vector3(0.045f, 0.045f, 0.045f), accent);

            // 顶部条：握把上沿的发光带，用来区分左右手
            AddPrimitive(PrimitiveType.Cube, "TopBar", _holder,
                new Vector3(0f, 0.022f, 0.030f), Quaternion.identity,
                new Vector3(0.018f, 0.005f, 0.062f), accent);

            Built = true;
            Debug.Log("[VRHands] " + name + " 手柄模型已生成（" + PartCount + " 个几何体，"
                      + (side == HandSide.Left ? "左手" : "右手") + "）");
        }

        /// <summary>显示/隐藏（桌面模式下没有手柄模型的位置，所以藏起来）。</summary>
        public void SetVisible(bool value)
        {
            if (ModelRoot == null) return;
            if (ModelRoot.activeSelf != value) ModelRoot.SetActive(value);
            Visible = value;
        }

        Material MakeMaterial(Color color, float emissive)
        {
            var shader = Shader.Find("Universal Render Pipeline/Lit");
            if (shader == null) shader = Shader.Find("Standard");
            var mat = new Material(shader);
            mat.name = "ControllerMat_" + color.ToString();
            if (mat.HasProperty("_BaseColor")) mat.SetColor("_BaseColor", color);
            if (mat.HasProperty("_Color")) mat.SetColor("_Color", color);
            if (emissive > 0f)
            {
                mat.EnableKeyword("_EMISSION");
                var glow = color * emissive;
                if (mat.HasProperty("_EmissionColor")) mat.SetColor("_EmissionColor", glow);
            }
            return mat;
        }

        void AddPrimitive(PrimitiveType type, string partName, Transform parent,
                          Vector3 localPos, Quaternion localRot, Vector3 localScale, Material mat)
        {
            var go = GameObject.CreatePrimitive(type);
            go.name = partName;
            go.transform.SetParent(parent, false);
            go.transform.localPosition = localPos;
            go.transform.localRotation = localRot;
            go.transform.localScale = localScale;

            // 自己不能被自己抓到、不能挡住自己的射线
            var col = go.GetComponent<Collider>();
            if (col != null) { if (Application.isPlaying) Destroy(col); else DestroyImmediate(col); }
            SetLayerRecursive(go, IgnoreRaycastLayer);

            var r = go.GetComponent<Renderer>();
            if (r != null)
            {
                r.sharedMaterial = mat;
                r.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                r.receiveShadows = false;
            }
            PartCount++;
        }

        static void SetLayerRecursive(GameObject go, int layer)
        {
            go.layer = layer;
            for (int i = 0; i < go.transform.childCount; i++)
                SetLayerRecursive(go.transform.GetChild(i).gameObject, layer);
        }
    }
}
