// 第 44 轮补充侦查：纸张情景「全向漏光」扫描。
// 玩家在房间里走动时，只要某个方位能透过墙体/天花板看到天空盒，屏幕上就会出现大片浅蓝。
// 这里站到几个玩家真会站的点位，每个点位朝 8 个方位拍一张，用「浅蓝 + 视线打不到任何碰撞体」判定真透空，
// 并把漏空的射线落到世界坐标上，直接指出是哪面墙/哪块天花板没封。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;

namespace FireSafety.EditorTools
{
    public static class Round44Gap
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string OutDir = "Logs/shots";
        const float HardTimeout = 200f;

        static readonly StringBuilder Report = new StringBuilder();
        static readonly List<string> Bad = new List<string>();

        // 玩家会在这些点位停留（纸张情景）
        static readonly Vector3[] Stands = new Vector3[]
        {
            new Vector3(-5.20f, 1.62f, -6.40f),   // 纸张房间中间
            new Vector3(-5.00f, 1.62f, -4.20f),   // 纸火桌旁
            new Vector3(-3.70f, 1.62f, -4.55f),   // 北面通道口
            new Vector3(-3.00f, 1.62f, -1.50f),   // 大厅
            new Vector3(-5.95f, 1.62f, -1.95f),   // Spawn01
            new Vector3(-3.35f, 1.62f, -5.55f),   // Spawn02
            new Vector3(5.90f, 1.62f, -1.20f),    // 茶水间
            new Vector3(6.60f, 1.62f, 1.10f)      // 走廊东端
        };
        const int Yaws = 8;

        static float _t0 = -1f, _phaseT;
        static bool _entered;
        static int _phase, _idx;
        static Transform _cam;

        public static void Run()
        {
            Directory.CreateDirectory(OutDir);
            Report.Clear(); Bad.Clear();
            _idx = 0; _phase = 0; _t0 = -1f; _entered = false; _cam = null;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G44GAP] " + s); Report.AppendLine(s); }

        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; }

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
                    {
                        var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                        if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    }
                    Next(1);
                    return;

                case 1:
                    if (Time.realtimeSinceStartup - _phaseT < 1.5f) return;
                    {
                        var start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                        if (start != null) start.ActuallyStart(1);   // 纸张情景
                    }
                    Next(2);
                    return;

                case 2:
                    if (Time.realtimeSinceStartup - _phaseT < 3.2f) return;
                    Next(3);
                    return;

                case 3:
                {
                    int total = Stands.Length * Yaws;
                    if (_idx >= total) { Next(4); return; }
                    int si = _idx / Yaws, yi = _idx % Yaws;
                    Shoot(Stands[si], yi * 45f, si, yi);
                    _idx++;
                    return;
                }

                case 4:
                    Dump("完成");
                    return;
            }
        }

        static void Shoot(Vector3 pos, float yaw, int si, int yi)
        {
            var tr = _cam;
            var oldPos = tr.position; var oldRot = tr.rotation; var oldParent = tr.parent;
            tr.SetParent(null, true);
            tr.position = pos;
            tr.rotation = Quaternion.Euler(0f, yaw, 0f);

            var cam = tr.GetComponent<Camera>();
            const int w = 800, h = 450;
            var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
            var oldTarget = cam.targetTexture; var oldEye = cam.stereoTargetEye; var oldFov = cam.fieldOfView;
            cam.fieldOfView = 75f;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.targetTexture = rt;
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
            tex.Apply();

            // 浅蓝像素 + 视线无碰撞 才算真漏光
            var px = tex.GetPixels32();
            int sampled = 0, holes = 0, sky = 0;
            string hint = "";
            for (int y = 0; y < h; y += 8)
            {
                for (int x = 0; x < w; x += 8)
                {
                    var c = px[y * w + x];
                    float r = c.r / 255f, g = c.g / 255f, b = c.b / 255f;
                    if (!(b > 0.60f && (b - r) > 0.14f && g > 0.50f)) continue;
                    sky++; sampled++;
                    var ray = cam.ViewportPointToRay(new Vector3((x + 0.5f) / w, 1f - (y + 0.5f) / h, 0f));
                    var hits = Physics.RaycastAll(ray, 60f, ~0, QueryTriggerInteraction.Ignore);
                    if (hits.Length == 0)
                    {
                        holes++;
                        if (hint.Length < 120)
                        {
                            var p20 = ray.origin + ray.direction * 20f;
                            hint += string.Format(" →20m({0:F1},{1:F1},{2:F1})", p20.x, p20.y, p20.z);
                        }
                    }
                }
            }

            float holeRatio = sampled > 0 ? (float)holes / sampled : 0f;
            float skyRatio = (float)sky / ((w / 8) * (h / 8));
            string tag = string.Format("站{0}({1:F1},{2:F1},{3:F1}) yaw{4:F0} 天空{5:F1}% 真透空{6:F1}%",
                si, pos.x, pos.y, pos.z, yaw, skyRatio * 100f, holeRatio * 100f);
            Say(tag + (holes > 0 ? hint : ""));

            if (holes > 0)
            {
                Bad.Add(tag);
                string file = string.Format("g44gap_s{0}_y{1:F0}.png", si, yaw);
                File.WriteAllBytes(Path.Combine(OutDir, file), tex.EncodeToPNG());
            }

            UnityEngine.Object.Destroy(tex);
            cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
            cam.fieldOfView = oldFov;
            RenderTexture.active = null;
            RenderTexture.ReleaseTemporary(rt);
            tr.position = oldPos; tr.rotation = oldRot;
            if (oldParent != null) tr.SetParent(oldParent, true);
        }

        static void Dump(string tag)
        {
            try
            {
                Report.AppendLine("===== 汇总（" + tag + "）：漏光方向 " + Bad.Count + " 处 =====");
                foreach (var b in Bad) Report.AppendLine("× " + b);
                File.WriteAllText(Path.Combine(OutDir, "_round44_gap.txt"), Report.ToString());
                Debug.Log("[G44GAP] 报告已写入 Logs/shots/_round44_gap.txt，漏光 " + Bad.Count + " 处");
            }
            catch (Exception e) { Debug.LogError("[G44GAP] 写报告失败：" + e.Message); }
            EditorApplication.Exit(0);
        }
    }
}
