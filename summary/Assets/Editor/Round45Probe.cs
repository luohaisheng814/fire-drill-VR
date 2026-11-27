// 第 45 轮玩家视角验收：
// ① 电源总闸旁北窗「一直闪」——玻璃改哑光低反射 + 设备区烟雾不再铺到窗前，
//    用 16 帧逐像素差分（网格块亮度来回翻转）证明窗面不再忽明忽暗；
// ② 答题选项每次随机 A/B/C（同一题重开后位置也不固定，答案判定跟着排列走）；
// ③ 烟雾整体加浓、第二阶段过道更浓（走廊三机位截图 + 参数断言）；
// ④ 以玩家视角走一遍（站在玩家眼睛高度拍照，不切俯视/自由相机）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round45Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 300f;
        const int DiffW = 800, DiffH = 450;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly List<Color32[]> Frames = new List<Color32[]>();

        static float _t0, _phaseT;
        static bool _entered;
        static int _phase, _shotIndex;
        static Transform _cam;
        static Camera _camera;
        static StartScreen _start;
        static DesktopDrillInput _desktop;
        static SmokeZone _stage1Smoke;
        static int[] _permBefore;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear(); Frames.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0; _shotIndex = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G45] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G45] × " + s); Log.Add("× " + s); Fails.Add(s); }
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
            if (!_entered)
            {
                _entered = true; _t0 = Time.realtimeSinceStartup;
                Say("进入 Play 模式（玩家视角）");
            }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) { _cam = cam.transform; _camera = cam; }

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
                    if (_start != null) _start.ActuallyStart(0);   // 电气情景（电源总闸就在北窗旁）
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.8f) return;
                    // ② 北窗：没有窗烟粒子
                    Check("北窗不再挂窗烟粒子（WindowSmoke_North / WindowFeed_North 都不存在）",
                        GameObject.Find("WindowSmoke_North") == null && GameObject.Find("WindowFeed_North") == null);

                    // ① 玻璃：哑光低反射，镜面高光与环境反射都关掉
                    var glassMat = AssetDatabase.LoadAssetAtPath<Material>("Assets/Materials/WindowGlassMat.mat");
                    Check("窗玻璃改哑光低反射（smoothness ≤0.15、高光/环境反射关）",
                        glassMat != null && glassMat.HasProperty("_Smoothness")
                        && glassMat.GetFloat("_Smoothness") <= 0.15f
                        && glassMat.IsKeywordEnabled("_SPECULARHIGHLIGHTS_OFF"),
                        glassMat != null && glassMat.HasProperty("_Smoothness")
                            ? ("smoothness=" + glassMat.GetFloat("_Smoothness").ToString("F2")) : "null");

                    // ① 设备区烟雾不再铺到窗前（北窗窗洞 x −4.30~−3.00、z 3.995、y 1.30~2.40）
                    var s1Go = GameObject.Find("Stage1Smoke");
                    _stage1Smoke = s1Go != null ? s1Go.GetComponent<SmokeZone>() : null;
                    Check("场景里能取到 Stage1Smoke", _stage1Smoke != null);
                    if (_stage1Smoke != null)
                    {
                        float minX = Mathf.Min(_stage1Smoke.start.x, _stage1Smoke.end.x) - _stage1Smoke.emitterShape.x * 0.5f;
                        float maxZ = Mathf.Max(_stage1Smoke.start.z, _stage1Smoke.end.z) + _stage1Smoke.emitterShape.z * 0.5f;
                        Check("设备区烟雾不再铺到北窗窗前（x 最左 ≥ −3.00、z 最远 ≤ 3.60）",
                            minX >= -3.00f && maxZ <= 3.60f,
                            "x 最左 " + minX.ToString("F2") + "、z 最远 " + maxZ.ToString("F2"));
                        Check("设备区烟雾加浓（发射点 ≥12 / 每秒 ≥34 / 浓度 ≥1.25）",
                            _stage1Smoke.emitterCount >= 12 && _stage1Smoke.ratePerSecond >= 34f
                            && _stage1Smoke.alphaScale >= 1.25f,
                            "发射点 " + _stage1Smoke.emitterCount + "，每秒 " + _stage1Smoke.ratePerSecond.ToString("F0")
                            + "，浓度 " + _stage1Smoke.alphaScale.ToString("F2"));

                        // 复现玩家场景：设备起火、烟已经充满房间，再去看窗
                        _stage1Smoke.Begin();
                        Say("已启动设备区烟雾（复现「起火后有烟」时的窗前画面）");
                    }
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 5.0f) return;   // 等烟充满
                    Frames.Clear();
                    Say("开始北窗 16 帧逐像素差分（烟已充满，玩家站在窗前 2 米）");
                    Next(4);
                    return;
                }

                case 4:
                {
                    if (Frames.Count >= 16) { Next(5); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 0.18f) return;
                    var px = Shot("g45v_north_" + Frames.Count + ".png",
                        new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f, DiffW, DiffH);
                    if (px != null) Frames.Add(px);
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 5:
                {
                    if (Frames.Count < 16) { Fail("北窗连拍帧数不足：" + Frames.Count); Next(6); return; }
                    float meanDiff, maxBlockAmp; int flickerBlocks, blockCount; string worstBlock;
                    AnalyzeFlicker(Frames, out meanDiff, out maxBlockAmp, out flickerBlocks, out blockCount, out worstBlock);
                    Check("① 北窗窗面 16 帧没有闪烁（网格块亮度不来回翻转、幅度 <2%）",
                        flickerBlocks == 0 && maxBlockAmp < 0.02f,
                        "翻转块 " + flickerBlocks + "/" + blockCount + "，最大块幅 " + (maxBlockAmp * 100f).ToString("F2")
                        + "% ，相邻帧平均差 " + (meanDiff * 100f).ToString("F2") + "%，最差块 " + worstBlock);
                    Shot("g45v_north_final.png", new Vector3(-4.60f, 1.62f, 2.30f),
                        new Vector3(-3.65f, 1.85f, 3.95f), 60f, 1600, 900);
                    // 电源总闸与窗的同框（用户投诉的实际视角）
                    Shot("g45v_north_switch.png", new Vector3(-3.10f, 1.62f, 1.60f),
                        new Vector3(-3.30f, 1.60f, 3.90f), 65f, 1600, 900);
                    Next(6);
                    return;
                }

                case 6:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    // ② 答题选项随机
                    var pre = PreTrainingQuiz.Instance;
                    Check("场景里能取到事前教育面板", pre != null);
                    if (pre == null) { Next(9); return; }
                    pre.Open(_cam, null, 1);
                    Next(7);
                    return;
                }

                case 7:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    var pre = PreTrainingQuiz.Instance;
                    Check("事前教育面板已打开", pre != null && pre.IsOpen);
                    if (pre == null || !pre.IsOpen) { Next(9); return; }

                    _permBefore = (int[])pre.CurrentPermutation.Clone();
                    var q = pre.questions[pre.QuestionIndex];
                    int correctBtn = pre.ButtonIndexOfCorrect();
                    Check("当前题的正确项落在某个按钮上（0~2）", correctBtn >= 0 && correctBtn <= 2,
                        "正确按钮=" + ((char)('A' + correctBtn)) + "，排列=" + Snap(_permBefore));

                    // 界面上 A/B/C 的文字必须按排列显示（不是原始固定顺序）
                    string labelA = FindOptionLabel("A. ");
                    string expectA = "A. " + SafeOpt(q.optionsCn, _permBefore[0]);
                    Check("A/B/C 显示顺序跟着随机排列走（A 行 = 题库第 " + _permBefore[0] + " 项）",
                        !string.IsNullOrEmpty(labelA) && labelA == expectA,
                        "界面「" + labelA + "」 / 期望「" + expectA + "」");

                    int before = pre.CorrectCount;
                    pre.AnswerOption(correctBtn);
                    Check("点「正确按钮」判为答对（判定跟着排列，而不是固定下标）",
                        pre.CorrectCount == before + 1, "答对数 " + before + "→" + pre.CorrectCount);
                    Next(8);
                    return;
                }

                case 8:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    var pre = PreTrainingQuiz.Instance;
                    if (pre == null) { Next(9); return; }
                    // 重开同一题：排列必须变（不能每局都固定 ABC）
                    pre.Close();
                    pre.Open(_cam, null, 1);
                    int[] after = (int[])pre.CurrentPermutation.Clone();
                    bool same = after[0] == _permBefore[0] && after[1] == _permBefore[1] && after[2] == _permBefore[2];
                    Check("同一题重开后选项顺序会重新随机（不是固定 A/B/C）", !same,
                        "上次 " + Snap(_permBefore) + " → 这次 " + Snap(after));
                    Say("重开后 A 行文字 = " + FindOptionLabel("A. "));
                    pre.Close();
                    Next(9);
                    return;
                }

                case 9:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                    // ③ 第二阶段过道浓烟
                    var flow = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
                    Check("场景里能取到 StageTwoFlow", flow != null);
                    if (flow == null) { Next(11); return; }
                    Check("第二阶段开启烟雾（useSmoke = true）", flow.useSmoke);
                    var corridor = flow.smoke;
                    Check("第二阶段过道有独立烟区", corridor != null);
                    if (corridor != null)
                    {
                        Check("第二阶段过道烟浓度（发射点 ≥13 / 每秒 ≥34 / 浓度 ≥1.15）",
                            corridor.emitterCount >= 13 && corridor.ratePerSecond >= 34f && corridor.alphaScale >= 1.15f,
                            "发射点 " + corridor.emitterCount + "，每秒 " + corridor.ratePerSecond.ToString("F0")
                            + "，浓度 " + corridor.alphaScale.ToString("F2") + "，粒子 " + corridor.particleSize.ToString("F2") + " 米");
                        corridor.Begin();
                    }
                    Next(10);
                    return;
                }

                case 10:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 4.0f) return;
                    var flow = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
                    var corridor = flow != null ? flow.smoke : null;
                    Check("第二阶段烟区已在播放", corridor != null && corridor.Active,
                        corridor != null ? ("Active=" + corridor.Active + "，发射点 " + corridor.EmitterCount) : "null");
                    // 走廊三机位：入口、中段、出口门前（走道烟的可见度）
                    Shot("g45v_corridor_enter.png", new Vector3(0.20f, 1.62f, 0.90f), new Vector3(4.60f, 1.55f, 1.05f), 75f, 1600, 900);
                    Shot("g45v_corridor_mid.png", new Vector3(2.10f, 1.62f, 0.85f), new Vector3(5.20f, 1.50f, 1.10f), 80f, 1600, 900);
                    Shot("g45v_corridor_door.png", new Vector3(4.10f, 1.62f, 1.05f), new Vector3(6.10f, 1.45f, 1.10f), 80f, 1600, 900);
                    Shot("g45v_lobby_smoke.png", new Vector3(-2.90f, 1.62f, 1.90f), new Vector3(3.20f, 1.55f, -0.60f), 80f, 1600, 900);
                    Next(11);
                    return;
                }

                case 11:
                    Finish("完成");
                    return;
            }
        }

        static string Snap(int[] p) { return p == null ? "null" : ("" + p[0] + p[1] + p[2]); }
        static string SafeOpt(string[] a, int i) { return a != null && i >= 0 && i < a.Length ? a[i] : ""; }

        static string FindOptionLabel(string prefix)
        {
            var pre = PreTrainingQuiz.Instance;
            if (pre == null) return "";
            foreach (var t in pre.GetComponentsInChildren<Text>(false))
                if (t != null && t.text != null && t.text.StartsWith(prefix)) return t.text;
            return "";
        }

        // 窗口区域（中央偏上）切成网格块，逐块求平均亮度序列：
        // 「闪烁」= 某块亮度来回翻转（≥2 次反向）且幅度明显，而不是整幅画面随火光缓慢变暗。
        static void AnalyzeFlicker(List<Color32[]> frames, out float meanDiff,
            out float maxBlockAmp, out int flickerBlocks, out int blockCount, out string worstBlock)
        {
            const int gx = 8, gy = 6;
            // 只框窗面本身（截图里窗框横跨 x≈0.29~0.67、纵向 0.10~0.87），
            // 右下角的火苗/火光不进这个框，避免把「火在烧」算成「窗户在闪」。
            float x0 = 0.32f, x1 = 0.63f, y0 = 0.18f, y1 = 0.80f;
            int w = DiffW, h = DiffH;
            meanDiff = 0f; maxBlockAmp = 0f; flickerBlocks = 0; blockCount = gx * gy;
            worstBlock = "";

            // 相邻帧整体平均差
            for (int f = 1; f < frames.Count; f++)
            {
                var a = frames[f - 1]; var b = frames[f];
                long sum = 0;
                for (int i = 0; i < a.Length; i += 17)
                    sum += Mathf.Abs((int)a[i].r - b[i].r) + Mathf.Abs((int)a[i].g - b[i].g) + Mathf.Abs((int)a[i].b - b[i].b);
                meanDiff += (float)sum / (3f * 255f * (a.Length / 17));
            }
            meanDiff /= Mathf.Max(1, frames.Count - 1);

            int ax = Mathf.FloorToInt(w * x0), bx = Mathf.FloorToInt(w * x1);
            int ay = Mathf.FloorToInt(h * (1f - y1)), by = Mathf.FloorToInt(h * (1f - y0));
            int cw = Mathf.Max(1, (bx - ax) / gx), ch = Mathf.Max(1, (by - ay) / gy);

            for (int byi = 0; byi < gy; byi++)
            {
                for (int bxi = 0; bxi < gx; bxi++)
                {
                    var seq = new List<float>(frames.Count);
                    foreach (var fr in frames)
                    {
                        float sum = 0f; int n = 0;
                        for (int y = ay + byi * ch; y < ay + (byi + 1) * ch; y += 3)
                            for (int x = ax + bxi * cw; x < ax + (bxi + 1) * cw; x += 3)
                            {
                                var c = fr[y * w + x];
                                sum += (c.r + c.g + c.b) / (3f * 255f); n++;
                            }
                        seq.Add(n > 0 ? sum / n : 0f);
                    }
                    float mn = seq[0], mx = seq[0];
                    float prevStep = 0f; int flips = 0;
                    for (int i = 1; i < seq.Count; i++)
                    {
                        float st = seq[i] - seq[i - 1];
                        mn = Mathf.Min(mn, seq[i]); mx = Mathf.Max(mx, seq[i]);
                        if (Mathf.Abs(st) > 0.012f)
                        {
                            if (Mathf.Abs(prevStep) > 0.012f && Mathf.Sign(st) != Mathf.Sign(prevStep)) flips++;
                            prevStep = st;
                        }
                    }
                    float amp = mx - mn;
                    if (amp > maxBlockAmp) { maxBlockAmp = amp; worstBlock = "(" + bxi + "," + byi + ")"; }
                    if (flips >= 2 && amp > 0.02f) flickerBlocks++;
                }
            }
        }

        static void Finish(string tag)
        {
            try
            {
                File.WriteAllText(Path.Combine(ShotDir, "_round45_probe.txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G45] 报告写入失败：" + e.Message); }
            Debug.Log("[G45] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }

        static Color32[] Shot(string name, Vector3 pos, Vector3 lookAt, float fov = 0f, int w = 1600, int h = 900)
        {
            try
            {
                var tr = _cam;
                var oldPos = tr.position; var oldRot = tr.rotation; var oldParent = tr.parent;
                tr.SetParent(null, true);
                tr.position = pos; tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);

                var cam = _camera != null ? _camera : tr.GetComponent<Camera>();
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
                var px = tex.GetPixels32();
                float luma = AverageLuma(tex, 0.30f, 0.26f, 0.70f, 0.80f);
                Say("截图 " + name + "  画面平均亮度=" + luma.ToString("F3"));

                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
                return px;
            }
            catch (Exception e) { Fail("截图失败 " + name + "：" + e.Message); return null; }
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
    }
}
