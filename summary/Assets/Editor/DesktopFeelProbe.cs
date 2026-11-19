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
    /// 第 32 轮：桌面视角/移动手感探针（真实 Play，玩家视角出图）。
    ///
    /// 针对"运行起来视角自己漂、鼠标过于灵敏、一推就走/一松就定"的回归：
    ///   1. 100 像素水平位移 → 偏航 = 灵敏度 × 100（默认约 6°），不再是 12°；
    ///   2. 60 帧 0.3 像素手抖 → 偏航变化 < 0.1°（死区生效）；
    ///   3. 单帧 5000 像素突跳（切窗口）→ 被 mouseDeltaClamp 限幅，不会甩头；
    ///   4. 外部把相机转到 30°，再给鼠标位移 → 从 30° 继续加，不会跳回旧角度（漂移根因）；
    ///   5. 静置 1.5 秒 → 偏航零变化；桌面模式下 DrillTrackedPose 不写相机；
    ///   6. 摇杆零漂（0.1）→ 1.2 秒几乎不动；满杆起步有加速斜坡；松手有减速；
    ///   7. 出图：转过 360 像素后的视角 + 静置后的视角，确认画面没有转飞。
    ///
    /// 批处理：-executeMethod FireSafety.EditorTools.DesktopFeelProbe.RunFromBatch（不加 -nographics / -quit）
    /// </summary>
    public static class DesktopFeelProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 180f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly HashSet<string> _seen = new HashSet<string>();

        static RuntimeDeviceMode _mode;
        static DesktopDrillInput _desk;
        static XRDrillLocomotion _loco;
        static DrillTrackedPose _pose;
        static Transform _cam, _rig;
        static StartScreen _start;

        static int _phase;
        static float _t0, _phaseT, _wait;
        static Vector3 _moveStart;
        static Vector3 _camPos0;
        static float _yawAtIdle, _speedAtRelease;
        static float _earlySpeed, _maxSpeed;
        static bool _shotLook, _shotIdle;

        [MenuItem("FireSafety/桌面视角与移动手感探针（玩家视角）")]
        public static void RunFromMenu() => RunFromBatch();

        public static void RunFromBatch()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear(); _seen.Clear();
            _phase = 0; _wait = 0f;
            _shotLook = _shotIdle = false;
            EditorSceneManager.OpenScene(ScenePath);
            _t0 = Time.realtimeSinceStartup;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[FEELPROBE] " + s); Log.Add(s); }

        static void Fail(string s)
        {
            if (!_seen.Add(s)) return;
            Debug.LogError("[FEELPROBE] ✘ " + s);
            Log.Add("✘ " + s);
            Fails.Add(s);
        }

        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("✔ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ShotDir, "_feelprobe.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Next(int p)
        {
            _phase = p;
            _phaseT = Time.realtimeSinceStartup;
            _wait = 0f;
            Say("→ 阶段 " + p);
        }

        static void Tick()
        {
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Fail("总超时"); Finish(1); return; }

            if (_phase == 0)
            {
                if (!Application.isPlaying) return;
                _mode = Object.FindObjectOfType<RuntimeDeviceMode>();
                _desk = Object.FindObjectOfType<DesktopDrillInput>();
                _loco = Object.FindObjectOfType<XRDrillLocomotion>();
                _pose = _cam != null ? _cam.GetComponent<DrillTrackedPose>() : null;
                _cam = Camera.main != null ? Camera.main.transform : null;
                _rig = _mode != null && _mode.rigRoot != null ? _mode.rigRoot : null;
                _start = Object.FindObjectOfType<StartScreen>();

                if (_cam == null) _cam = Camera.main != null ? Camera.main.transform : null;
                if (_pose == null && _cam != null) _pose = _cam.GetComponent<DrillTrackedPose>();

                bool ready = _mode != null && _desk != null && _loco != null && _cam != null;
                if (!ready)
                {
                    _wait += Time.deltaTime;
                    if (_wait > 10f) { Fail("组件未就绪"); Finish(1); }
                    return;
                }
                if (_start != null && !_start.DrillStarted) _start.StartGame();
                Next(1);
                return;
            }

            if (_phase == 1)
            {
                _wait += Time.deltaTime;
                if (_wait < 1.0f) return;

                Say("检测：" + _mode.DetailLine);
                Check("纯桌面模式（本机无头显会话）", _mode.deviceKind == RuntimeDeviceMode.DeviceKind.Desktop);
                Check("桌面模式相机不写 XR 位姿（DrillTrackedPose.HasPose=false）",
                      _pose == null || !_pose.HasPose);

                _cam.localRotation = Quaternion.identity;
                float yaw0 = _cam.localEulerAngles.y;
                Look(100f);
                float dy = Mathf.DeltaAngle(yaw0, _cam.localEulerAngles.y);
                Check("100 像素水平位移 ≈ 灵敏度×100（6° 量级）",
                      Mathf.Abs(dy - _desk.mouseLookSensitivity * 100f) < 1.2f, dy.ToString("F2") + "°");

                yaw0 = _cam.localEulerAngles.y;
                for (int i = 0; i < 60; i++) _desk.ApplyLook(0.3f, 0f);
                Check("60 帧手抖（0.3 像素）不累积（< 0.1°）",
                      Mathf.Abs(Mathf.DeltaAngle(yaw0, _cam.localEulerAngles.y)) < 0.1f,
                      Mathf.Abs(Mathf.DeltaAngle(yaw0, _cam.localEulerAngles.y)).ToString("F3") + "°");

                _desk.ApplyLook(5000f, 0f);
                float spike = Mathf.Abs(Mathf.DeltaAngle(yaw0, _cam.localEulerAngles.y));
                Check("5000 像素突跳被限幅（≤ 60 像素当量）",
                      spike <= _desk.mouseDeltaClamp * _desk.mouseLookSensitivity + 0.2f, spike.ToString("F2") + "°");

                // 外部改朝向（重定位 / XR 位姿）后，鼠标必须从当前朝向继续，而不是跳回缓存角度
                _cam.localRotation = Quaternion.Euler(0f, 30f, 0f);
                Look(100f);
                float fromExternal = Mathf.DeltaAngle(30f, _cam.localEulerAngles.y);
                Check("外部转到 30° 后，鼠标从 30° 继续（无跳回）",
                      Mathf.Abs(fromExternal - _desk.mouseLookSensitivity * 100f) < 1.2f,
                      fromExternal.ToString("F2") + "°");

                // 第 33 轮 API：运行时读写同一组手感参数
                DrillInputAPI.LookConfig lc;
                bool lookOk = DrillInputAPI.GetLook(out lc);
                Check("API：GetLook 可读（灵敏度 > 0）", lookOk && lc.yawSensitivity > 0f,
                      lookOk ? lc.yawSensitivity.ToString("F3") : "不可读");
                bool writeOk = lookOk && DrillInputAPI.SetLook(lc.yawSensitivity, lc.pitchSensitivity);
                Check("API：SetLook 写回后参数一致",
                      writeOk && Mathf.Abs(_desk.mouseLookSensitivity - lc.yawSensitivity) < 0.0001f);
                DrillInputAPI.MoveConfig mc;
                Check("API：GetMove 可读（移动速度 > 0）", DrillInputAPI.GetMove(out mc) && mc.moveSpeed > 0f);

                // 第 33 轮：TrackedPoseDriver 在桌面必须全停，否则会和鼠标争相机
                var tpds = _cam.GetComponentsInChildren<UnityEngine.InputSystem.XR.TrackedPoseDriver>(true);
                bool allOff = true;
                for (int i = 0; i < tpds.Length; i++) if (tpds[i] != null && tpds[i].enabled) allOff = false;
                Check("桌面：相机上的 XR TrackedPoseDriver 全部停用", allOff, tpds.Length + " 个");

                // 出图：连续 6 次 60 像素 = 360 像素当量，画面应只转约 21.6°，不会转飞
                for (int i = 0; i < 6; i++) _desk.ApplyLook(60f, 0f);
                if (!_shotLook) { Shot("vr33_feel_look.png"); _shotLook = true; }

                _yawAtIdle = _cam.localEulerAngles.y;
                _moveStart = _rig != null ? _rig.position : Vector3.zero;
                _camPos0 = _cam.localPosition;
                _cam.localPosition = _camPos0 + new Vector3(0.25f, 0.30f, 0.40f);   // 模拟被 XR/角色控制器推走
                Next(2);
                return;
            }

            // 静置：不碰鼠标，视角与位置都不该动
            if (_phase == 2)
            {
                _wait += Time.deltaTime;
                if (_wait < 1.5f) return;
                Check("静置 1.5 秒视角零漂移（< 0.1°）",
                      Mathf.Abs(Mathf.DeltaAngle(_yawAtIdle, _cam.localEulerAngles.y)) < 0.1f,
                      Mathf.Abs(Mathf.DeltaAngle(_yawAtIdle, _cam.localEulerAngles.y)).ToString("F3") + "°");
                Check("桌面：相机本地位置被锁回（无平移漂移）",
                      Vector3.Distance(_cam.localPosition, _camPos0) < 0.02f,
                      Vector3.Distance(_cam.localPosition, _camPos0).ToString("F3") + " 米");
                if (!_shotIdle) { Shot("vr33_feel_idle.png"); _shotIdle = true; }

                if (_rig == null) { Fail("找不到玩家锚点，跳过后半段"); Finish(1); return; }
                _moveStart = _rig.position;
                _loco.SetVirtualInput(new Vector2(0.1f, 0.05f), 0.05f);   // 零漂量级
                Next(3);
                return;
            }

            // 摇杆零漂：1.2 秒内位移应几乎为零
            if (_phase == 3)
            {
                _wait += Time.deltaTime;
                if (_wait < 1.2f) return;
                float drift = Vector3.Distance(_moveStart, _rig.position);
                _loco.ClearVirtualInput();
                Check("摇杆零漂（0.1）1.2 秒位移 < 0.05 米", drift < 0.05f, drift.ToString("F3") + " 米");
                Check("零漂时速度保持 0（不产生巡航）", _loco.CurrentSpeed < 0.05f,
                      _loco.CurrentSpeed.ToString("F3") + " 米/秒");

                // 挪到门外广场（x 7.25~13.75 / z -4~4，地面平整且无家具）：保证 1 秒 + 滑行有足够直线
                if (_rig != null)
                {
                    _rig.position = new Vector3(9.0f, 0f, 0f);
                    _rig.rotation = Quaternion.Euler(0f, 90f, 0f);
                    // 只测输入惯性：临时关掉 CharacterController 的碰撞，避免广场花箱/灯柱把滑行挡成 0
                    var cc = _rig.GetComponent<CharacterController>();
                    if (cc != null) cc.enabled = false;
                }
                _moveStart = _rig.position;
                _earlySpeed = 0f;
                _maxSpeed = 0f;
                _loco.SetVirtualInput(new Vector2(0f, 1f), 0f);
                Next(4);
                return;
            }

            // 满杆起步：先加速、后到巡航速度
            if (_phase == 4)
            {
                _wait += Time.deltaTime;
                _maxSpeed = Mathf.Max(_maxSpeed, _loco.CurrentSpeed);
                if (_wait < 0.12f) _earlySpeed = Mathf.Max(_earlySpeed, _loco.CurrentSpeed);
                if (_wait < 1.0f) return;

                Say("起步 0.12 秒速度=" + _earlySpeed.ToString("F2") + "，1 秒内峰值=" + _maxSpeed.ToString("F2"));
                Check("起步有加速斜坡（0.12 秒时低于巡航速度的 80%）",
                      _earlySpeed > 0f && _earlySpeed < _loco.moveSpeed * 0.8f);
                Check("巡航速度回到 2.1~2.3 米/秒（满杆不按 Shift）",
                      _maxSpeed >= 2.05f && _maxSpeed <= 2.35f, _maxSpeed.ToString("F2"));
                Check("满杆 1 秒位移 ≥ 1.4 米", Vector3.Distance(_moveStart, _rig.position) >= 1.4f,
                      Vector3.Distance(_moveStart, _rig.position).ToString("F2") + " 米");

                _speedAtRelease = _loco.CurrentSpeed;
                _moveStart = _rig.position;
                _loco.ClearVirtualInput();
                Next(5);
                return;
            }

            // 松手：速度收回，但不瞬停
            if (_phase == 5)
            {
                _wait += Time.deltaTime;
                if (_wait < 0.6f) return;
                float tail = Vector3.Distance(_moveStart, _rig.position);
                Say("松手前速度=" + _speedAtRelease.ToString("F2") + "，0.6 秒滑行=" + tail.ToString("F2"));
                Check("松手后速度归零（< 0.05 米/秒）", _loco.CurrentSpeed < 0.05f,
                      _loco.CurrentSpeed.ToString("F3"));
                Check("松手有短滑行（0.05~0.6 米），不是瞬停也不是漂很远", tail >= 0.05f && tail <= 0.6f,
                      tail.ToString("F2") + " 米");
                Say("通过=" + (Fails.Count == 0 ? "是" : "否"));
                Finish(Fails.Count == 0 ? 0 : 1);
            }
        }

        /// <summary>按单帧上限分段施加水平位移，模拟多帧连续鼠标移动。</summary>
        static void Look(float totalPixels)
        {
            float remaining = Mathf.Abs(totalPixels);
            float sign = Mathf.Sign(totalPixels);
            while (remaining > 0f)
            {
                float step = Mathf.Min(_desk.mouseDeltaClamp, remaining);
                _desk.ApplyLook(sign * step, 0f);
                remaining -= step;
            }
        }

        static void Shot(string file)
        {
            if (_cam == null) return;
            var cam = _cam.GetComponent<Camera>();
            if (cam == null) return;

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
            Object.DestroyImmediate(tex);
            rt.Release();
            Say("出图 " + file);
        }
    }
}
#endif
