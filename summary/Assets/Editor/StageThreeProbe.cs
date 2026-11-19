#if UNITY_EDITOR
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.XR;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 34 轮：第 3 阶段「安全出口门前复燃」实喷探针（虚拟头显 + 玩家第一视角）。
    /// 不再依赖第 1 阶段流程：直接进入正式演练 → 跳过撤离 → 进入 StageThree，
    /// 站到地面灭火点位、拿瓶、拔销、压压把、喷嘴对准火根持续喷射，记录 progress01 与门锁。
    ///
    /// 批处理：-executeMethod FireSafety.EditorTools.StageThreeProbe.RunPaper（不加 -nographics/-quit）
    /// 另有 RunPantry / RunElectrical。
    /// </summary>
    public static class StageThreeProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 90f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _scenario;
        static int _phase;
        static float _t0, _phaseT;
        static bool _shotBefore, _shotSpray, _shotAfter;
        static float _prog0;
        static bool _failedLogged;

        static DrillScenario _sc;
        static StageTwoFlow _flow;
        static Extinguisher _ext;
        static FireSource _fire;
        static ExitDoor _door;
        static Transform _rig, _cam, _rightHand;
        static CharacterController[] _ccs;

        public static void RunPaper() { Run(1); }
        public static void RunPantry() { Run(2); }
        public static void RunElectrical() { Run(0); }

        static void Run(int scenario)
        {
            _scenario = scenario;
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0f; _shotBefore = _shotSpray = _shotAfter = false;
            _failedLogged = false;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[S3PROBE] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[S3PROBE] ✘ " + s);
            Log.Add("✘ " + s);
            Fails.Add(s);
        }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("✔ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        static void Shot(string name)
        {
            string path = Path.Combine(ShotDir, name);
            ScreenCapture.CaptureScreenshot(path);
            Say("玩家视角截图 " + name);
        }

        static void Next(int phase)
        {
            _phase = phase;
            _phaseT = Time.realtimeSinceStartup;
            Say("→ 阶段 " + phase);
        }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (_t0 <= 0f) _t0 = Time.realtimeSinceStartup + 2f;

            if (_phase == 0)
            {
                if (!Resolve()) return;
                if (Time.realtimeSinceStartup - _t0 < 1.5f) return;

                _sc.StartFormal(0, _scenario);
                // 探针直接调用 StartFormal，绕过了开始界面的按钮，必须手动收起面板，否则玩家视角被面板挡住。
                var start = Object.FindObjectOfType<StartScreen>();
                if (start != null) start.SetVisible(false);
                var panel = Object.FindObjectOfType<StageOneCompletePanel>();
                if (panel != null && panel.IsOpen) panel.HidePanel();
                if (DrillCountdown.Instance != null) DrillCountdown.Instance.StopAndReset();
                _flow.skipEvacuation = true;
                _flow.Begin();
                _fire = _flow.stageTwoFire;
                _ext = _flow.spareExtinguisher;
                Next(1);
                return;
            }

            if (_phase == 1)
            {
                if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                if (_flow.CurrentStage != StageTwoFlow.Stage.StageThree)
                {
                    Fail("未进入第三阶段（当前 " + _flow.CurrentStage + "）");
                    Finish(1); return;
                }
                if (_fire == null || !_fire.IsBurning)
                {
                    Fail("第三阶段复燃火未点燃");
                    Finish(1); return;
                }
                Say("复燃火：位置=" + _fire.transform.position.ToString("F2")
                    + " 瞄准点=" + _fire.AimPoint.ToString("F2")
                    + " 燃烧=" + _fire.IsBurning + " 锁定=" + _fire.extinguishLocked
                    + " 进度=" + _fire.progress01.ToString("F3"));
                Check("第三阶段火源已解除喷射锁定", !_fire.extinguishLocked);

                Transform point = _flow.stageThreeStandPoint;
                Check("存在地面灭火点位", point != null && point.GetComponent<ExtinguishPoint>() != null);
                if (point == null) { Finish(1); return; }

                // 先从点位后侧 1.4 米拍一张「玩家走向圆环」的视角，确认点位真的可见。
                Vector3 back = point.position - _fire.transform.position;
                back.y = 0f;
                if (back.sqrMagnitude < 0.01f) back = -point.forward;
                TeleportTo(point.position + back.normalized * 1.4f);
                LookAtPoint(point.position + Vector3.up * 0.55f);
                Shot("vr34_stage3_point_marker.png");
                Next(4);
                return;
            }

            if (_phase == 4)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                Transform point = _flow.stageThreeStandPoint;
                TeleportTo(point.position);
                LookAtPoint(_fire.AimPoint);

                _ext = _flow.spareExtinguisher;
                if (_ext == null) { Fail("备用灭火器为空"); Finish(1); return; }
                _ext.MarkQuizPassed();
                if (!_ext.grab.IsHeld) _ext.grab.GrabExternal(_rightHand, HandSide.Right);
                if (!_ext.PinPulled) _ext.PullPin();
                if (_ext.handleLever != null && !_ext.handleLever.IsSqueezed) _ext.handleLever.PressExternal();

                Next(2);
                return;
            }

            if (_phase == 2)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                Aim();
                if (!_shotBefore)
                {
                    Shot("vr34_stage3_point_before.png");
                    _shotBefore = true;
                    Say("站位点距火 " + Vector3.Distance(_ext.nozzle.position, _fire.AimPoint).ToString("F2")
                        + " 米 · 已拔销=" + _ext.PinPulled + " 持握=" + _ext.grab.IsHeld
                        + " 压把=" + (_ext.handleLever != null && _ext.handleLever.PressedOnce));
                }
                bool started = _ext.TryStartSpray();
                Say("TryStartSpray=" + started + " · IsSpraying=" + _ext.IsSpraying);
                if (!started) { Fail("无法开始喷射"); Finish(1); return; }

                _prog0 = _fire.progress01;
                Next(3);
                return;
            }

            if (_phase == 3)
            {
                Aim();
                float t = Time.realtimeSinceStartup - _phaseT;

                if (!_shotSpray && t > 0.7f)
                {
                    Shot("vr34_stage3_spraying.png");
                    _shotSpray = true;
                }
                if (!_failedLogged && t > 1.2f)
                {
                    _failedLogged = true;
                    Say("喷射 1.2 秒：进度 " + _prog0.ToString("F3") + " → " + _fire.progress01.ToString("F3")
                        + " · 喷嘴距瞄准点 " + Vector3.Distance(_ext.nozzle.position, _fire.AimPoint).ToString("F2") + " 米");
                    if (Mathf.Abs(_fire.progress01 - _prog0) < 0.05f)
                        Fail("喷射 1.2 秒进度几乎不变（喷锥没打到火 / 火被锁定）");
                }

                if (_fire.IsExtinguished)
                {
                    _ext.StopSprayExternal();
                    if (!_shotAfter) { Shot("vr34_stage3_extinguished.png"); _shotAfter = true; }
                    Say("火已灭：最终进度=" + _fire.progress01.ToString("F3")
                        + " · 门解锁=" + (_door != null && _door.IsUnlocked)
                        + " · 阶段=" + _flow.CurrentStage);
                    Check("复燃火被灭火器实际喷灭", _fire.IsExtinguished);
                    Check("灭火后安全出口解锁", _door != null && _door.IsUnlocked);
                    Check("灭火后阶段推进到 ExitOpen", _flow.CurrentStage == StageTwoFlow.Stage.ExitOpen);
                    Finish(Fails.Count == 0 ? 0 : 1);
                    return;
                }
                if (t > 10f)
                {
                    _ext.StopSprayExternal();
                    Fail("持续喷射 10 秒仍未灭火（进度 " + _fire.progress01.ToString("F3") + "）");
                    Finish(1);
                }
                return;
            }
        }

        static bool Resolve()
        {
            _sc = Object.FindObjectOfType<DrillScenario>();
            _flow = Object.FindObjectOfType<StageTwoFlow>();
            _door = Object.FindObjectOfType<ExitDoor>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            if (_sc == null || _flow == null || _cam == null || _rig == null) return false;
            foreach (var tr in Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (tr.name == "RightHand Controller") _rightHand = tr;
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return _rightHand != null;
        }

        static void TeleportTo(Vector3 p)
        {
            Vector3 cam = _cam.position;
            Vector3 delta = new Vector3(p.x - cam.x, 0f, p.z - cam.z);
            if (_ccs != null) foreach (var c in _ccs) c.enabled = false;
            _rig.position += delta;
            Physics.SyncTransforms();
            if (_ccs != null) foreach (var c in _ccs) c.enabled = true;
            Say("机位放到灭火点位 " + p.ToString("F2") + " · 眼高 " + (_cam.position.y - p.y).ToString("F2") + " 米");
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

        static void Aim()
        {
            if (_ext == null || _ext.nozzle == null || _rightHand == null) return;
            Vector3 dir = _fire.AimPoint - _ext.nozzle.position;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion d = Quaternion.FromToRotation(_ext.nozzle.forward, dir.normalized);
            _rightHand.rotation = d * _rightHand.rotation;
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            if (_ext != null) _ext.StopSprayExternal();
            File.WriteAllText(Path.Combine(ShotDir, "_stage3probe_" + _scenario + ".txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }
    }
}
#endif
