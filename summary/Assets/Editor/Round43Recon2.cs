// 第 43 轮玩家视角侦查（临时工具）：一次性把三件事的现场证据拍下来
//   A. 电气情景：电源总闸（配电柜）旁北窗的多帧连拍（找「闪烁」来源），并与关闭排烟粒子后的对照帧比较
//   B. 电气情景：玩家静止、手里没拿灭火器时屏幕上的全部文字（找「距离灭火器」提示的常显来源）
//   C. 纸张情景：西墙 / 纸张火那一带的墙体射线扫描 + 多角度照片（找缺失的墙面）
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round43Recon2
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 320f;

        static readonly List<string> Log = new List<string>();
        static float _t0, _phaseT;
        static bool _entered;
        static int _phase, _shotIndex;
        static Transform _cam;

        static LanguageSelectScreen _lang;
        static StartScreen _start;
        static DrillScenario _scenario;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0; _shotIndex = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G43R] " + s); Log.Add(s); }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; _shotIndex = 0; Say("→ 阶段 " + p); }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play 模式"); }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Dump("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) _cam = cam.transform;

            switch (_phase)
            {
                case 0:
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    _lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                    Say("语言页=" + (_lang != null && _lang.IsOpen) + " 开始界面=" + (_start != null));
                    if (_lang != null) _lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;

                case 1:
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    if (_start != null) _start.ActuallyStart(0);   // 电气情景（配电柜起火）
                    Next(2);
                    return;

                case 2:
                {
                    // 站在出生点附近、离 01/02 号灭火器都远，静止不动，同时把屏幕文字全抓一遍
                    if (Time.realtimeSinceStartup - _phaseT < 3.2f) return;
                    DumpScreenTexts("电气情景 · 玩家静止、未点灭火器");
                    var pos = new Vector3(-5.55f, 1.58f, -2.55f);
                    var look = new Vector3(-3.65f, 1.85f, 3.95f);
                    ShotAt("g43_north_0.png", pos, look, 62f);
                    Next(3);
                    return;
                }

                case 3:
                {
                    // 北窗连拍 6 帧（0.5 秒一张），用来判断是否有明暗跳动
                    if (_shotIndex >= 6) { Next(4); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                    var pos = new Vector3(-4.60f, 1.62f, 2.30f);
                    var look = new Vector3(-3.65f, 1.85f, 3.95f);
                    ShotAt("g43_north_frame" + _shotIndex + ".png", pos, look, 60f);
                    _shotIndex++;
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 4:
                {
                    // 对照：一次性关掉北窗的排烟粒子与室内导流烟带，再连拍 3 帧
                    SetNorthSmoke(false);
                    if (_shotIndex >= 3) { Next(5); return; }
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    var pos = new Vector3(-4.60f, 1.62f, 2.30f);
                    var look = new Vector3(-3.65f, 1.85f, 3.95f);
                    ShotAt("g43_north_nosmoke" + _shotIndex + ".png", pos, look, 60f);
                    _shotIndex++;
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }

                case 5:
                {
                    // 对照：粒子关着、只关室内日光点光，看窗面亮度是否随光变化
                    SetNorthSmoke(false);
                    var lightGo = GameObject.Find("Daylight_North");
                    if (lightGo != null) lightGo.SetActive(false);
                    if (Time.realtimeSinceStartup - _phaseT < 1f) return;
                    var pos = new Vector3(-4.60f, 1.62f, 2.30f);
                    var look = new Vector3(-3.65f, 1.85f, 3.95f);
                    ShotAt("g43_north_nodlight.png", pos, look, 60f);
                    if (lightGo != null) lightGo.SetActive(true);
                    SetNorthSmoke(true);
                    Next(6);
                    return;
                }

                case 6:
                    // 切到纸张情景：重载场景，保证情景对象全部重置
                    Say("重载场景进入纸张情景");
                    SceneManager.LoadScene("FireSafetyOffice");
                    Next(7);
                    return;

                case 7:
                    if (Time.realtimeSinceStartup - _phaseT < 3.4f) return;
                    _lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                    if (_lang != null) _lang.Choose(DrillLocale.Lang.Chinese);
                    Next(8);
                    return;

                case 8:
                    if (Time.realtimeSinceStartup - _phaseT < 1.6f) return;
                    if (_start != null) _start.ActuallyStart(1);   // 纸张情景
                    Next(9);
                    return;

                case 9:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.2f) return;
                    // 正面看纸张火与西窗
                    ShotAt("g43_west_face.png", new Vector3(-4.35f, 1.55f, -4.62f),
                        new Vector3(-6.76f, 1.35f, -4.60f), 65f);
                    // 从房间北侧斜看纸张火背后（墙面）
                    ShotAt("g43_west_oblique.png", new Vector3(-5.20f, 1.60f, -2.60f),
                        new Vector3(-6.60f, 1.10f, -4.70f), 75f);
                    // 广角：整面西墙
                    ShotAt("g43_west_wide.png", new Vector3(-3.10f, 1.60f, -4.60f),
                        new Vector3(-6.76f, 1.45f, -4.60f), 88f);
                    Next(10);
                    return;
                }

                case 10:
                {
                    ScanWestWall();
                    ScanNorthWall();
                    Dump("完成");
                    return;
                }
            }
        }

        static void SetNorthSmoke(bool on)
        {
            foreach (var n in new[] { "WindowSmoke_North", "WindowFeed_North" })
            {
                var go = GameObject.Find(n);
                if (go != null && go.activeSelf != on) go.SetActive(on);
            }
            Say("北窗排烟粒子=" + on);
        }

        static void DumpScreenTexts(string tag)
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- 屏幕文字快照：" + tag + " ---");
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (c == null || !c.isActiveAndEnabled) continue;
                string owner = c.transform.root != null ? c.transform.root.name : c.name;
                foreach (var t in c.GetComponentsInChildren<Text>(false))
                {
                    if (t == null || string.IsNullOrWhiteSpace(t.text)) continue;
                    sb.AppendLine("  [" + owner + "/" + t.transform.parent.name + "] " + t.text.Replace("\n", " / "));
                }
            }
            var hud = UnityEngine.Object.FindFirstObjectByType<VRDrillHud>();
            if (hud != null)
                sb.AppendLine("  [VRDrillHud] hint=" + hud.CurrentHint + " | status=" + hud.CurrentStatus +
                              " | visible=" + hud.Visible + " suppressed=" + hud.suppressed);
            var scen = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (scen != null) sb.AppendLine("  [DrillScenario] hint=" + scen.CurrentHint);
            Say(sb.ToString());
        }

        static void ScanWestWall()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- 西墙射线扫描（从 x=-5.70 向 -X 打 3 米，未命中 = 缺口） ---");
            var miss = new List<string>();
            for (float y = 0.10f; y <= 2.90f; y += 0.10f)
            {
                for (float z = -6.00f; z <= -2.00f; z += 0.10f)
                {
                    var origin = new Vector3(-5.70f, y, z);
                    if (!Physics.Raycast(origin, Vector3.left, 3f, ~0, QueryTriggerInteraction.Ignore))
                        miss.Add(y.ToString("F1") + "@" + z.ToString("F1"));
                }
            }
            sb.AppendLine("  未命中格点数=" + miss.Count);
            if (miss.Count > 0) sb.AppendLine("  样例：" + string.Join(" | ", miss.GetRange(0, Math.Min(24, miss.Count))));
            float minY = 99f, maxY = -99f, minZ = 99f, maxZ = -99f;
            int n = 0;
            for (float y = 0.10f; y <= 2.90f; y += 0.10f)
                for (float z = -6.00f; z <= -2.00f; z += 0.10f)
                    if (!Physics.Raycast(new Vector3(-5.70f, y, z), Vector3.left, 3f, ~0, QueryTriggerInteraction.Ignore))
                    { n++; minY = Mathf.Min(minY, y); maxY = Mathf.Max(maxY, y); minZ = Mathf.Min(minZ, z); maxZ = Mathf.Max(maxZ, z); }
            if (n > 0) sb.AppendLine(string.Format("  缺口包围：y[{0:F1},{1:F1}] z[{2:F1},{3:F1}]", minY, maxY, minZ, maxZ));
            Say(sb.ToString());
        }

        static void ScanNorthWall()
        {
            var sb = new StringBuilder();
            sb.AppendLine("--- 北墙射线扫描（从 z=2.60 向 +Z 打 2 米，未命中 = 缺口） ---");
            int n = 0;
            var miss = new List<string>();
            for (float x = -4.90f; x <= -2.40f; x += 0.10f)
                for (float y = 0.10f; y <= 2.90f; y += 0.10f)
                {
                    if (!Physics.Raycast(new Vector3(x, y, 2.60f), Vector3.forward, 2f, ~0, QueryTriggerInteraction.Ignore))
                    { n++; if (miss.Count < 24) miss.Add(y.ToString("F1") + "@" + x.ToString("F1")); }
                }
            sb.AppendLine("  未命中格点数=" + n + (miss.Count > 0 ? " 样例：" + string.Join(" | ", miss) : ""));
            Say(sb.ToString());
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Say("× 截图无主相机：" + name); return; }
                var tr = cam.transform;
                var oldParent = tr.parent;
                var oldPos = tr.position;
                var oldRot = tr.rotation;
                if (oldParent != null) tr.SetParent(null, true);
                tr.position = pos;
                tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);

                int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture;
                var oldEye = cam.stereoTargetEye;
                float oldFov = cam.fieldOfView;
                if (fov > 1f) cam.fieldOfView = fov;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());

                // 采样窗洞区域的平均亮度，用来量化「闪不闪」
                Color avg = AverageRegion(tex, 0.42f, 0.30f, 0.16f, 0.40f);
                Say("截图 " + name + "  窗区平均亮度=" + avg.r.ToString("F3") + "/" + avg.g.ToString("F3") + "/" + avg.b.ToString("F3"));

                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget;
                cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos;
                tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
            }
            catch (Exception e) { Say("× 截图失败 " + name + "：" + e.Message); }
        }

        static Color AverageRegion(Texture2D tex, float x0, float y0, float x1, float y1)
        {
            int ax = Mathf.FloorToInt(tex.width * x0), ay = Mathf.FloorToInt(tex.height * y0);
            int bx = Mathf.FloorToInt(tex.width * x1), by = Mathf.FloorToInt(tex.height * y1);
            long r = 0, g = 0, b = 0, n = 0;
            for (int y = ay; y < by; y += 2)
                for (int x = ax; x < bx; x += 2)
                {
                    var c = tex.GetPixel(x, y);
                    r += (long)(c.r * 255); g += (long)(c.g * 255); b += (long)(c.b * 255); n++;
                }
            if (n == 0) return Color.black;
            return new Color(r / (float)n / 255f, g / (float)n / 255f, b / (float)n / 255f);
        }

        static void Dump(string reason)
        {
            Say("结束：" + reason);
            var sb = new StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            File.WriteAllText(Path.Combine(ShotDir, "_round43_recon.txt"), sb.ToString(), new UTF8Encoding(false));
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}
