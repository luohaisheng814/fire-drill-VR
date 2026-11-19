#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 36 轮玩家视角验收：
    /// 1) 开始界面按钮的悬停/按下/松开反馈；
    /// 2) 从大厅西侧、茶水间、房间中部三个方向去安全出口时的地面箭头与自适应出口牌。
    /// 批处理：-executeMethod FireSafety.EditorTools.ExitGuideProbe.Run
    /// </summary>
    public static class ExitGuideProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 150f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _phase;
        static float _t0, _phaseT;
        static bool _entered;
        static StartScreen _start;
        static GuidanceArrows _arrows;
        static Transform _rig, _cam;
        static CharacterController[] _ccs;
        static ButtonFeedback _startFeedback;
        static int _exitIndex;

        static readonly Vector3[] ExitOrigins =
        {
            new Vector3(0.0f, 0f, 2.6f),    // 大厅西北侧
            new Vector3(6.3f, 0f, -2.95f),  // 茶水间/03 号灭火器一侧
            new Vector3(2.6f, 0f, 2.4f)    // 大厅东北侧空地
        };
        static readonly string[] ExitOriginNames = { "lobby_west", "tea_room", "room_center" };

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0f; _t0 = 0f; _entered = false; _exitIndex = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G36] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G36] × " + s);
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
                if (cam == null) return;
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
                if (!Resolve()) return;
                if (Time.realtimeSinceStartup - _t0 < 2.0f) return;
                DrillLocale.SetEnglish(false);
                _start.SetVisible(true);
                _startFeedback = FindStartFeedback();
                Check("开始界面按钮均挂载 ButtonFeedback", CountFeedback() >= 6, "数量=" + CountFeedback());
                if (_startFeedback == null) { Finish("NO_START_FEEDBACK"); return; }
                Next(1);
            }
            else if (_phase == 1)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;
                Shot("vr36_ui_idle.png");
                var ped = MakePointer();
                ExecuteEvents.Execute<IPointerEnterHandler>(_startFeedback.gameObject, ped, ExecuteEvents.pointerEnterHandler);
                Next(2);
            }
            else if (_phase == 2)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                Shot("vr36_ui_hover.png");
                Check("悬停放大", _startFeedback.CurrentScale > 1.02f,
                    "scale=" + _startFeedback.CurrentScale.ToString("F3"));

                var ped = MakePointer();
                ExecuteEvents.Execute<IPointerDownHandler>(_startFeedback.gameObject, ped, ExecuteEvents.pointerDownHandler);
                Next(3);
            }
            else if (_phase == 3)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.12f) return;
                Check("按下状态被记录", _startFeedback.IsPressed);
                Check("按下时按钮缩小", _startFeedback.CurrentScale < 0.98f,
                    "scale=" + _startFeedback.CurrentScale.ToString("F3"));
                Check("按下白色闪光可见", _startFeedback.FlashAlpha > 0.05f,
                    "alpha=" + _startFeedback.FlashAlpha.ToString("F3"));
                Shot("vr36_ui_press.png");

                var ped = MakePointer();
                ExecuteEvents.Execute<IPointerUpHandler>(_startFeedback.gameObject, ped, ExecuteEvents.pointerUpHandler);
                Next(4);
            }
            else if (_phase == 4)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.40f) return;
                Shot("vr36_ui_release.png");
                Check("松开后退出按下态", !_startFeedback.IsPressed);
                Check("松开后回到可点击比例", _startFeedback.CurrentScale > 0.95f,
                    "scale=" + _startFeedback.CurrentScale.ToString("F3"));

                // 进入安全出口引导：隐藏开始界面，打开第 3 阶段引导
                _start.SetVisible(false);
                _arrows.StopGuidance();
                _arrows.BeginExitGuidance();
                TeleportTo(ExitOrigins[0]);
                LookAtPoint(_arrows.exitTarget.position);
                Next(5);
            }
            else if (_phase == 5 || _phase == 6 || _phase == 7)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.7f) return;
                int idx = _phase - 5;
                Shot("vr36_exit_" + ExitOriginNames[idx] + ".png");
                Check("出口箭头从 " + ExitOriginNames[idx] + " 可见", _arrows.VisibleArrowCount >= 2,
                    "arrows=" + _arrows.VisibleArrowCount + " path=" + _arrows.PathPointCount
                    + " detour=" + _arrows.UsingDetour);

                Vector3 player = _cam.position;
                Vector3 exit = _arrows.exitTarget.position;
                Vector3 toExit = exit - player; toExit.y = 0f;
                Check("第一枚箭头在玩家前方", Vector3.Distance(Flat(_arrows.FirstArrowPosition), Flat(player)) > 0.6f,
                    "d=" + Vector3.Distance(Flat(_arrows.FirstArrowPosition), Flat(player)).ToString("F2"));
                Check("箭头整体朝出口方向", Vector3.Dot(_arrows.FirstArrowForward, toExit.normalized) > 0.15f,
                    "dot=" + Vector3.Dot(_arrows.FirstArrowForward, toExit.normalized).ToString("F2"));
                Check("出口牌显示实时距离和方向", _arrows.ExitSignVisible
                    && _arrows.CurrentSignText.Contains("安全出口")
                    && _arrows.CurrentSignText.Contains("米"),
                    "text=" + _arrows.CurrentSignText);

                if (idx + 1 < ExitOrigins.Length)
                {
                    TeleportTo(ExitOrigins[idx + 1]);
                    LookAtPoint(exit);
                    Next(5 + idx + 1);
                }
                else
                {
                    Check("不同来向均能生成 2 段以上路径",
                        _arrows.PathPointCount >= 2 && _arrows.VisibleArrowCount >= 2);
                    Next(8);
                }
            }
            else if (_phase == 8)
            {
                Finish("DONE");
            }
        }

        static bool Resolve()
        {
            _start = StartScreen.Instance != null ? StartScreen.Instance : Object.FindObjectOfType<StartScreen>();
            _arrows = GuidanceArrows.Instance != null ? GuidanceArrows.Instance : Object.FindObjectOfType<GuidanceArrows>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            if (_start == null || _arrows == null) { Fail("找不到 StartScreen / GuidanceArrows"); return false; }
            if (_rig == null || _cam == null) { Fail("找不到 XR Origin / 主相机"); return false; }
            if (_arrows.exitTarget == null) { Fail("GuidanceArrows 未配置 exitTarget"); return false; }
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return true;
        }

        static int CountFeedback()
        {
            return _start.GetComponentsInChildren<ButtonFeedback>(true).Length;
        }

        static ButtonFeedback FindStartFeedback()
        {
            foreach (var fb in _start.GetComponentsInChildren<ButtonFeedback>(true))
            {
                var t = fb.GetComponentInChildren<Text>(true);
                if (t == null) continue;
                string s = t.text ?? "";
                if (s.Contains("开始") || s.Contains("Start")) return fb;
            }
            return null;
        }

        static PointerEventData MakePointer()
        {
            var ped = new PointerEventData(EventSystem.current);
            var cam = Camera.main;
            if (cam != null && _startFeedback != null)
                ped.position = RectTransformUtility.WorldToScreenPoint(cam, _startFeedback.transform.position);
            return ped;
        }

        static Vector3 Flat(Vector3 v) { v.y = 0f; return v; }

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
            File.WriteAllText(Path.Combine(ShotDir, "_exitguide_36.txt"), sb.ToString());
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
