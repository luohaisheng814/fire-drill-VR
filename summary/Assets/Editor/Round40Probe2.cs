#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 40 轮侦查二：把 ext02 周围 3 米内的家具/墙体列出来，弄清"柜子后面"到底是什么，
    /// 并tested 几个候选挂墙机位。
    /// </summary>
    public static class Round40Probe2
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";

        static DrillScenario _scenario;
        static bool _entered;
        static int _phase;
        static float _t0, _phaseT;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            _phase = 0; _entered = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G40b] " + s); }

        static void Tick()
        {
            if (!Application.isPlaying) return;
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; }
            float t = Time.realtimeSinceStartup - _t0;
            if (t > 40f) { EditorApplication.Exit(3); return; }
            if (t < 2.0f) return;

            if (_scenario == null)
            {
                _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                var cam = Camera.main;
                if (_scenario == null || cam == null) return;
                var rig = GameObject.Find("XR Origin") ?? GameObject.Find("XR Rig");
                if (rig != null) rig.transform.position = new Vector3(0f, -200f, 0f);
                cam.transform.SetParent(null, true);
                cam.fieldOfView = 72f;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                _scenario.StartFormal(-1, 1);

                Vector3 ext = new Vector3(-6.25f, 0.16f, -2.10f);
                Say("ext02 附近 3 米内的物体（按距离）：");
                var list = new List<(float d, string s)>();
                foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    var b = r.bounds;
                    if (b.size.x > 60f || b.size.y > 60f) continue;
                    float d = Vector3.Distance(b.center, ext);
                    if (d > 3.0f) continue;
                    list.Add((d, r.name + " @" + b.center.ToString("F2") + " 尺寸=" + b.size.ToString("F2")));
                }
                list.Sort((a, b) => a.d.CompareTo(b.d));
                for (int i = 0; i < Mathf.Min(list.Count, 26); i++) Say("  " + list[i].d.ToString("F2") + "  " + list[i].s);

                Say("相框/画 类物体：");
                foreach (var r in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    string n = r.name.ToLowerInvariant();
                    if (!(n.Contains("frame") || n.Contains("picture") || n.Contains("paint") || n.Contains("art") || n.Contains("poster") || n.Contains("photo"))) continue;
                    var b = r.bounds;
                    if (b.size.x > 60f) continue;
                    Say("  " + r.name + " @" + b.center.ToString("F2") + " 尺寸=" + b.size.ToString("F2"));
                }
                _phaseT = t; _phase = 1;
            }

            if (_phase == 1)
            {
                if (t - _phaseT < 1.2f) return;
                ShotAt("g40b_west_wall_north.png", new Vector3(-4.40f, 1.62f, -1.30f), new Vector3(-6.60f, 1.10f, -2.30f), 78f);
                ShotAt("g40b_west_wall_south.png", new Vector3(-4.40f, 1.62f, -3.30f), new Vector3(-6.60f, 1.00f, -2.60f), 78f);
                _phase = 2; _phaseT = t;
            }
            else if (_phase == 2)
            {
                if (t - _phaseT < 0.5f) return;
                ShotAt("g40b_candidate_wallA.png", new Vector3(-3.60f, 1.60f, -1.60f), new Vector3(-3.30f, 1.15f, -3.90f), 72f);
                ShotAt("g40b_candidate_wallB.png", new Vector3(-2.00f, 1.60f, -2.00f), new Vector3(-5.60f, 1.10f, -3.90f), 80f);
                Say("侦查二完成");
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
                _phase = 3; _phaseT = t;
            }
        }

        static void ShotAt(string name, Vector3 pos, Vector3 look, float fov)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) return;
                var tr = cam.transform;
                var oldParent = tr.parent; var oldPos = tr.position; var oldRot = tr.rotation;
                float oldFov = cam.fieldOfView; var oldEye = cam.stereoTargetEye;
                if (oldParent != null) tr.SetParent(null, true);
                tr.position = pos;
                tr.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
                int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                cam.fieldOfView = fov;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = null;
                cam.fieldOfView = oldFov;
                cam.stereoTargetEye = oldEye;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
                Say("截图 " + name);
            }
            catch (Exception e) { Say("截图失败 " + name + "：" + e.Message); }
        }
    }
}
#endif
