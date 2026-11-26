// 第 43 轮玩家视角验收：①开始界面没有中英切换按钮 ②配电柜旁北窗连拍不再明暗跳动
// ③纸张火背后墙面完整无洞口 ④「距离灭火器」只在点击/抓取时提示，静止时屏幕没有这条提示。
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
    public static class Round43Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 260f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly List<float> WindowLuma = new List<float>();

        static float _t0, _phaseT;
        static bool _entered;
        static int _phase, _shotIndex;
        static Transform _cam;
        static StartScreen _start;
        static DrillScenario _scenario;
        static DesktopDrillInput _desktop;

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

        static void Say(string s) { Debug.Log("[G43] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G43] × " + s); Log.Add("× " + s); Fails.Add(s); }
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
                    Check("启动仍是语言选择页（切语言入口保留在这里）", lang != null && lang.IsOpen);
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;
                }

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _desktop = UnityEngine.Object.FindFirstObjectByType<DesktopDrillInput>();
                    int langButtons = 0;
                    var startCanvas = _start != null ? _start.GetComponentInChildren<Canvas>(true) : null;
                    if (startCanvas != null)
                        foreach (var b in startCanvas.GetComponentsInChildren<Button>(true))
                            if (b != null && (b.gameObject.name.Contains("Btn_Language") || (b.transform.parent != null && b.transform.parent.name.Contains("Btn_Language"))) )
                                langButtons++;
                    Check("① 开始界面已无中英切换按钮（Btn_Language 数量 " + langButtons + "）", langButtons == 0 && GameObject.Find("Btn_Language") == null);
                    Check("开始界面两条训练入口仍在（未误删）",
                        _start != null && _start.buttonLabel.Contains("决策") && _start.tutorialLabel.Contains("操作流程"));
                    ShotAt("g43v_01_start_no_lang.png", new Vector3(0f, 1.58f, 3.35f), new Vector3(0f, 1.25f, 0.6f), 60f);
                    Next(2);
                    return;
                }

                case 2:
                    if (Time.realtimeSinceStartup - _phaseT < 1f) return;
                    if (_start != null) _start.ActuallyStart(0);   // 电气情景
                    Next(3);
                    return;

                case 3:
                {
                    // 静止在出生点旁边（离 01/02 号瓶都远），先抓屏幕文字：正常游玩时不该有「距离灭火器」提示
                    if (Time.realtimeSinceStartup - _phaseT < 3.6f) return;
                    String text = ScreenTexts();
                    Check("④ 静止未点灭火器时，屏幕上没有「距离灭火器」提示",
                        !text.Contains("距离灭火器") && !text.Contains("离瓶身") && !text.Contains("太远了"));
                    Check("④ 静止时也没有「距灭火器」答题行", !text.Contains("距灭火器"));
                    ShotAt("g43v_02_north_before.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f);
                    Next(4);
                    return;
                }

                case 4:
                {
                    // 北窗连拍 8 帧（0.45 秒一张），前 2 帧是相机摆好后的预热帧不计入统计，
                    // 后 6 帧量窗洞平均亮度，用来看窗面是否还在明暗跳动。
                    if (_shotIndex >= 8) { Next(5); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;
                    ShotAt("g43v_north_" + _shotIndex + ".png", new Vector3(-4.60f, 1.62f, 2.30f),
                        new Vector3(-3.65f, 1.85f, 3.95f), 60f, recordLuma: _shotIndex >= 2);
                    _shotIndex++;
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 5:
                {
                    if (WindowLuma.Count < 2) { Fail("窗亮度采样不足"); Next(6); return; }
                    float min = WindowLuma[0], max = WindowLuma[0], sum = 0f;
                    foreach (var v in WindowLuma) { min = Mathf.Min(min, v); max = Mathf.Max(max, v); sum += v; }
                    float avg = sum / WindowLuma.Count;
                    float amp = max - min;
                    Check("② 北窗连续 6 帧的平均亮度跳动 ≤ 2%（实际 " + (amp * 100f).ToString("F2") + "%）",
                        amp <= 0.02f, "平均 " + avg.ToString("F3") + " 最小 " + min.ToString("F3") + " 最大 " + max.ToString("F3"));
                    ShotAt("g43v_03_north_after.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f);
                    Next(6);
                    return;
                }

                case 6:
                {
                    // ④ 抓取拒绝提示的范围/冷却：直接对 01 号瓶的 ToggleGrab 调私有入口
                    var exts = UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None);
                    Extinguisher probe = null;
                    float best = 999f;
                    foreach (var e in exts)
                    {
                        if (e == null || e.grab == null) continue;
                        float d = Vector3.Distance(_cam.position, e.transform.position);
                        if (d < best) { best = d; probe = e; }
                    }
                    Check("场景里有可交互灭火器", probe != null);
                    if (probe != null)
                    {
                        int rejects = 0;
                        Action<float> counter = d => rejects++;
                        probe.grab.GrabRejected += counter;
                        var hand = new GameObject("PROBE_HAND").transform;
                        var hb = new HandBinding { side = HandSide.Right, attachPoint = hand };
                        hand.position = probe.transform.position + new Vector3(1.0f, 0f, 0f);   // 超出抓取范围但在 3 米提示范围内
                        SetPrivate(probe.grab, "_lastRejectHintAt", -10f);
                        InvokeReject(probe.grab, hb);
                        InvokeReject(probe.grab, hb);            // 冷却期内第二次不应再提示
                        Check("④ Grip 在瓶旁按下会提示一次距离", rejects == 1, "次数=" + rejects);
                        hand.position = probe.transform.position + new Vector3(9.0f, 0f, 0f);   // 9 米外
                        SetPrivate(probe.grab, "_lastRejectHintAt", -10f);
                        InvokeReject(probe.grab, hb);
                        Check("④ 离瓶 9 米时即使按 Grip 也不提示距离（其他时间不触发）", rejects == 1, "次数=" + rejects);
                        probe.grab.GrabRejected -= counter;
                        UnityEngine.Object.Destroy(hand.gameObject);
                    }
                    ShotAt("g43v_04_north_room.png", new Vector3(-1.20f, 1.60f, 1.20f), new Vector3(-3.65f, 1.70f, 3.95f), 78f);
                    Next(7);
                    return;
                }

                case 7:
                    Say("重载场景进入纸张情景");
                    SceneManager.LoadScene("FireSafetyOffice");
                    Next(8);
                    return;

                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.4f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(9);
                    return;
                }

                case 9:
                    if (Time.realtimeSinceStartup - _phaseT < 1.5f) return;
                    if (_start != null) _start.ActuallyStart(1);   // 纸张情景
                    Next(10);
                    return;

                case 10:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.2f) return;
                    HideToastAndBoards();
                    // ③ 从室内正对纸张火打射线：必须命中墙面，不能穿出去
                    bool hit = Physics.Raycast(new Vector3(-5.30f, 1.85f, -4.60f), Vector3.left,
                        1.6f, ~0, QueryTriggerInteraction.Ignore);
                    Check("③ 纸张火后方射线命中墙面（不再有窗洞）", hit);
                    bool hitLow = Physics.Raycast(new Vector3(-5.30f, 1.10f, -4.60f), Vector3.left,
                        1.6f, ~0, QueryTriggerInteraction.Ignore);
                    Check("③ 纸张火高度（1.10 米）射线同样命中墙面", hitLow);
                    ShotAt("g43v_05_paper_wall.png", new Vector3(-4.55f, 1.60f, -3.55f),
                        new Vector3(-6.10f, 1.15f, -4.62f), 70f);
                    ShotAt("g43v_06_paper_wall_face.png", new Vector3(-5.05f, 1.72f, -4.60f),
                        new Vector3(-6.76f, 1.70f, -4.60f), 55f);
                    ShotAt("g43v_07_paper_room.png", new Vector3(-2.20f, 1.62f, -3.10f),
                        new Vector3(-6.70f, 1.45f, -4.50f), 80f);
                    Next(11);
                    return;
                }

                case 11:
                    Finish("完成");
                    return;
            }
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

        static void InvokeReject(ToggleGrab grab, HandBinding hb)
        {
            var m = typeof(ToggleGrab).GetMethod("ReportGrabRejected", BindingFlags.NonPublic | BindingFlags.Instance);
            if (m == null) { Fail("ReportGrabRejected 不存在"); return; }
            m.Invoke(grab, new object[] { hb });
        }

        static void SetPrivate(object target, string field, object value)
        {
            var f = target.GetType().GetField(field, BindingFlags.NonPublic | BindingFlags.Instance);
            if (f != null) f.SetValue(target, value);
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f, bool recordLuma = false)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Fail("截图无主相机：" + name); return; }
                var tr = cam.transform;
                var oldParent = tr.parent; var oldPos = tr.position; var oldRot = tr.rotation;
                if (oldParent != null) tr.SetParent(null, true);
                tr.position = pos;
                tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);

                int w = 1600, h = 900;
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

                // 窗洞在画面中央偏上：采样 x 0.40~0.62 / y 0.26~0.66 的平均亮度
                float luma = AverageLuma(tex, 0.40f, 0.26f, 0.62f, 0.66f);
                if (recordLuma) WindowLuma.Add(luma);
                Say("截图 " + name + "  中央区平均亮度=" + luma.ToString("F3"));

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
            double sum = 0; int n = 0;
            for (int y = ay; y < by; y += 3)
                for (int x = ax; x < bx; x += 3)
                {
                    var c = tex.GetPixel(x, y);
                    sum += 0.299 * c.r + 0.587 * c.g + 0.114 * c.b;
                    n++;
                }
            return n == 0 ? 0f : (float)(sum / n);
        }

        static void Finish(string reason)
        {
            Say("结果 " + reason + "，失败数=" + Fails.Count);
            var sb = new StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("FAIL_COUNT=" + Fails.Count);
            sb.AppendLine("PROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1));
            File.WriteAllText(Path.Combine(ShotDir, "_round43_probe.txt"), sb.ToString(), new UTF8Encoding(false));
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
