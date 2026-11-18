using UnityEngine;
using UnityEngine.UI;

namespace FireSafety
{
    /// <summary>
    /// 第 30 轮：头显里的「操作帮助 / 输入诊断」面板。
    /// Y 键（左手柄 secondary）或 F5 打开/关闭；面板贴在视线前 0.95 米、略低于视线中心。
    /// 面板里同时给出：当前模式、手柄识别状态、输入源、实时扳机/握把数值 —— 手柄没反应时，
    /// 玩家在头显里就能看出是哪一层断了（设备没识别 / 有设备但按键值不动 / 只是没指到目标）。
    /// </summary>
    public class VRHelpPanel : MonoBehaviour
    {
        [Header("摆放")]
        public Transform head;
        public float distance = 0.95f;
        public float verticalOffset = -0.12f;

        [Header("内容")]
        public bool startsOpen = false;

        Canvas _canvas;
        Text _title;
        Text _body;
        bool _visible;
        float _nextRefresh;

        public bool Visible => _visible;

        void Awake()
        {
            Build();
            SetVisible(startsOpen);
        }

        void Build()
        {
            var go = new GameObject("VRHelpPanelCanvas", typeof(RectTransform));
            go.transform.SetParent(transform, false);

            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 350;

            var scaler = go.AddComponent<CanvasScaler>();
            scaler.dynamicPixelsPerUnit = 8f;

            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(900f, 800f);
            rt.localScale = Vector3.one * 0.00078f;

            var bgGo = new GameObject("Bg", typeof(RectTransform), typeof(Image));
            bgGo.transform.SetParent(go.transform, false);
            var bg = bgGo.GetComponent<Image>();
            bg.color = new Color(0.03f, 0.05f, 0.08f, 1f);
            bg.raycastTarget = false;
            Stretch(bgGo.GetComponent<RectTransform>(), 0f);

            var titleGo = new GameObject("Title", typeof(RectTransform), typeof(Text));
            titleGo.transform.SetParent(go.transform, false);
            _title = titleGo.GetComponent<Text>();
            _title.font = UIFont.Resolve();
            if (_title.font != null) _title.material = _title.font.material;
            _title.fontSize = 38;
            _title.fontStyle = FontStyle.Bold;
            _title.color = new Color(1f, 0.86f, 0.35f, 1f);
            _title.alignment = TextAnchor.MiddleLeft;
            _title.horizontalOverflow = HorizontalWrapMode.Overflow;
            _title.raycastTarget = false;
            var trt = titleGo.GetComponent<RectTransform>();
            trt.anchorMin = new Vector2(0f, 0.86f);
            trt.anchorMax = new Vector2(1f, 1f);
            trt.offsetMin = new Vector2(34f, 0f);
            trt.offsetMax = new Vector2(-34f, -8f);

            var bodyGo = new GameObject("Body", typeof(RectTransform), typeof(Text));
            bodyGo.transform.SetParent(go.transform, false);
            _body = bodyGo.GetComponent<Text>();
            _body.font = UIFont.Resolve();
            if (_body.font != null) _body.material = _body.font.material;
            _body.fontSize = 26;
            _body.color = new Color(0.92f, 0.95f, 1f, 1f);
            _body.alignment = TextAnchor.UpperLeft;
            _body.lineSpacing = 1.08f;
            _body.horizontalOverflow = HorizontalWrapMode.Wrap;
            _body.verticalOverflow = VerticalWrapMode.Overflow;
            _body.raycastTarget = false;
            var brt = bodyGo.GetComponent<RectTransform>();
            brt.anchorMin = new Vector2(0f, 0f);
            brt.anchorMax = new Vector2(1f, 0.86f);
            brt.offsetMin = new Vector2(34f, 22f);
            brt.offsetMax = new Vector2(-34f, -4f);

            Refresh();
        }

        static void Stretch(RectTransform rt, float pad)
        {
            rt.anchorMin = Vector2.zero;
            rt.anchorMax = Vector2.one;
            rt.offsetMin = new Vector2(pad, pad);
            rt.offsetMax = new Vector2(-pad, -pad);
        }

        public void Toggle()
        {
            SetVisible(!_visible);
        }

        public void SetVisible(bool visible)
        {
            _visible = visible;
            if (_canvas != null) _canvas.gameObject.SetActive(visible);
            if (visible) Refresh();
        }

        void LateUpdate()
        {
            if (!_visible) return;
            if (head == null && Camera.main != null) head = Camera.main.transform;
            if (head == null) return;

            Vector3 desired = head.position + head.forward * distance + head.up * verticalOffset;
            _canvas.transform.position = Vector3.Lerp(_canvas.transform.position, desired, Time.deltaTime * 8f);
            Vector3 look = _canvas.transform.position - head.position;
            if (look.sqrMagnitude > 0.0001f)
            {
                Quaternion rot = Quaternion.LookRotation(look.normalized, Vector3.up);
                _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, rot, Time.deltaTime * 8f);
            }

            if (Time.unscaledTime >= _nextRefresh)
            {
                _nextRefresh = Time.unscaledTime + 0.25f;
                Refresh();
            }
        }

        void Refresh()
        {
            if (_title == null || _body == null) return;

            string kind = RuntimeDeviceMode.HasInstance
                ? RuntimeDeviceMode.KindLabel(RuntimeDeviceMode.Instance.deviceKind)
                : (VRDrillInput.DrillInputActive ? "VR 头显" : "桌面");
            string control = RuntimeDeviceMode.HasInstance
                ? RuntimeDeviceMode.ControlLabel(RuntimeDeviceMode.Instance.controlMode)
                : (VRDrillInput.DrillInputActive ? "手柄摇杆 / Grip+Trigger" : "键盘鼠标");

            _title.text = "操作帮助 · " + kind;

            var snap = VRInputSource.Read(false);
            string guide = RuntimeDeviceMode.HasInstance ? RuntimeDeviceMode.Instance.Guidance : "";
            _body.text =
                "模式：" + kind + " · " + control + "\n"
                + VRInputSource.Diagnostics() + "\n"
                + (string.IsNullOrEmpty(guide) ? "" : "⚠ " + guide + "\n")
                + (snap.keyboardFallback ? "⚠ 手柄没识别到，正在用键盘兜底\n" : "")
                + "\n"
                + "左摇杆     移动（沿头显朝向，推满小跑）\n"
                + "右摇杆     左右 45° 瞬转\n"
                + "右摇杆按下 原地重定位\n"
                + "Grip       抓取 / 放下灭火器\n"
                + "Trigger    拔销 → 压把 → 喷射\n"
                + "           空手：指电源箱断电 / 指门开门\n"
                + "A          开始 / 翻页 / 继续 / 选 A\n"
                + "B / X      问答选 B / 选 C\n"
                + "Y          开关本面板（F5 同效）\n"
                + "左摇杆按下 蹲下 / 站起\n"
                // 已经给出「为什么连不上 / 下一步做什么」时，不再重复底部的通用排查段，
                // 否则面板高度装不下、文字会顶到背景外面。
                + (string.IsNullOrEmpty(guide)
                    ? "\n没反应就按顺序查：\n"
                      + "① 「设备」是否识别到左右手柄\n"
                      + "② 按 Trigger / Grip 看 T/G 数值是否跳动\n"
                      + "③ 数值会动但没反应 → 射线没指到目标"
                    : "");
        }
    }
}
