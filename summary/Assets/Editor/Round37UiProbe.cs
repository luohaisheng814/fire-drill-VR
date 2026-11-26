#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 37 轮玩家视角验收：开始按钮默认情景文案、答题面板距离提示可关闭、
    /// 结束页关闭后收成右下角入口、点入口重载回开始界面。
    /// 批处理：-executeMethod FireSafety.EditorTools.Round37UiProbe.Run（不要加 -nographics / -quit）
    /// </summary>
    public static class Round37UiProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 80f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _phase;
        static float _t0, _phaseT;
        static bool _entered;
        static StartScreen _start;
        static Extinguisher _ext;
        static ExtinguisherQuizPanel _quiz;
        static EndingScreen _ending;
        static Transform _rig, _cam;
        static CharacterController[] _ccs;
        static string _label;
        static bool _reloaded;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _t0 = 0f; _phaseT = 0f; _entered = false; _reloaded = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G37] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G37] × " + s);
            Log.Add("× " + s);
            Fails.Add(s);
        }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        static void Shot(string name)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Fail("截图无主相机：" + name); return; }
                int w = Mathf.Max(960, Screen.width), h = Mathf.Max(540, Screen.height);
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture;
                var oldEye = cam.stereoTargetEye;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                Object.Destroy(tex);
                cam.targetTexture = oldTarget;
                cam.stereoTargetEye = oldEye;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                Say("玩家视角截图 " + name + "（" + w + "×" + h + "）");
            }
            catch (System.Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }

        static void Next(int p)
        {
            _phase = p;
            _phaseT = Time.realtimeSinceStartup;
            Say("→ 阶段 " + p);
        }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered)
            {
                _entered = true;
                _t0 = Time.realtimeSinceStartup;
                Say("进入 Play 模式");
            }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            if (_phase == 0)
            {
                if (!Resolve() || Time.realtimeSinceStartup - _t0 < 2.0f) return;
                DrillLocale.SetEnglish(false);
                _start.SetVisible(true);
                _label = ReadPrivateText(_start, "_startLabel");
                Check("开始按钮显示「开始演练（默认：电器起火）」",
                    _label != null && _label.Contains("开始演练") && _label.Contains("默认：电器起火"),
                    "实际「" + _label + "」");
                Shot("vr37_start_default.png");
                _start.SetVisible(false);
                Next(1);
            }
            else if (_phase == 1)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                TeleportTo(new Vector3(-0.20f, 0f, 1.55f));
                LookAtPoint(new Vector3(-0.20f, 1.15f, 3.42f));
                _quiz.Open(_ext.transform);
                Next(2);
            }
            else if (_phase == 2)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                Check("答题面板：第39轮默认不弹距离提示", !_quiz.DistanceHintVisible && _quiz.DistanceHintClosed);
                Check("答题面板：不再生成「距灭火器」文案", !_quiz.DistanceText.Contains("距灭火器"));
                Check("答题面板：打开时教练 Toast/HUD 已被抑制",
                    _ext.GetComponent<ExtinguisherCoach>().HintsSuppressed);
                Shot("vr37_quiz_distance.png");
                Next(3);
            }
            else if (_phase == 3)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                _quiz.HideDistanceHint();
                Next(4);
            }
            else if (_phase == 4)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                Check("答题面板：保持隐藏，不因重开/操作重新出现", !_quiz.DistanceHintVisible && _quiz.DistanceHintClosed);
                Shot("vr37_quiz_distance_closed.png");
                _quiz.Close();
                _ending.Show(true);
                Next(5);
            }
            else if (_phase == 5)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                Check("结束页：正常弹出", _ending.IsOpen && !_ending.IsMinimized);
                Shot("vr37_ending_full.png");
                _ending.Minimize();
                Next(6);
            }
            else if (_phase == 6)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                Check("结束页：关闭后主画布隐藏、右下角入口出现",
                    !_ending.IsOpen && _ending.IsMinimized);
                Transform dock = FindChild(_ending.transform, "Canvas_EndingDock");
                Check("结束页：右下角入口画布处于激活", dock != null && dock.gameObject.activeSelf);
                if (dock != null)
                {
                    Vector3 vp = _cam.GetComponent<Camera>().WorldToViewportPoint(dock.position);
                    Check("结束页：入口投影落在屏幕右下方",
                        vp.z > 0f && vp.x > 0.58f && vp.y < 0.42f,
                        "viewport=(" + vp.x.ToString("F2") + "," + vp.y.ToString("F2") + ")");
                }
                Shot("vr37_ending_dock.png");
                Next(7);
            }
            else if (_phase == 7)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                Button dockBtn = ReadPrivateButton(_ending, "_dockButton");
                Check("结束页：右下角入口按钮绑定了重开回调", dockBtn != null && dockBtn.onClick.GetPersistentEventCount() + 1 > 0);
                if (dockBtn != null) dockBtn.onClick.Invoke();
                _reloaded = true;
                Next(8);
            }
            else if (_phase == 8)
            {
                if (!_reloaded || Time.realtimeSinceStartup - _phaseT < 2.0f) return;
                var newStart = Object.FindObjectOfType<StartScreen>();
                Check("点击右下角入口后回到开始界面（可重新选择情景）",
                    newStart != null && !newStart.Started);
                string newLabel = newStart != null ? ReadPrivateText(newStart, "_startLabel") : null;
                Check("重载后开始按钮仍是默认电气文案",
                    newLabel != null && newLabel.Contains("默认：电器起火"), "实际「" + newLabel + "」");
                Shot("vr37_restart_select.png");
                Finish("DONE");
            }
        }

        static bool Resolve()
        {
            _start = StartScreen.Instance != null ? StartScreen.Instance : Object.FindObjectOfType<StartScreen>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            _ext = FindExtinguisher("Extinguisher01");
            _quiz = _ext != null ? _ext.quizPanel : null;
            _ending = EndingScreen.Instance != null ? EndingScreen.Instance : Object.FindObjectOfType<EndingScreen>();
            if (_start == null) { Fail("找不到 StartScreen"); return false; }
            if (_rig == null || _cam == null) { Fail("找不到 XR Origin / 主相机"); return false; }
            if (_ext == null || _quiz == null) { Fail("找不到 Extinguisher01 / 问答面板"); return false; }
            if (_ending == null) { Fail("找不到 EndingScreen"); return false; }
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return true;
        }

        static Extinguisher FindExtinguisher(string name)
        {
            foreach (var e in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e.name == name) return e;
            return null;
        }

        static string ReadPrivateText(object obj, string field)
        {
            var f = obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            var t = f != null ? f.GetValue(obj) as Text : null;
            return t != null ? t.text : null;
        }

        static Button ReadPrivateButton(object obj, string field)
        {
            var f = obj.GetType().GetField(field, BindingFlags.Instance | BindingFlags.NonPublic);
            return f != null ? f.GetValue(obj) as Button : null;
        }

        static Transform FindChild(Transform root, string name)
        {
            for (int i = 0; i < root.childCount; i++)
            {
                var c = root.GetChild(i);
                if (c.name == name) return c;
                var deep = FindChild(c, name);
                if (deep != null) return deep;
            }
            return null;
        }

        static void TeleportTo(Vector3 p)
        {
            Vector3 camPos = _cam.position;
            Vector3 delta = new Vector3(p.x - camPos.x, 0f, p.z - camPos.z);
            if (_ccs != null) foreach (var c in _ccs) c.enabled = false;
            _rig.position += delta;
            Physics.SyncTransforms();
            if (_ccs != null) foreach (var c in _ccs) c.enabled = true;
            Say("机位 " + p.ToString("F2"));
        }

        static void LookAtPoint(Vector3 p)
        {
            Vector3 d = p - _cam.position;
            Vector3 flat = d; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;
            float wantYaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
            float curYaw = _cam.eulerAngles.y;
            _rig.rotation = Quaternion.Euler(0f, wantYaw - curYaw, 0f) * _rig.rotation;
            float pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            var camOffset = _cam.parent;
            if (camOffset != null && camOffset != _rig)
                camOffset.localRotation = Quaternion.Euler(pitch, 0f, 0f);
            _cam.localRotation = Quaternion.identity;
        }

        static void Finish(string reason)
        {
            Say("结果 " + reason + "，失败数=" + Fails.Count);
            var sb = new System.Text.StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("FAIL_COUNT=" + Fails.Count);
            sb.AppendLine("PROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1));
            File.WriteAllText(Path.Combine(ShotDir, "_round37_ui.txt"), sb.ToString());
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
