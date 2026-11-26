using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>第 40 轮补探（Play 模式）：手持 02 号瓶后打印 nozzle 与各火源距离，定位「距离火源 11.3 米」的来源。</summary>
    public static class Round40Probe7
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        static float _t0 = -1f;
        static bool _done;
        static readonly List<string> Lines = new List<string>();

        public static void Run()
        {
            _t0 = -1f; _done = false; Lines.Clear();
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Tick()
        {
            if (_done) return;
            if (!Application.isPlaying) return;
            if (_t0 < 0f) _t0 = Time.realtimeSinceStartup;
            if (Time.realtimeSinceStartup - _t0 < 4f) return;   // 等运行时装配

            _done = true;
            var cam = Camera.main;
            if (cam == null) { Say("没有主相机"); Finish(1); return; }
            cam.transform.SetParent(null, true);
            cam.transform.position = new Vector3(-5.71f, 1.55f, -2.35f);
            cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, -0.35f, -1f), Vector3.up);
            cam.fieldOfView = 72f;

            var scenario = Object.FindFirstObjectByType<DrillScenario>();
            scenario.StartFormal(-1, 1);
            System.Threading.Thread.Sleep(300);

            Extinguisher ext = null;
            foreach (var e in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e != null && e.gameObject.name.Contains("02")) ext = e;
            if (ext == null) { Say("找不到 02 号瓶"); Finish(1); return; }

            Say("cam=" + cam.transform.position.ToString("F2"));
            Say("ext02 root(未抓)=" + ext.transform.position.ToString("F2") + " nozzle=" + (ext.nozzle ? ext.nozzle.position.ToString("F2") : "null"));

            var hand = new GameObject("P7Hand").transform;
            hand.SetParent(cam.transform, false);
            hand.localPosition = new Vector3(0.40f, -0.52f, 0.95f);
            hand.rotation = Quaternion.LookRotation(new Vector3(0f, -0.35f, -1f), Vector3.up);

            ext.quizRequired = false;
            ext.grab.GrabExternal(hand, HandSide.Right);
            Say("after grab held=" + ext.grab.IsHeld + " root=" + ext.transform.position.ToString("F2")
                + " handWorld=" + hand.position.ToString("F2"));

            if (ext.nozzle != null)
                Say("nozzle=" + ext.nozzle.position.ToString("F2") + " fwd=" + ext.nozzle.forward.ToString("F2"));

            foreach (var f in FireSource.Active)
            {
                if (f == null) continue;
                float d = ext.nozzle ? Vector3.Distance(ext.nozzle.position, f.AimPoint) : -1f;
                Say("fire " + f.gameObject.name + " burning=" + f.IsBurning + " out=" + f.IsExtinguished
                    + " prog=" + f.progress01.ToString("F2") + " aim=" + f.AimPoint.ToString("F2")
                    + " dNozzle=" + d.ToString("F2"));
            }

            var coach = ext.GetComponent<ExtinguisherCoach>();
            if (coach != null)
            {
                coach.EvaluateNow(out string msg, out float dist);
                Say("coach band=" + coach.Band + " fire=" + (coach.LastFire ? coach.LastFire.gameObject.name : "null")
                    + " dist=" + coach.LastDistance.ToString("F2") + " msg=" + msg);
            }

            Finish(0);
        }

        static void Say(string s) { Lines.Add(s); Debug.Log("[P7] " + s); }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            Directory.CreateDirectory(@"D:\43763\VR\summary\Logs\shots");
            File.WriteAllText(@"D:\43763\VR\summary\Logs\shots\_round40_probe7.txt", string.Join("\n", Lines.ToArray()));
            EditorApplication.Exit(code);
        }
    }
}
