using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 引导页（온보딩 화면）：开始演练前的分页操作教学。
    ///   · 共 4 页：演练目标 / 桌面操作 / VR 手柄操作 / 安全考核规则；
    ///   · 面板锚定右手手柄正前方（与开始界面同一套跟随逻辑），朝向玩家；
    ///   · 最后一页按钮变为「开始演练」，直接开始演练；另有「跳过」回到开始界面。
    /// 仅在打开时显示，关闭后回到开始界面。
    /// </summary>
    public class OnboardingScreen : MonoBehaviour
    {
        public static OnboardingScreen Instance { get; private set; }

        [Header("引用（留空自动查找）")]
        public StartScreen startScreen;

        [Header("位置（手柄锚点优先，缺省回退相机前方）")]
        public Transform anchorHand;
        [Tooltip("沿手柄前向伸出的距离（米）；0.6 米刚好一眼读完四页正文")]
        public float handForwardDistance = 0.6f;
        [Tooltip("相对手柄向上抬的偏移（米）")]
        public float handUpOffset = 0.12f;
        public float canvasScale = 0.0009f;
        [Tooltip("无手柄锚点时相对头显的前方距离（米）")]
        public float fallbackDistance = 1.0f;
        [Tooltip("与相机的最远距离（米）：锚点再远也把面板拉回相机前方，防止面板跑到走廊尽头")]
        public float maxCameraDistance = 1.3f;
        [Tooltip("超距兜底：直接放到相机正前方该距离处")]
        public float cameraFallbackDistance = 1.0f;

        [Header("文案")]
        public string title = "操作教学 · 开始前必读";
        public string prevLabel = "上一步";
        public string nextLabel = "下一步";
        public string lastLabel = "开始演练";
        public string skipLabel = "跳过教学";

        [TextArea(4, 12)]
        public string[] pages = BuildDefaultPages();

        /// <summary>四页默认文案（编辑器装配时也调用它，保证场景里的文案与代码同步）。</summary>
        public static string[] BuildDefaultPages()
        {
            return new string[]
            {
                "【演练目标】\n" +
                "办公区电气起火。你需要在 3 分钟内完成整条流程：\n" +
                "① 找到红色灭火器（进入视线会高亮，地面有引导箭头）；\n" +
                "② 通过灭火器常识考核（5 题，答对 60% 才能拿起）；\n" +
                "③ 拿起 → 拔出保险销 → 按下压把 → 对准火焰根部喷射；\n" +
                "④ 火焰扑灭后点「继续往下」：烟雾扩散，蹲下潜行到大厅；\n" +
                "⑤ 大厅复燃，再灭一次火，走进「安全出口 EXIT」门内即通关。",

                "【桌面操作（无头显时使用）】\n" +
                "· 右键单击灭火器：弹出常识问答，答完即拿起灭火器；\n" +
                "· 左键单击：拔出保险销；\n" +
                "· 左键双击：按下压把（必须手持灭火器才生效）；\n" +
                "· 右键双击：开始 / 停止喷射；\n" +
                "· C 键：蹲下 / 站起（第二阶段浓烟中必须蹲下潜行）；\n" +
                "· P 键：放下灭火器；回车 / 空格：开始演练；问答按 1 / 2 / 3 作答。",

                "【VR 头显操作】\n" +
                "· 左摇杆移动：推杆方向就是行走方向（按头显朝向平移）；\n" +
                "· 右摇杆左右各瞬转 45°，转身后再推杆即朝新方向走；\n" +
                "· 蹲下：直接真人蹲下（镜头低于 1.15 米自动蹲行），也可按下左摇杆切换蹲 / 站；\n" +
                "· Grip 抓取灭火器与保险销（再扣一次松开）；\n" +
                "· Trigger 第一次按下拔保险销，再按一次压下压把持续喷射，松开扳机即停；\n" +
                "· 右手射线指向按钮扣 Trigger 点按；A 键开始 / 翻页 / 继续往下；问答用 A / B / X 选 A / B / C。",

                "【安全考核规则】\n" +
                "· 5 道灭火器常识题，每题 A / B / C 三个选项；\n" +
                "· 每题限时 10 秒，超时按答错处理；\n" +
                "· 答对 60%（3 / 5 题）即通过，才能拿起灭火器；\n" +
                "· 未通过可靠近灭火器重新按 Grip 抓取再次作答（桌面模式右键单击），答错会给出知识点讲解。\n\n" +
                "准备好了就点「开始演练」。"
            };
        }

        [Header("事件")]
        public UnityEvent onOpened = new UnityEvent();
        public UnityEvent onClosed = new UnityEvent();
        public UnityEvent onStartDrill = new UnityEvent();

        public bool IsOpen { get; private set; }
        public int PageIndex => _page;
        public int PageCount => pages != null ? pages.Length : 0;

        Canvas _canvas;
        CanvasGroup _group;
        RectTransform _panelRect;
        Text _titleText;
        Text _bodyText;
        Text _pageText;
        Text _nextLabelText;
        Text _prevLabelText;
        Button _prevBtn;
        Button _nextBtn;
        Transform _cam;
        int _page;

        private void Awake()
        {
            Instance = this;
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            BuildUI();
            _canvas.gameObject.SetActive(false);
        }

        private void Update()
        {
            if (!IsOpen) return;
            if (_canvas == null) return;
            if (_cam == null) _cam = Camera.main ? Camera.main.transform : null;

            Vector3 desired = _canvas.transform.position;
            if (anchorHand != null)
            {
                desired = anchorHand.position
                          + anchorHand.forward * handForwardDistance
                          + anchorHand.up * handUpOffset;
            }
            else if (_cam != null)
            {
                desired = _cam.position + _cam.forward * fallbackDistance
                          + _cam.up * -0.05f;
            }

            // 距离保护：锚点手柄在桌面模式下可能离相机很远，超距拉回相机正前方
            if (_cam != null)
                desired = StartScreen.ClampToCamera(desired, _cam.position, _cam.forward,
                    cameraFallbackDistance, maxCameraDistance);
            _canvas.transform.position = desired;

            Transform look = _cam != null ? _cam : anchorHand;
            if (look != null)
            {
                Quaternion target = Quaternion.LookRotation(_canvas.transform.position - look.position);
                _canvas.transform.rotation = Quaternion.Slerp(_canvas.transform.rotation, target,
                    Time.deltaTime * 4f);
            }
        }

        // ---------------- 打开 / 关闭 ----------------

        /// <summary>打开引导页，从第 1 页开始。此时暂时隐藏开始界面，避免两层面板按钮互相遮挡。</summary>
        public void Show()
        {
            if (IsOpen) return;
            if (_canvas == null) BuildUI();
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            if (startScreen != null) startScreen.SetVisible(false);

            _page = 0;
            RefreshPage();
            PlaceNow();
            _canvas.gameObject.SetActive(true);
            IsOpen = true;

            if (Application.isPlaying && _group != null)
            {
                _group.alpha = 0f;
                UIFX.Fade(this, _group, 0f, 1f, 0.25f);
            }
            else if (_group != null) _group.alpha = 1f;

            Play("Sounds/SFX/ui_click", 0.6f);
            onOpened?.Invoke();
            Debug.Log("[Onboarding] 引导页已打开，共 " + PageCount + " 页。");
        }

        /// <summary>关闭引导页；restoreStart=true 时恢复开始界面（「跳过教学」路径）。</summary>
        public void Close(bool restoreStart = true)
        {
            if (!IsOpen) return;
            IsOpen = false;
            if (_canvas != null) _canvas.gameObject.SetActive(false);
            if (restoreStart && startScreen != null && !startScreen.Started) startScreen.SetVisible(true);
            onClosed?.Invoke();
            Debug.Log("[Onboarding] 引导页已关闭。");
        }

        /// <summary>「跳过教学」按钮：关闭并回到开始界面。</summary>
        public void Skip() => Close(true);

        public void NextPage()
        {
            if (PageCount == 0) return;
            if (_page >= PageCount - 1)
            {
                StartDrill();
                return;
            }
            _page++;
            RefreshPage();
            Play("Sounds/SFX/ui_click", 0.4f);
        }

        public void PrevPage()
        {
            if (_page <= 0) return;
            _page--;
            RefreshPage();
            Play("Sounds/SFX/ui_click", 0.4f);
        }

        /// <summary>最后一页确认：关闭引导并开始演练。</summary>
        public void StartDrill()
        {
            Close(false);
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            if (startScreen != null)
            {
                if (!startScreen.Started) startScreen.StartGame();
            }
            else Debug.LogWarning("[Onboarding] 找不到 StartScreen，无法开始演练。");
            onStartDrill?.Invoke();
            Debug.Log("[Onboarding] 引导完成，开始演练。");
        }

        // ---------------- UI ----------------

        private void RefreshPage()
        {
            if (pages == null || pages.Length == 0) return;
            _page = Mathf.Clamp(_page, 0, pages.Length - 1);

            if (_bodyText != null) _bodyText.text = pages[_page];
            if (_pageText != null) _pageText.text = (_page + 1) + " / " + pages.Length;

            bool last = _page >= pages.Length - 1;
            if (_nextLabelText != null) _nextLabelText.text = last ? lastLabel : nextLabel;
            if (_prevBtn != null) _prevBtn.interactable = _page > 0;
            if (_prevLabelText != null)
                _prevLabelText.color = _page > 0 ? Color.white : new Color(1f, 1f, 1f, 0.35f);
        }

        private void BuildUI()
        {
            var go = new GameObject("Canvas_Onboarding");
            go.transform.SetParent(transform, false);
            _canvas = go.AddComponent<Canvas>();
            _canvas.renderMode = RenderMode.WorldSpace;
            _canvas.sortingOrder = 305;
            var rt = go.GetComponent<RectTransform>();
            rt.sizeDelta = new Vector2(1000, 760);
            rt.localScale = Vector3.one * canvasScale;
            go.AddComponent<CanvasScaler>();
            go.AddComponent<GraphicRaycaster>();
            go.AddComponent<TrackedDeviceGraphicRaycaster>();
            _group = go.AddComponent<CanvasGroup>();

            var panel = new GameObject("Panel", typeof(RectTransform), typeof(Image),
                typeof(VerticalLayoutGroup));
            panel.transform.SetParent(go.transform, false);
            panel.GetComponent<Image>().color = new Color(0.05f, 0.07f, 0.12f, 0.96f);
            var layout = panel.GetComponent<VerticalLayoutGroup>();
            layout.spacing = 10f;
            layout.padding = new RectOffset(30, 30, 22, 22);
            layout.childAlignment = TextAnchor.UpperCenter;
            layout.childControlWidth = true;
            layout.childControlHeight = false;
            layout.childForceExpandWidth = true;
            layout.childForceExpandHeight = false;

            _panelRect = panel.GetComponent<RectTransform>();
            _panelRect.sizeDelta = new Vector2(940, 720);
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;

            _titleText = MakeText(panel.transform, title, 42, new Color(1f, 0.85f, 0.2f), 62f);
            _bodyText = MakeText(panel.transform, "", 27, Color.white, 360f);
            _bodyText.alignment = TextAnchor.UpperLeft;

            _pageText = MakeText(panel.transform, "", 24, new Color(0.72f, 0.78f, 0.9f), 34f);

            var row = new GameObject("Buttons", typeof(RectTransform), typeof(HorizontalLayoutGroup));
            row.transform.SetParent(panel.transform, false);
            var hl = row.GetComponent<HorizontalLayoutGroup>();
            hl.spacing = 20f;
            hl.childAlignment = TextAnchor.MiddleCenter;
            hl.childControlWidth = true;
            hl.childControlHeight = false;
            hl.childForceExpandWidth = true;
            hl.childForceExpandHeight = false;
            var hrt = row.GetComponent<RectTransform>();
            hrt.sizeDelta = new Vector2(880, 92);
            var le = row.AddComponent<LayoutElement>();
            le.preferredHeight = 92f;

            _prevBtn = MakeButton(row.transform, prevLabel, 32, new Color(0.28f, 0.32f, 0.4f, 1f), 86f);
            _prevLabelText = _prevBtn.GetComponentInChildren<Text>();
            _prevBtn.onClick.AddListener(PrevPage);
            _nextBtn = MakeButton(row.transform, nextLabel, 32, new Color(0.12f, 0.55f, 0.30f, 1f), 86f);
            _nextLabelText = _nextBtn.GetComponentInChildren<Text>();
            _nextBtn.onClick.AddListener(NextPage);

            var skip = MakeButton(panel.transform, skipLabel, 24, new Color(0.35f, 0.28f, 0.2f, 1f), 56f);
            skip.onClick.AddListener(Skip);
            var sle = skip.GetComponent<LayoutElement>();
            if (sle != null) sle.preferredWidth = 260f;
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

        private void PlaceNow()
        {
            if (_canvas == null) return;
            if (_cam == null) _cam = Camera.main ? Camera.main.transform : null;

            Vector3 desired;
            if (anchorHand != null)
            {
                desired = anchorHand.position
                          + anchorHand.forward * handForwardDistance
                          + anchorHand.up * handUpOffset;
            }
            else if (_cam != null)
            {
                desired = _cam.position + _cam.forward * fallbackDistance + _cam.up * -0.05f;
            }
            else return;

            if (_cam != null)
                desired = StartScreen.ClampToCamera(desired, _cam.position, _cam.forward,
                    cameraFallbackDistance, maxCameraDistance);

            _canvas.transform.position = desired;
            Transform look = _cam != null ? _cam : anchorHand;
            if (look != null)
                _canvas.transform.rotation = Quaternion.LookRotation(desired - look.position);
        }

        private void Play(string path, float volume)
        {
            if (!Application.isPlaying) return;
            var clip = Resources.Load<AudioClip>(path);
            if (clip != null) AudioSource.PlayClipAtPoint(clip, transform.position, volume);
        }
    }
}
