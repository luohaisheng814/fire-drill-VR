// 第 44 轮侦查（Play 模式）：①确认西墙（纸张火那侧）到底哪一段没有实体墙
// ②确认北窗（电源总闸旁）窗前是否还有烟粒子在动 ③拍玩家视角证据图。
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.SceneManagement;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round44Recon2
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const string ReportPath = "Logs/shots/_round44_recon.txt";
        const float HardTimeout = 200f;
        static readonly float[] Heights = { 0.60f, 1.40f, 2.20f, 2.80f };

        static readonly List<string> Log = new List<string>();
        static readonly StringBuilder Sb = new StringBuilder();
        static float _t0, _phaseT;
        static bool _entered;
        static int _phase;
        static Transform _cam;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Sb.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[R44] " + s); Log.Add(s); Sb.AppendLine(s); }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; Say("→ 阶段 " + p); }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play"); }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish(); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) _cam = cam.transform;

            switch (_phase)
            {
                case 0:
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    var lang = Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.0f) return;
                    var start = Object.FindFirstObjectByType<StartScreen>();
                    if (start != null) start.ActuallyStart(0);
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.5f) return;
                    // 电气情景：电源总闸旁的北窗，先看带烟状态
                    ShotAt("g44v_north_smoke.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f);
                    var smoke = GameObject.Find("WindowSmoke_North");
                    var feed = GameObject.Find("WindowFeed_North");
                    Say("WindowSmoke_North=" + (smoke != null) + " active=" + (smoke != null && smoke.activeInHierarchy)
                        + "；WindowFeed_North=" + (feed != null) + " active=" + (feed != null && feed.activeInHierarchy));
                    if (feed != null) feed.SetActive(false);
                    if (smoke != null) smoke.SetActive(false);
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    ShotAt("g44v_north_nosomke.png", new Vector3(-4.60f, 1.62f, 2.30f), new Vector3(-3.65f, 1.85f, 3.95f), 60f);
                    Next(4);
                    return;
                }

                case 4:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.4f) return;
                    Say("重载进纸张情景");
                    SceneManager.LoadScene("FireSafetyOffice");
                    Next(5);
                    return;
                }

                case 5:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.2f) return;
                    var lang2 = Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang2 != null && lang2.IsOpen) lang2.Choose(DrillLocale.Lang.Chinese);
                    Next(6);
                    return;
                }

                case 6:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.6f) return;
                    var start = Object.FindFirstObjectByType<StartScreen>();
                    if (start != null) start.ActuallyStart(1);   // 纸张情景
                    Next(7);
                    return;
                }

                case 7:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 2.2f) return;
                    DumpWestObjects();
                    ScanWestProfile();
                    ShotAt("g44v_paper_west.png", new Vector3(-4.00f, 1.62f, -3.10f), new Vector3(-6.80f, 1.45f, -4.40f), 70f);
                    ShotAt("g44v_paper_side.png", new Vector3(-5.20f, 1.62f, -2.60f), new Vector3(-6.70f, 1.30f, -3.80f), 60f);
                    ShotAt("g44v_paper_room.png", new Vector3(-2.60f, 1.62f, -2.60f), new Vector3(-5.60f, 1.10f, -4.40f), 70f);
                    Next(8);
                    return;
                }

                case 8:
                    Finish();
                    return;
            }
        }

        static void Finish()
        {
            File.WriteAllText(ReportPath, Sb.ToString());
            Debug.Log("[R44] 报告写入 " + ReportPath);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }

        static void DumpWestObjects()
        {
            Say("===== 西侧（x<-6.2）对象清单 =====");
            var trs = Object.FindObjectsByType<Transform>(FindObjectsInactive.Include, FindObjectsSortMode.None);
            var seen = new HashSet<string>();
            foreach (var t in trs)
            {
                if (t == null) continue;
                var r = t.GetComponent<MeshRenderer>();
                if (r == null) continue;
                var b = r.bounds;
                if (b.min.x > -6.2f) continue;
                string key = t.name + "|" + b.min.ToString("F2");
                if (!seen.Add(key)) continue;
                Say(string.Format("{0,-40} active={1,-5} y=({2:F2}~{3:F2}) z=({4:F2}~{5:F2}) x=({6:F2}~{7:F2})",
                    t.name, t.gameObject.activeInHierarchy, b.min.y, b.max.y, b.min.z, b.max.z, b.min.x, b.max.x));
            }
        }

        static void ScanWestProfile()
        {
            Say("===== 西墙剖面扫描（从 x=-5.60 向 -X 打 2.4 米，4 个高度）=====");
            float gapStart = float.NaN, lastGap = 0f; int gapCount = 0;
            for (float z = -8f; z <= 4.001f; z += 0.25f)
            {
                bool any = false;
                string who = "";
                for (int i = 0; i < Heights.Length; i++)
                {
                    Vector3 from = new Vector3(-5.60f, Heights[i], z);
                    if (Physics.Raycast(from, Vector3.left, out RaycastHit hit, 2.4f, ~0, QueryTriggerInteraction.Ignore))
                    { any = true; who = hit.collider.name; }
                }
                if (!any)
                {
                    if (float.IsNaN(gapStart)) gapStart = z;
                    lastGap = z; gapCount++;
                }
                else if (!float.IsNaN(gapStart))
                {
                    Say(string.Format("  ✘ 缺口 z {0:F2}~{1:F2}（{2} 个采样点四层全空）", gapStart, lastGap, gapCount));
                    gapStart = float.NaN; gapCount = 0;
                }
            }
            if (!float.IsNaN(gapStart))
                Say(string.Format("  ✘ 缺口 z {0:F2}~{1:F2}（{2} 个采样点四层全空）", gapStart, lastGap, gapCount));
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f)
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
                Say("截图 " + name);
                Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
            }
            catch (System.Exception e) { Say("截图失败 " + name + "：" + e.Message); }
        }
    }
}
