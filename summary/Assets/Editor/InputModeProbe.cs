#if UNITY_EDITOR
using System;
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
    /// 第 29 轮：设备检测 / 操作模式切换的玩家视角探针（真实 Play）。
    ///
    /// 依次验证：
    ///   1. 自动检测：批处理没有头显 → 桌面模式，键鼠层开、手柄层关、手柄模型隐藏、VR 提示条隐藏；
    ///   2. 强制真机：手柄层开、键鼠层关、键盘调试关、手柄模型与 VR 提示条出现；
    ///   3. 头显移动：虚拟摇杆推 1.2 秒，玩家真的走了（位移 + 速度）；
    ///   4. 头显重定位：RecenterTo 把玩家放回指定站位，眼高保持正常；
    ///   5. 强制模拟器：回到键鼠层、键盘调试开、手柄模型仍可见（模拟器里它是唯一"手在哪"的参照）；
    ///   6. 回到自动检测：重新判回桌面模式。
    /// 每步出一张玩家视角图，末两行写 FAIL_COUNT / PROBE_EXIT。
    /// 批处理：-executeMethod FireSafety.EditorTools.InputModeProbe.RunFromBatch（不加 -nographics / -quit）
    /// </summary>
    public static class InputModeProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float PhaseTimeout = 30f;
        const float HardTimeout = 200f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly HashSet<string> _failSeen = new HashSet<string>();

        static RuntimeDeviceMode _mode;
        static VRDrillInput _vr;
        static DesktopDrillInput _desk;
        static XRDrillLocomotion _loco;
        static VRDrillHud _vrhud;
        static Transform _rig, _cam;
        static StartScreen _start;
        static VRControllerVisual _visL, _visR;

        static int _phase;
        static float _phaseT, _t0, _wait;
        static bool _shotDesk, _shotHead, _shotMove, _shotRecenter, _shotSim, _shotAuto;
        static Vector3 _moveStart;
        static float _moveMaxSpeed;
        static Vector3 _spawn;

        // ------------------------------------------------------------------
        [MenuItem("FireSafety/设备检测与操作模式探针（玩家视角）")]
        public static void RunFromMenu() => RunFromBatch();

        public static void RunFromBatch()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear(); _failSeen.Clear();
            _phase = 0; _phaseT = 0f; _wait = 0f;
            _shotDesk = _shotHead = _shotMove = _shotRecenter = _shotSim = _shotAuto = false;
            _moveMaxSpeed = 0f;
            EditorSceneManager.OpenScene(ScenePath);
            _t0 = Time.realtimeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s)
        {
            Debug.Log("[MODEPROBE] " + s);
            Log.Add(s);
        }

        static void Fail(string s)
        {
            if (!_failSeen.Add(s)) return;
            Debug.LogError("[MODEPROBE] ✘ " + s);
            Log.Add("✘ " + s);
            Fails.Add(s);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ShotDir, "_modeprobe.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Next(int phase)
        {
            _phase = phase;
            _phaseT = Time.realtimeSinceStartup;
            _wait = 0f;      // 每个阶段独立计时，别让上一阶段的等待累计把本阶段判定瞬间跳过
            Say("→ 进入阶段 " + phase);
        }

        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) { Say("✔ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）")); }
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        // ------------------------------------------------------------------
        static void Tick()
        {
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Fail("总超时"); Finish(1); return; }
            if (_phase > 0 && Time.realtimeSinceStartup - _phaseT > PhaseTimeout)
            { Fail("阶段 " + _phase + " 超时"); Finish(1); return; }

            if (_phase == 0)
            {
                if (!Application.isPlaying) return;
                _mode = UnityEngine.Object.FindObjectOfType<RuntimeDeviceMode>();
                _vr = UnityEngine.Object.FindObjectOfType<VRDrillInput>();
                _desk = UnityEngine.Object.FindObjectOfType<DesktopDrillInput>();
                _loco = UnityEngine.Object.FindObjectOfType<XRDrillLocomotion>();
                _vrhud = UnityEngine.Object.FindObjectOfType<VRDrillHud>();
                _start = UnityEngine.Object.FindObjectOfType<StartScreen>();
                var origin = UnityEngine.Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
                _rig = origin != null && origin.Origin != null ? origin.Origin.transform : null;
                _cam = Camera.main != null ? Camera.main.transform : null;
                var visuals = UnityEngine.Object.FindObjectsByType<VRControllerVisual>(FindObjectsSortMode.None);
                foreach (var v in visuals)
                {
                    if (v == null) continue;
                    if (v.side == HandSide.Left) _visL = v; else _visR = v;
                }

                bool ready = _mode != null && _vr != null && _desk != null && _loco != null && _vrhud != null
                             && _rig != null && _cam != null;
                if (!ready) { _wait += Time.deltaTime; if (_wait > 8f) { Fail("组件未就绪"); Finish(1); } return; }
                if (_start != null && !_start.DrillStarted) _start.StartGame();
                Next(1);
                return;
            }

            if (_phase == 1)
            {
                _wait += Time.deltaTime;
                if (_wait < 0.8f) return;
                Say("检测结果：" + _mode.DetailLine);
                Check("自动判定为桌面模式（无头显批处理）", _mode.deviceKind == RuntimeDeviceMode.DeviceKind.Desktop);
                Check("键鼠层接管（DesktopDrillInput.enabled）", _desk.enabled);
                Check("手柄层让位（VRDrillInput.enabled=false）", !_vr.enabled);
                Check("移动层处于键鼠模式（键盘调试开）", _loco.keyboardDebug && !_loco.xrControlMode);
                Check("桌面模式下不显示手柄模型", !ControllerVisible());
                Check("桌面模式下隐藏 VR 提示条", !_vrhud.Visible);
                string hud = HudText();
                Check("底部 HUD 写明「桌面模式」", hud.Contains("桌面模式"), hud.Length > 0 ? "" : "HUD 文本为空");
                if (!_shotDesk) { Shot("vr29_mode_1desktop.png"); _shotDesk = true; }
                Next(2);
                return;
            }

            if (_phase == 2)
            {
                _wait += Time.deltaTime;
                if (_wait < 0.5f) { _mode.ForceKind(RuntimeDeviceMode.DeviceKind.Headset); return; }
                if (_wait < 1.3f) return;
                Say("强制真机后：" + _mode.DetailLine + " · 提示条状态=" + _vrhud.CurrentStatus);
                Check("强制真机 → 判定为 VR 头显", _mode.deviceKind == RuntimeDeviceMode.DeviceKind.Headset);
                Check("强制真机 → 手柄层接管", _vr.enabled);
                Check("强制真机 → 键鼠层让位（避免两套输入打架）", !_desk.enabled);
                Check("强制真机 → 移动层切手柄模式（键盘调试关闭）", _loco.xrControlMode && !_loco.keyboardDebug);
                Check("强制真机 → 手柄模型出现", ControllerVisible());
                Check("强制真机 → VR 提示条出现并写明「VR 头显」",
                    _vrhud.Visible && _vrhud.CurrentStatus.Contains("VR 头显"));
                if (!_shotHead) { Shot("vr29_mode_2headset.png"); _shotHead = true; }
                // 挪到门外广场的空地再测移动：出生点紧挨办公桌，正前方会被家具挡住；
                // 朝东（+x）对着知识墙走，截图里既有位移参照又能看到场景
                _loco.RecenterTo(new Vector3(7.80f, 0f, 1.20f), 90f, true);
                _moveStart = _rig.position; _moveMaxSpeed = 0f;
                // 摇杆推到 0.8（不触发小跑），速度约 1.76 米/秒，0.9 秒走约 1.6 米
                _loco.SetVirtualInput(new Vector2(0f, 0.8f), 0f);
                Next(3);
                return;
            }

            if (_phase == 3)
            {
                _moveMaxSpeed = Mathf.Max(_moveMaxSpeed, _loco.CurrentSpeed);
                _wait += Time.deltaTime;
                if (_wait < 0.9f) return;
                _loco.ClearVirtualInput();
                float moved = Vector3.Distance(_rig.position, _moveStart);
                Check("头显摇杆移动真的让玩家走了（位移 ≥ 1.2 米）", moved >= 1.2f, moved.ToString("F2") + " 米");
                Check("移动速度在 1.5~4.0 米/秒区间（不是瞬移也不是爬）",
                    _moveMaxSpeed >= 1.5f && _moveMaxSpeed <= 4.0f, _moveMaxSpeed.ToString("F2") + " 米/秒");
                Check("移动后仍在地面（y 未被顶飞）", Mathf.Abs(_rig.position.y) < 0.3f,
                    _rig.position.y.ToString("F2"));
                if (!_shotMove) { Shot("vr29_mode_3xrmove.png"); _shotMove = true; }
                // 重定位：把玩家放回大厅原点，检查眼高
                _spawn = new Vector3(3.20f, 0f, 1.20f);
                _loco.RecenterTo(_spawn, 90f, true);
                Next(4);
                return;
            }

            if (_phase == 4)
            {
                _wait += Time.deltaTime;
                if (_wait < 0.5f) return;
                float dh = Vector2.Distance(new Vector2(_rig.position.x, _rig.position.z),
                                            new Vector2(_spawn.x, _spawn.z));
                Check("重定位把玩家送回指定站位（水平误差 ≤ 0.2 米）", dh <= 0.2f, dh.ToString("F2") + " 米");
                Check("重定位后眼高正常（1.4~1.8 米）", _cam.position.y >= 1.4f && _cam.position.y <= 1.8f,
                    _cam.position.y.ToString("F2") + " 米");
                if (!_shotRecenter) { Shot("vr29_mode_4recenter.png"); _shotRecenter = true; }

                _mode.ForceKind(RuntimeDeviceMode.DeviceKind.Simulator);
                Next(5);
                return;
            }

            if (_phase == 5)
            {
                _wait += Time.deltaTime;
                if (_wait < 0.9f) return;
                Say("强制模拟器后：" + _mode.DetailLine);
                Check("强制模拟器 → 判定为 XR 模拟器", _mode.deviceKind == RuntimeDeviceMode.DeviceKind.Simulator);
                Check("强制模拟器 → 切回键鼠层", _desk.enabled);
                Check("强制模拟器 → 手柄层关闭（模拟器不许冒充真机接管）", !_vr.enabled);
                Check("强制模拟器 → 移动层键盘调试可用", _loco.keyboardDebug && !_loco.xrControlMode);
                Check("强制模拟器 → 手柄模型仍可见（模拟器里它是「手在哪」的参照）", ControllerVisible());
                Check("强制模拟器 → VR 提示条收起", !_vrhud.Visible);
                string hud = HudText();
                Check("底部 HUD 写明「XR 模拟器」", hud.Contains("XR 模拟器"), hud.Length > 0 ? "" : "HUD 文本为空");
                if (!_shotSim) { Shot("vr29_mode_5simulator.png"); _shotSim = true; }
                Next(6);
                return;
            }

            if (_phase == 6)
            {
                _wait += Time.deltaTime;
                if (_wait < 0.4f) { _mode.ClearForce(); return; }
                if (_wait < 1.2f) return;
                Say("回到自动检测：" + _mode.DetailLine);
                Check("清除手动覆盖后回到自动判定（桌面模式）",
                    !_mode.forced && _mode.deviceKind == RuntimeDeviceMode.DeviceKind.Desktop);
                Check("自动模式下键鼠层重新接管", _desk.enabled && !_vr.enabled);
                Check("自动模式下底部 HUD 重新显示「桌面模式」", HudText().Contains("桌面模式"));
                if (!_shotAuto) { Shot("vr29_mode_6auto.png"); _shotAuto = true; }
                Say("切换次数=" + _mode.SwitchCount + " · 通过=" + (Fails.Count == 0 ? "是" : "否"));
                Finish(Fails.Count == 0 ? 0 : 1);
            }
        }

        // ------------------------------------------------------------------
        static bool ControllerVisible()
        {
            bool l = _visL != null && _visL.Visible;
            bool r = _visR != null && _visR.Visible;
            return l || r || (_visL == null && _visR == null);
        }

        static string HudText()
        {
            if (_desk == null) return "";
            var f = typeof(DesktopDrillInput).GetField("_hudText", BindingFlags.Instance | BindingFlags.NonPublic);
            var t = f != null ? f.GetValue(_desk) as Text : null;
            if (t == null) return "";
            string s = (t.text ?? "").Replace("\n", " ");
            return s.Length > 400 ? s.Substring(0, 400) : s;
        }

        static void Shot(string file)
        {
            if (_cam == null) return;
            var cam = _cam.GetComponent<Camera>();
            if (cam == null) return;

            // 桌面 HUD 是 ScreenSpaceOverlay：直接渲染到 RenderTexture 时不会出现。
            // 出图前临时切到 ScreenSpaceCamera（同尺寸、同排序），出图后切回去，
            // 这样截图才是"玩家实际看到的画面"。
            var hud = DesktopHudCanvas();
            bool switched = false;
            if (hud != null && hud.renderMode == RenderMode.ScreenSpaceOverlay)
            {
                hud.renderMode = RenderMode.ScreenSpaceCamera;
                hud.worldCamera = cam;
                hud.planeDistance = 0.3f;
                switched = true;
            }

            // 必须带 24 位深度缓冲：0 深度位的 RT 里所有物体都"没有前后关系"，
            // 世界空间 UI 会穿透墙面显示，截图会看起来像穿墙 bug
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var prev = cam.targetTexture;
            cam.targetTexture = rt;
            Canvas.ForceUpdateCanvases();
            cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = prev;
            File.WriteAllBytes(Path.Combine(ShotDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();

            if (switched)
            {
                hud.renderMode = RenderMode.ScreenSpaceOverlay;
                hud.worldCamera = null;
            }
            Say("出图 " + file);
        }

        static Canvas DesktopHudCanvas()
        {
            if (_desk == null) return null;
            var f = typeof(DesktopDrillInput).GetField("_hudCanvas", BindingFlags.Instance | BindingFlags.NonPublic);
            return f != null ? f.GetValue(_desk) as Canvas : null;
        }
    }
}
#endif
