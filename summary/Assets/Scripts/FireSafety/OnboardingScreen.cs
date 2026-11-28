using UnityEngine;
using UnityEngine.Events;
using UnityEngine.UI;
using UnityEngine.XR.Interaction.Toolkit.UI;

namespace FireSafety
{
    /// <summary>
    /// 引导页（온보딩 화면）：开始演练前的分页操作教学。
    ///   · 共 6 页：两种身份总原则 / 电气 / 纸张 / 茶水间 / 两种火情情形 / 韩国官方五步；
    ///   · 面板锚定右手手柄正前方（与开始界面同一套跟随逻辑），朝向玩家；
    ///   · 最后一页按钮为「返回演练面板」，只关闭教学并恢复主面板，不直接开始演练。
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
        public string title = "操作教学 · 身份与情景判断";
        public string prevLabel = "上一步";
        public string nextLabel = "下一步";
        public string lastLabel = "返回演练面板";
        public string skipLabel = "跳过教学";

        [TextArea(6, 16)]
        public string[] pages = BuildDefaultPages();

        /// <summary>六页默认文案：总原则 + 电气/纸张/茶水间各一页 + 两种火情情形 + 复盘。</summary>
        public static string[] BuildDefaultPages()
        {
            return new string[]
            {
                "【先认清身份】\n\n" +
                "A 普通员工\n" +
                "  发现火灾第一动作：大声提醒 + 按墙上手动报警按钮；\n" +
                "  随后立即沿最近安全出口撤离。\n" +
                "  不尝试灭火，不折返取物，不在现场等待上级指示。\n\n" +
                "B 火灾初期应对责任人\n" +
                "  只有「初期小火 + 器材合适 + 视野清楚 + 身后退路安全」\n" +
                "  四条全部满足时，才可取墙上灭火器尝试初期灭火；\n" +
                "  任何一条不满足，或火势变大、烟雾变浓，立即停止并撤离。",

                "【电气设备起火（配电柜 / 插排）】\n\n" +
                "A 普通员工\n" +
                "  报警 → 撤离。不要碰配电柜，不要用水，不尝试灭火。\n\n" +
                "B 初期应对责任人\n" +
                "  先切断电源总闸（能安全接近时），\n" +
                "  再用 CO₂ 或 ABC 干粉灭火器对准火焰根部；\n" +
                "  不得用水，不得把手伸进带电设备；\n" +
                "  断电失败或火已蔓延到天花板，立即报警撤离。\n\n" +
                "逃生：走最近安全出口，走楼梯、不乘电梯，低姿沿墙撤离。",

                "【纸张 / 档案起火（A 类固体火）】\n\n" +
                "A 普通员工\n" +
                "  报警 → 撤离。纸张火看似小，但复燃快、烟大有毒，\n" +
                "  不承担灭火职责。\n\n" +
                "B 初期应对责任人\n" +
                "  用 ABC 干粉或水基灭火器，对准火焰根部，\n" +
                "  由近到远、由上到下扫射；\n" +
                "  灭后把纸堆翻开检查，防止阴燃复燃；\n" +
                "  火已烧到书架 / 天花板时立即撤离。\n\n" +
                "逃生：不穿过起火桌位，绕开燃烧区，走最近安全出口。",

                "【茶水间小家电 / 油类起火（F 类）】\n\n" +
                "A 普通员工\n" +
                "  报警 → 撤离。不要拔插头（可能已带电 / 烫伤），不要泼水。\n\n" +
                "B 初期应对责任人\n" +
                "  能安全断电时先断电；\n" +
                "  用湿化学（水基）或干粉灭火器从侧面覆盖，\n" +
                "  绝不能向油火直接泼水（会爆燃飞溅）；\n" +
                "  火苗窜上橱柜或烟太浓时立即撤离。\n\n" +
                "逃生：茶水间门口就是出口方向，低姿出门，不乘电梯。",

                "【两种火情情形（独立演练，不绑定三种起火物）】\n\n" +
                "情形 1 · 火势初起、烟雾开始蔓延\n" +
                "  A 普通员工：按报警按钮 → 立即撤离；\n" +
                "  B 初期应对责任人：按报警 → 灭火 → 撤离。\n\n" +
                "情形 2 · 火势已发展、烟雾弥漫\n" +
                "  A、B 都不允许灭主火：\n" +
                "  按报警按钮 → 低姿沿墙，绕开火源与浓烟，撤向东侧安全出口。\n\n" +
                "出口门规则（本办公室唯一安全出口在东侧，门外 10.4 米为集合点）\n" +
                "  情形 1 / 情形 2 门前都没有火；走近门扇右击即可开门，\n" +
                "  不要求先灭火、不要求答题、不需要其他条件。\n\n" +
                "两条逃生路线（都通向同一个东侧安全出口）\n" +
                "  路线 A（黄色主箭头）：沿大厅北侧 / 办公区外缘走，避开地板火；\n" +
                "  路线 B（青色备用箭头）：沿大厅南侧绕开火源，再从东墙折向出口。\n" +
                "  浓烟中哪条箭头看得清就走哪条；不要穿过火焰，不要折返取物。",

                "【韩国官方口径（소방청 / NFTC 101）与注意事项】\n\n" +
                "① 주변 사람에게 알린다（先提醒周围人，再按手动报警按钮）\n" +
                "② 대피방향을 결정한다（判断最近安全出口；走楼梯，绝不乘电梯）\n" +
                "③ 신속히 대피한다（低姿、贴墙、快速撤离，不折返取物）\n" +
                "④ 119 신고（撤到安全地带后再拨打 119）\n" +
                "⑤ 대피 후 인원 확인（到集合点清点人数）\n\n" +
                "【소화기 배치 기준 · 本办公室已按比例落实】\n" +
                "  · 小型灭火器：任意位置到最近一台的步行距离 ≤ 20 m；\n" +
                "  · 本办公室约 13.6×9.8 m，三台布点后最远 7.9 m（按 1:2.5 缩放≈19.8 m）；\n" +
                "  · 取用高度 ≤ 1.5 m，挂在看得见、拿得到的位置，前方留净空；\n" +
                "  · 每台挂「소화기」标识；电气区、纸张区、茶水间各一台。\n\n" +
                "※ 注意事项\n" +
                "  门把手很热时不要开门；烟雾中贴墙低姿，不折返取物；\n" +
                "  灭火器只用于初期小火；火势已发展（情形 2）立即撤离，绝不尝试灭火。"
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
        bool _hudHidden;

        /// <summary>教学阅读期间收起倒计时/生命值/火焰血条，避免压在正文上。</summary>
        void HideDrillHud()
        {
            if (_hudHidden) return;
            _hudHidden = true;
            foreach (var v in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None)) v.SetHudVisible(false);
            foreach (var f in FindObjectsByType<FireHealthBar>(FindObjectsSortMode.None)) f.SetHudVisible(false);
            foreach (var d in FindObjectsByType<DrillCountdown>(FindObjectsSortMode.None)) d.SetHudVisible(false);
            foreach (var h in FindObjectsByType<VRDrillHud>(FindObjectsSortMode.None)) h.SetVisible(false);
        }

        void RestoreDrillHud()
        {
            if (!_hudHidden) return;
            _hudHidden = false;
            foreach (var v in FindObjectsByType<PlayerVitals>(FindObjectsSortMode.None)) v.SetHudVisible(true);
            foreach (var f in FindObjectsByType<FireHealthBar>(FindObjectsSortMode.None)) f.SetHudVisible(true);
            foreach (var d in FindObjectsByType<DrillCountdown>(FindObjectsSortMode.None)) d.SetHudVisible(true);
        }

        public void Show()
        {
            if (IsOpen) return;
            if (_canvas == null) BuildUI();
            if (startScreen == null) startScreen = FindObjectOfType<StartScreen>();
            if (startScreen != null) startScreen.SetVisible(false);
            HideDrillHud();

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
            RestoreDrillHud();
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
                FinishTutorial();
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

        /// <summary>
        /// 第 49 轮：操作教学只负责讲清「身份 × 三种情景」的决断；
        /// 读完关闭面板并恢复「办公室消防知识演练」主面板，由玩家自己再选情景 / 情形。
        /// </summary>
        public void FinishTutorial()
        {
            Close(true);
            Debug.Log("[Onboarding] 操作教学阅读完毕，已返回办公室消防知识演练面板。");
        }

        /// <summary>兼容旧入口：现在也只会返回主面板，不再直接开始演练。</summary>
        public void StartDrill() => FinishTutorial();

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
            _panelRect.sizeDelta = new Vector2(940, 760);
            _panelRect.anchorMin = _panelRect.anchorMax = new Vector2(0.5f, 0.5f);
            _panelRect.pivot = new Vector2(0.5f, 0.5f);
            _panelRect.anchoredPosition = Vector2.zero;

            _titleText = MakeText(panel.transform, title, 40, new Color(1f, 0.85f, 0.2f), 58f);
            _bodyText = MakeText(panel.transform, "", 24, Color.white, 420f);
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
