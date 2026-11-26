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
    /// 第 40 轮侦查：纸张情景（情景二）里灭火器和火源到底在哪一侧、隔了哪堵墙。
    /// 只拍玩家视角图 + 打印墙体/家具坐标，不做断言。
    /// </summary>
    public static class Round40Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";

        static DrillScenario _scenario;
        static StageTwoFlow _stageTwo;
        static Transform _cam;
        static int _phase;
        static float _t0, _phaseT;
        static bool _entered;
        static Extinguisher _ext02;
        static FireSource _paperFire;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            _phase = 0; _entered = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G40] " + s); }

        static void Tick()
        {
            if (!Application.isPlaying) return;
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play"); }
            float t = Time.realtimeSinceStartup - _t0;
            if (t > 40f) { EditorApplication.Exit(3); return; }
            if (t < 2.0f) return;

            if (_scenario == null)
            {
                _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                _stageTwo = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
                var go = GameObject.Find("Extinguisher02");
                if (go != null) _ext02 = go.GetComponent<Extinguisher>();
                _cam = Camera.main != null ? Camera.main.transform : null;
                if (_scenario == null || _cam == null) return;
                var rig = GameObject.Find("XR Origin") ?? GameObject.Find("XR Rig");
                if (rig != null) rig.transform.position = new Vector3(0f, -200f, 0f);
                _cam.SetParent(null, true);
                Camera.main.fieldOfView = 72f;
                Camera.main.stereoTargetEye = StereoTargetEyeMask.None;
                _scenario.StartFormal(-1, 1);      // 1 = 情景二（纸张）
                _paperFire = _scenario.deviceFire;
                Say("纸张情景已启动，火=" + (_paperFire != null ? _paperFire.transform.position.ToString("F2") : "null")
                    + " 瞄准点=" + (_paperFire != null ? _paperFire.AimPoint.ToString("F2") : "null"));
                if (_ext02 != null)
                    Say("02 号瓶=" + _ext02.transform.position.ToString("F2"));
                foreach (var w in UnityEngine.Object.FindObjectsByType<MeshRenderer>(FindObjectsSortMode.None))
                {
                    if (!w.name.StartsWith("Wall_") && !w.name.StartsWith("wall_") && !w.name.StartsWith("WindowWall")) continue;
                    Vector3 p = w.transform.position;
                    if (p.x > -8f && p.x < 8f && p.z > -9f && p.z < 5f)
                        Say("  墙体 " + w.name + " @" + p.ToString("F2") + " 尺寸=" + w.bounds.size.ToString("F2"));
                }
                _phaseT = t; _phase = 1;
            }

            if (_phase == 1)
            {
                if (t - _phaseT < 1.2f) return;
                ShotAt("g40_paper_spawn0_lookfire.png", new Vector3(-5.95f, 1.62f, -1.95f), new Vector3(-5.71f, 1.05f, -4.54f), 75f);
                ShotAt("g40_paper_spawn1_lookfire.png", new Vector3(-3.35f, 1.62f, -5.55f), new Vector3(-5.71f, 1.05f, -4.54f), 75f);
                _phase = 2; _phaseT = t;
            }
            else if (_phase == 2)
            {
                if (t - _phaseT < 0.5f) return;
                ShotAt("g40_paper_corridor_to_fire.png", new Vector3(-4.20f, 1.62f, -2.20f), new Vector3(-5.71f, 0.95f, -4.60f), 80f);
                ShotAt("g40_paper_ext02_view.png", new Vector3(-4.60f, 1.62f, -2.20f), new Vector3(-6.40f, 0.60f, -2.05f), 70f);
                _phase = 3; _phaseT = t;
            }
            else if (_phase == 3)
            {
                if (t - _phaseT < 0.5f) return;
                ShotAt("g40_paper_wide.png", new Vector3(-2.60f, 1.75f, -1.40f), new Vector3(-5.90f, 0.90f, -3.60f), 90f);
                Say("侦查完成");
                EditorApplication.update -= Tick;
                EditorApplication.Exit(0);
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
