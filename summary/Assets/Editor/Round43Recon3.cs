// 第 43 轮侦查（纸张情景近景）：关掉遮挡 HUD，从玩家正常站位拍西窗/纸张火，
// 并把「从室内穿过西窗洞看到的到底是什么」用 RaycastAll 逐层打出来。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round43Recon3
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 200f;

        static readonly List<string> Log = new List<string>();
        static float _t0, _phaseT;
        static bool _entered;
        static int _phase;
        static Transform _cam;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G43R3] " + s); Log.Add(s); }
        static void Next(int p) { _phase = p; _phaseT = Time.realtimeSinceStartup; Say("→ 阶段 " + p); }

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
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;
                    var start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    if (start != null) start.ActuallyStart(1);
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 3.2f) return;
                    HideObstructions();
                    ShotAt("g43b_west_window_close.png", new Vector3(-5.05f, 1.80f, -4.60f),
                        new Vector3(-6.76f, 1.85f, -4.60f), 52f);
                    ShotAt("g43b_paper_side.png", new Vector3(-4.55f, 1.60f, -3.55f),
                        new Vector3(-6.10f, 1.15f, -4.62f), 70f);
                    ShotAt("g43b_west_room.png", new Vector3(-2.20f, 1.62f, -3.10f),
                        new Vector3(-6.70f, 1.55f, -4.50f), 80f);
                    RayProbe();
                    Next(3);
                    return;
                }

                case 3:
                    Finish("完成");
                    return;
            }
        }

        static void HideObstructions()
        {
            try { DrillToast.HideNow(); } catch (Exception) { }
            foreach (var n in new[] { "DrillToast", "DrillCountdown", "DesktopHintHUD", "ScenarioKnowledgeBoards", "Canvas_PlayerVitals" })
            {
                var go = GameObject.Find(n);
                if (go != null && go.activeSelf) { go.SetActive(false); Say("隐藏 " + n); }
            }
        }

        static void RayProbe()
        {
            var origins = new[]
            {
                new Vector3(-5.30f, 1.85f, -4.60f),   // 正对窗洞中心
                new Vector3(-5.30f, 1.45f, -4.60f),   // 窗洞下沿
                new Vector3(-5.30f, 2.30f, -4.60f),   // 窗洞上沿
            };
            foreach (var o in origins)
            {
                var sb = new StringBuilder();
                sb.Append("RaycastAll 从 " + o.ToString("F2") + " 向 -X：");
                var hits = Physics.RaycastAll(o, Vector3.left, 6f, ~0, QueryTriggerInteraction.Collide);
                Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
                foreach (var h in hits)
                {
                    var r = h.collider.GetComponent<Renderer>();
                    sb.Append("\n    " + h.distance.ToString("F2") + "m  " + h.collider.name +
                              "  mat=" + (r != null && r.sharedMaterial != null ? r.sharedMaterial.name : "-") +
                              "  root=" + (h.collider.transform.root != null ? h.collider.transform.root.name : "-"));
                }
                Say(sb.ToString());
            }
        }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Say("× 无相机"); return; }
                var tr = cam.transform;
                var oldParent = tr.parent; var oldPos = tr.position; var oldRot = tr.rotation;
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
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget;
                cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
                Say("截图 " + name);
            }
            catch (Exception e) { Say("× 截图失败 " + name + "：" + e.Message); }
        }

        static void Finish(string reason)
        {
            Say("结束：" + reason);
            var sb = new StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            File.WriteAllText(Path.Combine(ShotDir, "_round43_recon3.txt"), sb.ToString(), new UTF8Encoding(false));
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}
