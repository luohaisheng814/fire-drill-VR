// 第 44 轮玩家视角验收：①电源总闸旁北窗不再闪（窗里没有烟粒子、8 帧亮度稳定）
// ②纸张起火点背后那面墙补齐到天花板（火苗上方不再透空）③答题时不再弹灭火器距离提示
// ④灭火器上的答题环节已取消（点击即拿起、直接可拔销），结算页不再有常识考核行。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round44Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 240f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly List<float> WindowLuma = new List<float>();
        static float LastSkyRatio;
        static float LastHoleRatio;

        static float _t0, _phaseT;
        static bool _entered;
        static int _phase, _shotIndex;
        static Transform _cam;
        static StartScreen _start;
        static DesktopDrillInput _desktop;
        static Extinguisher _ext01;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear(); WindowLuma.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0; _shotIndex = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G44] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G44] × " + s); Log.Add("× " + s); Fails.Add(s); }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; _shotIndex = 0; Say("→ 阶段 " + p); }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play 模式"); }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) _cam = cam.transform;

            switch (_phase)
            {
                case 0:
                {
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;
                }

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _desktop = UnityEngine.Object.FindFirstObjectByType<DesktopDrillInput>();
                    Check("开始界面没有多余的语言按钮（切语言只在最前面的语言页）",
                        GameObject.Find("Btn_Language") == null);
                    if (_start != null) _start.ActuallyStart(0);   // 电气情景
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.6f) return;
                    // ② 北窗（电源总闸旁）里不该再有任何烟粒子在动
                    var smokeN = GameObject.Find("WindowSmoke_North");
                    var feedN = GameObject.Find("WindowFeed_North");
                    int smokeTotal = 0;
                    foreach (var ws in UnityEngine.Object.FindObjectsByType<WindowSmoke>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                        if (ws != null) smokeTotal++;
                    Check("② 北窗不再挂窗口排烟粒子（WindowSmoke_North 不存在）", smokeN == null);
                    Check("② 北窗不再挂室内导流烟带（WindowFeed_North 不存在）", feedN == null);
                    Check("② 全场只剩茶水间那扇窗还有排烟（WindowSmoke 数量 1）", smokeTotal == 1, "实际 " + smokeTotal);
                    ShotAt("g44v_north_0.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f, true);
                    _shotIndex = 1;
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (_shotIndex >= 8) { Next(4); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;
                    ShotAt("g44v_north_" + _shotIndex + ".png", new Vector3(-4.60f, 1.62f, 2.30f),
                        new Vector3(-3.65f, 1.85f, 3.95f), 60f, _shotIndex >= 2);
                    _shotIndex++;
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 4:
                {
                    float min = WindowLuma[0], max = WindowLuma[0], sum = 0f;
                    foreach (var v in WindowLuma) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v; }
                    float amp = max - min;
                    // 第 44 轮：玩家说的「闪烁」是窗面忽明忽暗（来回跳），不是全屋火光渐暗。
                    // 所以判据看振荡：相邻帧差分反复变号（≥2 次）或单帧跳变过大才算闪。
                    float maxStep = 0f, prevStep = 0f; int flips = 0;
                    for (int i = 1; i < WindowLuma.Count; i++)
                    {
                        float step = WindowLuma[i] - WindowLuma[i - 1];
                        maxStep = Mathf.Max(maxStep, Mathf.Abs(step));
                        if (Mathf.Abs(step) > 0.004f)
                        {
                            if (Mathf.Abs(prevStep) > 0.004f && Mathf.Sign(step) != Mathf.Sign(prevStep)) flips++;
                            prevStep = step;
                        }
                    }
                    Check("② 北窗连拍 8 帧没有来回闪烁（无窗烟粒子后窗面只随全屋火光渐变）",
                        flips <= 1 && maxStep <= 0.06f,
                        "总幅 " + (amp * 100f).ToString("F2") + "% 单帧最大 " + (maxStep * 100f).ToString("F2") +
                        "% 反向次数 " + flips + " 平均 " + (sum / WindowLuma.Count).ToString("F3"));
                    ShotAt("g44v_north_after.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f);
                    Next(5);
                    return;
                }

                case 5:
                {
                    // ④ 灭火器上的答题环节取消：站到 01 号瓶旁点击 → 直接拿起，不弹问答
                    var go = GameObject.Find("Extinguisher01");
                    if (go != null) _ext01 = go.GetComponent<Extinguisher>();
                    Check("场景里能取到 Extinguisher01", _ext01 != null);
                    if (_ext01 == null) { Next(6); return; }
                    Check("④ 灭火器已不再要求答题（quizRequired = false）", !_ext01.quizRequired);
                    Check("④ 问答面板组件已停用（不会自己弹出来）",
                        _ext01.quizPanel == null || !_ext01.quizPanel.enabled);

                    _cam.position = new Vector3(-1.30f, 1.62f, 3.30f);
                    _cam.rotation = Quaternion.LookRotation((new Vector3(-0.20f, 1.10f, 3.42f) - _cam.position).normalized, Vector3.up);
                    bool picked = _desktop != null && _desktop.TryPickupExtinguisher(_ext01);
                    Check("④ 玩家点击灭火器后直接拿起（不再先答 5 题）", picked && _ext01.grab.IsHeld);
                    Check("④ 拿起来不做题也能直接拔销", _ext01.CanPullPin);
                    Check("④ 拿起时问答面板没有弹出来",
                        _ext01.quizPanel == null || !_ext01.quizPanel.IsOpen);
                    if (_desktop != null) _ext01.grab.ReleaseExternal();   // 等同于按 P 放下
                    Next(6);
                    return;
                }

                case 6:
                {
                    // ③ 答题面板开着时，任何「还差 X 米」都不许弹（玩家截图里的遮挡问题）
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    var pre = UnityEngine.Object.FindFirstObjectByType<PreTrainingQuiz>();
                    Check("找到事前教育面板", pre != null);
                    if (pre == null) { Next(9); return; }
                    pre.Open(_cam, null, 1);
                    Next(7);
                    return;
                }

                case 7:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.4f) return;
                    Check("事前教育面板已打开", PreTrainingQuiz.Instance != null && PreTrainingQuiz.Instance.IsOpen);
                    Check("答题期间 ToggleGrab 判定为「答题中」（抓取输入被屏蔽）", ToggleGrab.QuizUiOpen());
                    DrillToast.HideNow();
                    if (_ext01 != null && _ext01.coach != null)
                        InvokePrivate(_ext01.coach, "OnGrabRejected", 2.0f);
                    Next(8);
                    return;
                }

                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;
                    string text = ScreenTexts();
                    Check("③ 答题面板开着时不会弹「手柄离瓶身还差 X 米」",
                        !text.Contains("还差") && !text.Contains("再靠近一点") && !text.Contains("抓取范围"));
                    var pre = PreTrainingQuiz.Instance;
                    if (pre != null) pre.Close();
                    Next(9);
                    return;
                }

                case 9:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    // 关掉答题后提示恢复正常（证明是「答题时屏蔽」而不是永久禁掉）
                    DrillToast.HideNow();
                    if (_ext01 != null && _ext01.coach != null)
                        InvokePrivate(_ext01.coach, "OnGrabRejected", 2.0f);
                    Next(10);
                    return;
                }

                case 10:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    string text = ScreenTexts();
                    Check("③ 关掉答题后距离提示恢复正常", text.Contains("还差") || text.Contains("抓取范围"));
                    Say("重载场景进入纸张情景");
                    SceneManager.LoadScene("FireSafetyOffice");
                    Next(11);
                    return;
                }

                case 11:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.4f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(12);
                    return;
                }

                case 12:
                    if (Time.realtimeSinceStartup - _phaseT < 1.5f) return;
                    if (_start != null) _start.ActuallyStart(1);   // 纸张情景
                    Next(13);
                    return;

                case 13:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.0f) return;
                    HideToastAndBoards();
                    // ① 纸张火背后那面墙：原来只有 1.75 米高，火苗上方整段透空
                    var patch = GameObject.Find("PaperWall_BackUpper");
                    Check("① 纸张火背后补了上半截墙（PaperWall_BackUpper 存在）", patch != null);
                    if (patch != null)
                    {
                        var r = patch.GetComponentInChildren<MeshRenderer>();
                        Bounds b = r != null ? r.bounds : new Bounds();
                        Check("① 补墙覆盖火苗上方 1.75~3.00 米、x -6.75~-4.75",
                            Mathf.Abs(b.min.y - 1.75f) < 0.2f && Mathf.Abs(b.max.y - 3.0f) < 0.2f
                            && b.min.x < -6.4f && b.max.x > -4.9f,
                            "y " + b.min.y.ToString("F2") + "~" + b.max.y.ToString("F2")
                            + " x " + b.min.x.ToString("F2") + "~" + b.max.x.ToString("F2"));
                    }
                    bool hitUp = Physics.Raycast(new Vector3(-5.71f, 2.40f, -4.20f), Vector3.back, 1.4f, ~0, QueryTriggerInteraction.Ignore);
                    Check("① 从火苗上方往墙打射线命中墙面（不再透空）", hitUp);
                    ShotAt("g44v_paper_back.png", new Vector3(-4.60f, 1.62f, -3.55f), new Vector3(-5.80f, 1.90f, -4.70f), 75f);
                    ShotAt("g44v_paper_room.png", new Vector3(-2.30f, 1.62f, -3.05f), new Vector3(-6.00f, 1.60f, -4.50f), 80f);
                    Check("① 纸张情景广角（朝火桌）看不到天空盒透空", LastHoleRatio <= 0.02f,
                        "天空色 " + (LastSkyRatio * 100f).ToString("F2") + "% 其中真透空 "
                        + (LastHoleRatio * 100f).ToString("F2") + "%");
                    ShotAt("g44v_paper_far.png", new Vector3(-2.60f, 1.62f, -1.60f), new Vector3(-5.90f, 1.70f, -4.60f), 70f);
                    Check("① 从大厅远看纸张房间看不到天空盒透空", LastHoleRatio <= 0.02f,
                        "天空色 " + (LastSkyRatio * 100f).ToString("F2") + "% 其中真透空 "
                        + (LastHoleRatio * 100f).ToString("F2") + "%");
                    ShotAt("g44v_paper_east.png", new Vector3(-4.30f, 1.62f, -3.30f), new Vector3(-2.20f, 1.60f, -5.00f), 75f);
                    Check("① 纸张房间隔断东半段上方已封实（朝南看不到天空）", LastHoleRatio <= 0.02f,
                        "天空色 " + (LastSkyRatio * 100f).ToString("F2") + "% 其中真透空 "
                        + (LastHoleRatio * 100f).ToString("F2") + "%");
                    Next(14);
                    return;
                }

                case 14:
                    Finish("完成");
                    return;
            }
        }

        static void Finish(string tag)
        {
            try
            {
                File.WriteAllText(Path.Combine(ShotDir, "_round44_probe.txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G44] 报告写入失败：" + e.Message); }
            Debug.Log("[G44] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }

        static void HideToastAndBoards()
        {
            try { DrillToast.HideNow(); } catch (Exception) { }
            foreach (var b in UnityEngine.Object.FindObjectsByType<PlazaKnowledgeBoard>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.gameObject.activeSelf) b.gameObject.SetActive(false);
            foreach (var b in UnityEngine.Object.FindObjectsByType<ScenarioKnowledgeBoards>(FindObjectsInactive.Include, FindObjectsSortMode.None))
                if (b != null && b.gameObject.activeSelf) b.gameObject.SetActive(false);
        }

        static string ScreenTexts()
        {
            var sb = new StringBuilder();
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                foreach (var t in c.GetComponentsInChildren<Text>(false))
                    if (t != null && !string.IsNullOrWhiteSpace(t.text)) sb.Append(t.text).Append(" | ");
            }
            var hud = UnityEngine.Object.FindFirstObjectByType<VRDrillHud>();
            if (hud != null) sb.Append(hud.CurrentHint).Append(" | ").Append(hud.CurrentStatus);
            var scen = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (scen != null) sb.Append(scen.CurrentHint);
            return sb.ToString();
        }

        static void InvokePrivate(object target, string method, params object[] args)
        {
            if (target == null) return;
            var m = target.GetType().GetMethod(method, BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) { Fail("找不到方法 " + method); return; }
            try { m.Invoke(target, args); } catch (Exception e) { Fail(method + " 调用失败：" + e.Message); }
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f, bool recordLuma = false)
        {
            try
            {
                var tr = _cam;
                var oldPos = tr.position; var oldRot = tr.rotation; var oldParent = tr.parent;
                tr.SetParent(null, true);
                tr.position = pos; tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);

                var cam = tr.GetComponent<Camera>();
                const int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture; var oldEye = cam.stereoTargetEye; float oldFov = cam.fieldOfView;
                if (fov > 1f) cam.fieldOfView = fov;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());

                float luma = AverageLuma(tex, 0.40f, 0.26f, 0.62f, 0.66f);
                LastSkyRatio = SkyRatio(tex);
                LastHoleRatio = SkyHoleRatio(tex, cam);
                if (LastSkyRatio > 0.004f) DiagnoseSky(tex, cam, name);
                if (recordLuma) WindowLuma.Add(luma);
                Say("截图 " + name + "  中央区平均亮度=" + luma.ToString("F3")
                    + "  天空色=" + (LastSkyRatio * 100f).ToString("F2") + "%"
                    + "  真透空=" + (LastHoleRatio * 100f).ToString("F2") + "%");

                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
            }
            catch (Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }

        static float AverageLuma(Texture2D tex, float x0, float y0, float x1, float y1)
        {
            int ax = Mathf.Clamp(Mathf.FloorToInt(tex.width * x0), 0, tex.width - 1);
            int ay = Mathf.Clamp(Mathf.FloorToInt(tex.height * y0), 0, tex.height - 1);
            int bx = Mathf.Clamp(Mathf.FloorToInt(tex.width * x1), ax + 1, tex.width);
            int by = Mathf.Clamp(Mathf.FloorToInt(tex.height * y1), ay + 1, tex.height);
            var px = tex.GetPixels(ax, ay, bx - ax, by - ay);
            float sum = 0f;
            for (int i = 0; i < px.Length; i++) sum += (px[i].r + px[i].g + px[i].b) / 3f;
            return px.Length > 0 ? sum / px.Length : 0f;
        }

        // 「天空色」像素占比：画面里出现成片的浅蓝（b 明显高于 r、g 也不低）就说明墙体/天花板透空。
        // 用来量化玩家视角有没有「看到天空盒」的缺口，比肉眼看截图更稳定。
        static float SkyRatio(Texture2D tex)
        {
            var px = tex.GetPixels32();
            int sky = 0;
            for (int i = 0; i < px.Length; i++)
            {
                float r = px[i].r / 255f, g = px[i].g / 255f, b = px[i].b / 255f;
                if (b > 0.60f && (b - r) > 0.14f && g > 0.50f) sky++;
            }
            return px.Length > 0 ? (float)sky / px.Length : 0f;
        }

        // 真正透空的口径：像素看起来是浅蓝「并且」沿这条视线打不到任何碰撞体。
        // 只按颜色判会把被室内冷色日光打亮的墙板（wall_small (5) 就是这样）误算成天空。
        static float SkyHoleRatio(Texture2D tex, Camera cam)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            int sampled = 0, holes = 0;
            for (int y = 0; y < h; y += 6)
            {
                for (int x = 0; x < w; x += 6)
                {
                    var c = px[y * w + x];
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    if (!(b > 0.60f && (b - r) > 0.14f && g > 0.50f)) continue;
                    sampled++;
                    var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / w, 1f - (y + 0.5f) / h, 0f));
                    if (Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Ignore).Length == 0) holes++;
                }
            }
            return sampled > 0 ? (float)holes / sampled : 0f;
        }

        // 对「天空色」像素反查：把像素换成 viewport 射线，看这条视线打到什么、或者干脆什么都没打到，
        // 定位透空到底是哪面墙/天花板没封住。
        static void DiagnoseSky(Texture2D tex, Camera cam, string name)
        {
            int w = tex.width, h = tex.height;
            var px = tex.GetPixels32();
            var hits = new Dictionary<string, int>();
            int sampled = 0, missed = 0; string missSample = "";
            for (int y = 0; y < h; y += 6)
            {
                for (int x = 0; x < w; x += 6)
                {
                    var c = px[y * w + x];
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    if (!(b > 0.60f && (b - r) > 0.14f && g > 0.50f)) continue;
                    sampled++;
                    if (sampled % 25 != 0) continue;   // 均匀抽 1/25，够定位了
                    var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / w, 1f - (y + 0.5f) / h, 0f));
                    var hh = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Ignore);
                    if (hh.Length == 0)
                    {
                        missed++;
                        if (missSample.Length < 200)
                            missSample += string.Format(" [{0:F1},{1:F1},{2:F1}]→({3:F2},{4:F2},{5:F2})",
                                ray.origin.x, ray.origin.y, ray.origin.z,
                                ray.direction.x, ray.direction.y, ray.direction.z);
                        continue;
                    }
                    System.Array.Sort(hh, (a, bb) => a.distance.CompareTo(bb.distance));
                    string key = hh[0].collider.name + "@" + hh[0].distance.ToString("F1") + "m";
                    hits[key] = hits.ContainsKey(key) ? hits[key] + 1 : 1;
                }
            }
            var sb = new StringBuilder();
            foreach (var kv in hits)
            {
                if (kv.Value >= 2) sb.Append(kv.Key).Append("×").Append(kv.Value).Append("  ");
            }
            Say("🔎 天空像素定位 " + name + "：采样 " + sampled + "，无命中 " + missed
                + (sb.Length > 0 ? "；命中分布 " + sb.ToString() : "")
                + (missed > 0 ? "；漏空射线" + missSample : ""));
        }
    }
}
