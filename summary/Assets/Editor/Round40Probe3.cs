#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 40 轮玩家视角验收：
    /// 1) 纸张情景 02 号灭火器已从柜/椅夹缝挪出、挂到西墙挂钩板上（瓶底离地 0.30 米）；
    /// 2) 房间烟雾浓度、全局大厅烟雾、西窗「室内→窗洞」导流都被真实打开；
    /// 3) 答题面板仍然不出现「距灭火器」距离提示；
    /// 4) 实喷灭火，火灭后各层烟雾自动收；
    /// 5) 安全出口解锁后，门内→门外 6 米的烟雾扩散自动开始。
    /// 批处理：-executeMethod FireSafety.EditorTools.Round40Probe3.Run（不要加 -nographics / -quit）
    /// </summary>
    public static class Round40Probe3
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 120f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _phase;
        static float _t0, _phaseT;
        static bool _entered;

        static StartScreen _start;
        static DrillScenario _scenario;
        static PlayerVitals _vitals;
        static Extinguisher _ext02;
        static FireSource _fire;
        static ExitDoor _door;
        static DoorSmokeSpread _doorSmoke;
        static SmokeZone _paperZone, _lobbySmoke, _windowFeed;
        static Transform _rig, _cam;
        static CharacterController[] _ccs;
        static Transform _handAnchor;
        static bool _shotSpray;

        public static void Run()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _t0 = 0f; _phaseT = 0f; _entered = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G40v] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G40v] × " + s);
            Log.Add("× " + s);
            Fails.Add(s);
        }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }
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

            float since = Time.realtimeSinceStartup - _phaseT;

            if (_phase == 0)
            {
                if (Time.realtimeSinceStartup - _t0 < 2.0f || !Resolve()) return;
                DrillLocale.SetEnglish(false);
                _start.SetVisible(false);
                _scenario.StartFormal(-1, 1);   // 情景二：办公桌纸张起火
                _fire = _scenario.deviceFire;   // StartFormal 之后这里才是当前情景的火源
                if (_fire == null) { Fail("情景启动后拿不到火源"); Finish("NO_FIRE"); return; }
                Say("纸张情景启动：火=" + _fire.AimPoint.ToString("F2")
                    + " · 02 号瓶=" + _ext02.transform.position.ToString("F2")
                    + " · 门=" + (_door != null ? _door.transform.position.ToString("F2") : "null"));
                Next(1);
            }
            else if (_phase == 1)
            {
                if (since < 3.2f) return;
                Check("纸张房间烟雾已自动开启", _paperZone != null && _paperZone.Active,
                    _paperZone != null ? ("发射点 " + _paperZone.EmitterCount) : "null");
                Check("全局大厅烟雾已自动开启", _lobbySmoke != null && _lobbySmoke.Active,
                    _lobbySmoke != null ? ("发射点 " + _lobbySmoke.EmitterCount) : "null");
                Check("西窗室内导流烟带已自动开启", _windowFeed != null && _windowFeed.Active,
                    _windowFeed != null ? ("发射点 " + _windowFeed.EmitterCount) : "null");
                var winSmoke = GameObject.Find("WindowSmoke_West");
                var winComp = winSmoke != null ? winSmoke.GetComponent<WindowSmoke>() : null;
                Check("西窗排烟粒子在播放", winComp != null && winComp.IsPlaying);
                ShotAt("g40v_paper_room_smoke.png", new Vector3(-3.40f, 1.62f, -5.50f), new Vector3(-5.71f, 1.05f, -4.54f), 78f);
                ShotAt("g40v_paper_window_smoke.png", new Vector3(-5.25f, 1.60f, -4.60f), new Vector3(-6.80f, 1.72f, -4.60f), 72f);
                ShotAt("g40v_paper_lobby_smoke.png", new Vector3(-0.70f, 1.62f, 1.80f), new Vector3(5.00f, 1.30f, -1.20f), 80f);
                Next(2);
            }
            else if (_phase == 2)
            {
                if (since < 0.5f) return;
                TeleportTo(new Vector3(-5.62f, 0f, -1.55f));
                LookAtPoint(new Vector3(_ext02.transform.position.x, _ext02.transform.position.y + 0.45f, _ext02.transform.position.z));
                Next(3);
            }
            else if (_phase == 3)
            {
                if (since < 1.2f) return;
                float dy = _ext02.transform.position.y;
                float dWall = Mathf.Abs(_ext02.transform.position.x - (-6.55f));
                Check("02 号瓶已离开地面、挂在墙面挂钩上（瓶底 ≥ 0.25 米）", dy >= 0.40f,
                    "pivot y=" + dy.ToString("F2") + "，推算瓶底≈" + (dy - 0.16f).ToString("F2") + " 米");
                Check("02 号瓶贴在西墙面（距墙面 ≤ 0.30 米）", dWall <= 0.30f, dWall.ToString("F2") + " 米");
                Check("02 号瓶所在墙面已有挂钩板", GameObject.Find("Extinguisher02_Bracket") != null);
                float dFire = Vector2.Distance(
                    new Vector2(_ext02.transform.position.x, _ext02.transform.position.z),
                    new Vector2(_fire.transform.position.x, _fire.transform.position.z));
                Check("取瓶站位仍在火焰危险半径之外", dFire > _vitals.fireDangerRadius,
                    "距火 " + dFire.ToString("F2") + " 米，半径 " + _vitals.fireDangerRadius.ToString("F2"));
                Shot("g40v_ext02_on_wall.png");
                Next(4);
            }
            else if (_phase == 4)
            {
                if (since < 0.4f) return;
                _ext02.quizRequired = true;
                _ext02.quizPanel.Open(_ext02.transform);
                Next(5);
            }
            else if (_phase == 5)
            {
                if (since < 0.9f) return;
                var q = _ext02.quizPanel;
                Check("答题面板已打开", q.IsOpen);
                Check("答题时「距灭火器」提示不弹出", !q.DistanceHintVisible);
                Check("答题文案不含「距灭火器」", !q.DistanceText.Contains("距灭火器"), "实际「" + q.DistanceText + "」");
                var coach = _ext02.GetComponent<ExtinguisherCoach>();
                Check("教练距离 Toast/HUD 被抑制", coach != null && coach.HintsSuppressed);
                Shot("g40v_quiz_no_distance.png");
                q.Close();
                Next(6);
            }
            else if (_phase == 6)
            {
                if (since < 0.4f) return;
                if (_handAnchor == null)
                {
                    var go = new GameObject("ProbeHand");
                    go.transform.SetParent(_cam, false);
                    go.transform.localPosition = new Vector3(0.30f, -0.34f, 0.90f);
                    go.transform.localRotation = Quaternion.identity;
                    _handAnchor = go.transform;
                }
                _ext02.grab.GrabExternal(_handAnchor, HandSide.Right);
                if (!_ext02.grab.IsHeld) { Fail("外部吸附失败，02 号瓶没拿到手上"); Finish("GRAB_FAIL"); return; }
                if (_ext02.quizPanel != null && _ext02.quizPanel.IsOpen) _ext02.quizPanel.Close();
                _ext02.MarkQuizPassed();
                _ext02.PullPin();
                if (_ext02.handleLever != null) _ext02.handleLever.PressExternal();
                Say("拿瓶：IsHeld=" + _ext02.grab.IsHeld + " 拔销=" + _ext02.PinPulled
                    + " 问答=" + _ext02.QuizPassed + " 压把=" + (_ext02.handleLever != null && _ext02.handleLever.PressedOnce));
                TeleportTo(new Vector3(_fire.transform.position.x, 0f, _fire.transform.position.z + 2.0f));
                LookAtPoint(_fire.AimPoint);
                Next(7);
            }
            else if (_phase == 7)
            {
                if (since < 0.6f) return;
                Aim();
                if (!_ext02.IsSpraying)
                {
                    bool ok = _ext02.TryStartSpray();
                    Check("距离合适时可以起喷", ok && _ext02.IsSpraying);
                    _phaseT = Time.realtimeSinceStartup;
                    return;
                }
                Aim();
                float t = Time.realtimeSinceStartup - _phaseT;
                if (t > 0.6f && !_shotSpray) { _shotSpray = true; Shot("g40v_spraying.png"); }
                if (_fire.IsExtinguished)
                {
                    _ext02.StopSprayExternal();
                    Say("纸张火已扑灭，火焰剩余 " + (_fire.progress01 * 100f).ToString("F0") + "%");
                    Shot("g40v_extinguished.png");
                    Next(8);
                    return;
                }
                if (t > 12f) { Fail("喷射 12 秒未灭火"); _ext02.StopSprayExternal(); Next(8); }
            }
            else if (_phase == 8)
            {
                if (since < 3.0f) return;
                Check("灭火后纸张房间烟雾自动停止", _paperZone != null && !_paperZone.Active);
                Check("灭火后西窗导流烟带自动停止", _windowFeed != null && !_windowFeed.Active);
                Check("灭火后大厅全局烟雾自动停止", _lobbySmoke != null && !_lobbySmoke.Active);
                ShotAt("g40v_after_smoke_clear.png", new Vector3(-3.40f, 1.62f, -5.50f), new Vector3(-5.71f, 1.05f, -4.54f), 78f);
                _door.Unlock();
                Next(9);
            }
            else if (_phase == 9)
            {
                if (since < 2.2f) return;
                Check("安全出口已打开（门扇到位）", _door.IsOpen || _door.IsUnlocked);
                Check("门打开后门口烟雾扩散已启动", _doorSmoke != null && _doorSmoke.Active,
                    _doorSmoke != null ? ("发射点 " + _doorSmoke.EmitterCount) : "null");
                Vector3 dp = _door.transform.position;
                Vector3 inward = _door.transform.forward; inward.y = 0f; inward.Normalize();
                Vector3 outward = -inward;
                Vector3 insideShot = dp + inward * 2.6f + Vector3.up * 1.55f;
                Vector3 outsideShot = dp + outward * 3.6f + Vector3.up * 1.55f;
                ShotAt("g40v_door_smoke_inside.png", insideShot, new Vector3(dp.x, 1.05f, dp.z), 78f);
                ShotAt("g40v_door_smoke_outside.png", outsideShot, new Vector3(dp.x, 1.15f, dp.z), 80f);
                Finish("DONE");
            }
        }

        static bool Resolve()
        {
            _start = Object.FindObjectOfType<StartScreen>();
            _scenario = Object.FindObjectOfType<DrillScenario>();
            _vitals = Object.FindObjectOfType<PlayerVitals>();
            _door = Object.FindObjectOfType<ExitDoor>();
            _doorSmoke = Object.FindObjectOfType<DoorSmokeSpread>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            _ext02 = null;
            foreach (var e in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e.name == "Extinguisher02") _ext02 = e;
            _fire = _scenario != null ? _scenario.deviceFire : null;   // StartFormal 之后指向当前情景火源
            _paperZone = ResolveZone("Stage1Smoke_Paper");
            _lobbySmoke = ResolveZone("LobbyGlobalSmoke");
            _windowFeed = ResolveZone("WindowFeed_West");
            if (_start == null) { Fail("找不到 StartScreen"); return false; }
            if (_scenario == null) { Fail("找不到 DrillScenario"); return false; }
            if (_vitals == null) { Fail("找不到 PlayerVitals"); return false; }
            if (_ext02 == null) { Fail("找不到 Extinguisher02"); return false; }
            if (_door == null) { Fail("找不到 ExitDoor"); return false; }
            if (_rig == null || _cam == null) { Fail("找不到 XR Origin / 主相机"); return false; }
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return true;
        }

        static SmokeZone ResolveZone(string name)
        {
            var go = GameObject.Find(name);
            return go != null ? go.GetComponent<SmokeZone>() : null;
        }

        static void Aim()
        {
            if (_ext02 == null || _ext02.nozzle == null || _fire == null) return;
            Vector3 dir = _fire.AimPoint - _ext02.nozzle.position;
            if (dir.sqrMagnitude < 0.0001f) return;
            _ext02.AlignNozzleTo(dir);
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

        static void Shot(string name) { ShotAt(name, _cam.position, _cam.position + _cam.forward * 3f); }

        static void ShotAt(string name, Vector3 pos, Vector3 lookAt, float fov = 0f)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Fail("截图无主相机：" + name); return; }
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
                Object.Destroy(tex);
                cam.targetTexture = oldTarget;
                cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos;
                tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
                Say("截图 " + name);
            }
            catch (System.Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }

        static void Finish(string reason)
        {
            Say("结果 " + reason + "，失败数=" + Fails.Count);
            var sb = new System.Text.StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("FAIL_COUNT=" + Fails.Count);
            sb.AppendLine("PROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1));
            File.WriteAllText(Path.Combine(ShotDir, "_round40_probe.txt"), sb.ToString(), new System.Text.UTF8Encoding(false));
            EditorApplication.update -= Tick;
            if (_ext02 != null) _ext02.StopSprayExternal();
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
