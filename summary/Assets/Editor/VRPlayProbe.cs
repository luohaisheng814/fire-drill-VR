#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using Unity.XR.CoreUtils;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 头显（虚拟 HMD）播放态验收探针：没有真机时用 VRDrillInput / XRDrillLocomotion 的虚拟输入通道，
    /// 以玩家视角(相机)渲染关键帧，逐段验证「A 开始 → 左摇杆位移/右摇杆瞬转 → 问答 → Grip 抓取 →
    /// 扳机拔销/压把/持续喷射 → 扑灭 → 蹲姿 → 复燃灭火 → 走进安全出口 → 结束页」。
    /// 说明：办公场景有隔墙，直线推进会被墙体挡住，因此除「空地位移/瞬转」外，机位用脚本就近安置，
    /// 交互动作仍全部走头显输入层（扳机、摇杆按下、A/B/C）。
    /// 批处理：-executeMethod FireSafety.EditorTools.VRPlayProbe.RunFromBatch（不要加 -nographics / -quit）
    /// </summary>
    public static class VRPlayProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 150f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static XROrigin _rig;
        static Transform _rigRoot, _rightHand;
        static Camera _cam;
        static VRDrillInput _vr;
        static XRDrillLocomotion _loco;
        static StageTwoFlow _flow;
        static Extinguisher _ext1, _held;
        static FireSource _fire1, _fire2;
        static ExitDoor _door;
        static StageOneCompletePanel _panel;
        static EndingScreen _ending;

        static bool _started;
        static int _phase;
        static float _phaseT, _clock0, _walkYaw;
        static Vector3 _walkStart;
        static float _lastProgress = -1f;
        static bool _shotStart, _shotQuiz, _shotSpray, _shotDoor, _shotEnd;
        static bool _shotHands;
        static int _shotQuizFrame = -1;
        static VRControllerVisual _visLeft, _visRight;
        static RectTransform panelForDebug;
        static Transform _leftHand;
        static float _quizOpenT = -1f, _doorOpenAt = -1f, _endingAt = -1f;
        static string _lastMsg = ""; static float _lastMsgT;

        // ------------------------------------------------------------------
        [MenuItem("FireSafety/VR 播放态探针（虚拟头显 · 玩家视角）")]
        public static void RunFromMenu() => RunFromBatch();

        public static void RunFromBatch()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            EditorSceneManager.OpenScene(ScenePath);
            _clock0 = Time.realtimeSinceStartup;
            _started = false;
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s)
        {
            if (s == _lastMsg && Time.realtimeSinceStartup - _lastMsgT < 0.6f) return;
            _lastMsg = s; _lastMsgT = Time.realtimeSinceStartup;
            Debug.Log("[VRPROBE] " + s);
            Log.Add(s);
        }

        static void Fail(string s)
        {
            if (!_failSeen.Add(s)) return;          // 同一断言只记一次，避免高帧率刷屏
            Debug.LogError("[VRPROBE] ✘ " + s); Log.Add("✘ " + s); Fails.Add(s);
        }
        static readonly HashSet<string> _failSeen = new HashSet<string>();

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ShotDir, "_vrprobe.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void NextPhase()
        {
            _phase++;
            _phaseT = Time.realtimeSinceStartup;
            _lastProgress = -1f;
        }

        static void Teleport(Vector3 p, string why)
        {
            _loco.SetVirtualInput(Vector2.zero, 0f);
            Vector3 cam = CamPos();
            Vector3 d = new Vector3(p.x - cam.x, 0f, p.z - cam.z);

            // 落点先对地：把 rig 的 Y 直接压到目标处地板高度上。
            // 否则 rig 的 Y 会继承之前的下坠（实测能掉到 -452 米，喷嘴跟着一路远离火源）。
            RaycastHit hit;
            float groundY = _rigRoot.position.y;
            if (Physics.Raycast(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, out hit, 30f, ~0,
                                 QueryTriggerInteraction.Ignore))
                groundY = hit.point.y + 0.02f;

            // XRDrillLocomotion 每帧用 CharacterController.Move 推进（含重力），直接改 transform
            // 会被它的内部位置状态顶回去，瞬移期间先关掉，落地后再开。
            var ccs = _rigRoot.GetComponentsInChildren<CharacterController>(true);
            foreach (var c in ccs) c.enabled = false;
            _rigRoot.position = new Vector3(_rigRoot.position.x + d.x, groundY, _rigRoot.position.z + d.z);
            Physics.SyncTransforms();
            foreach (var c in ccs) c.enabled = true;

            Say("机位安置到" + why + "（办公场景隔墙，直线推杆会被挡住，就近站位）· 眼高 "
                + (_cam.transform.position.y - groundY).ToString("F2") + " 米");
        }

        static Vector3 CamPos() => _cam != null ? _cam.transform.position : _rigRoot.position;

        static float Flat(Vector3 a, Vector3 b) => new Vector2(a.x - b.x, a.z - b.z).magnitude;

        // ------------------------------------------------------------------
        static void Tick()
        {
            if (!EditorApplication.isPlaying)
            {
                if (_started) Finish(Fails.Count == 0 ? 0 : 1);
                return;
            }

            if (!_started)
            {
                if (Time.realtimeSinceStartup - _clock0 > HardTimeout) { Fail("播放态启动超时"); Finish(1); return; }
                if (!Resolve()) return;
                _started = true;
                NextPhase();
                Say("进入播放态：XR Origin=" + _rig.name + "，相机眼高 " + _cam.transform.position.y.ToString("F2")
                    + " 米；无真机（XRSettings.isDeviceActive=" + UnityEngine.XR.XRSettings.isDeviceActive
                    + "），走虚拟头显输入通道");
                return;
            }

            if (Time.realtimeSinceStartup - _phaseT > HardTimeout) { Fail("阶段 " + _phase + " 超时"); Finish(1); return; }

            PoseHand();

            switch (_phase)
            {
                case 1: P1_Start(); break;
                case 2: P2_Locomotion(); break;
                case 3: P3_Quiz(); break;
                case 4: P4_GrabAndPress(); break;
                case 5: P5_SprayOne(); break;
                case 6: P6_Continue(); break;
                case 7: P7_CrouchLobby(); break;
                case 8: P8_StageThree(); break;
                case 9: P9_Exit(); break;
                default: Finish(Fails.Count == 0 ? 0 : 1); break;
            }
        }

        /// <summary>
        /// 无真机时手柄停在默认位姿（正前方偏中），手里那瓶灭火器会正好盖住视线中心，
        /// 问答面板/火焰全被挡。这里按真人握持习惯把右手摆到右前下方，喷口仍然朝前。
        /// </summary>
        static void PoseHand()
        {
            if (_rightHand == null || _cam == null) return;
            Transform ct = _cam.transform;
            // 右前下方 0.26 m / 0.48 m：仍在 60° FOV 内（下偏 26.6°），手柄代理模型能出现在画面里
            _rightHand.position = ct.position + ct.rotation * new Vector3(0.26f, -0.24f, 0.48f);
        }

        static bool Resolve()
        {
            _rig = UnityEngine.Object.FindFirstObjectByType<XROrigin>(FindObjectsInactive.Include);
            if (_rig == null) return false;
            _rigRoot = _rig.Origin != null ? _rig.Origin.transform : _rig.transform;
            _cam = _rig.Camera != null ? _rig.Camera : Camera.main;
            if (_cam == null) return false;

            _vr = UnityEngine.Object.FindFirstObjectByType<VRDrillInput>(FindObjectsInactive.Include);
            _loco = UnityEngine.Object.FindFirstObjectByType<XRDrillLocomotion>(FindObjectsInactive.Include);
            _flow = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>(FindObjectsInactive.Include);
            _panel = UnityEngine.Object.FindFirstObjectByType<StageOneCompletePanel>(FindObjectsInactive.Include);
            _ending = UnityEngine.Object.FindFirstObjectByType<EndingScreen>(FindObjectsInactive.Include);
            _door = ExitDoor.Instance;

            foreach (var e in UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (!e.name.Contains("02") && _ext1 == null) _ext1 = e;
            foreach (var f in UnityEngine.Object.FindObjectsByType<FireSource>(FindObjectsSortMode.None))
                if (f.isActiveAndEnabled) { _fire1 = f; break; }

            foreach (var tr in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (tr.name == "RightHand Controller") _rightHand = tr;
            foreach (var tr in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (tr.name == "LeftHand Controller") _leftHand = tr;
            foreach (var v in UnityEngine.Object.FindObjectsByType<VRControllerVisual>(FindObjectsSortMode.None))
                if (v.side == HandSide.Left) _visLeft = v; else _visRight = v;

            Debug.Log("[VRPROBE] 诊断：_rig.Camera=" + (_rig.Camera != null ? _rig.Camera.name : "null")
                + " Camera.main=" + (Camera.main != null ? Camera.main.name : "null")
                + " locoRoot=" + (_loco.rigRoot != null ? _loco.rigRoot.name : _loco.name)
                + " camInOrigin=" + _cam.transform.IsChildOf(_rigRoot)
                + " camInLoco=" + _cam.transform.IsChildOf(_loco.transform)
                + " flowPlayer=" + (_flow.player != null ? _flow.player.name : "null")
                + " lobby=" + (_flow.arrows != null && _flow.arrows.lobbyTarget != null
                    ? _flow.arrows.lobbyTarget.position.ToString("F2") : "null"));

            return _vr != null && _loco != null && _ext1 != null && _flow != null && _fire1 != null && _rightHand != null;
        }

        // ------------------------------------------------------------------
        static void P1_Start()
        {
            _vr.virtualMode = true;
            _vr.vTrigger = false; _vr.vStickClick = false;
            _loco.SetVirtualInput(Vector2.zero, 0f);
            PoseHand();       // 手柄摆在身前，开始面板/提示条才会像真人举在眼前
            if (_leftHand != null && _cam != null)
                _leftHand.position = _cam.transform.position
                    + _cam.transform.rotation * new Vector3(-0.26f, -0.24f, 0.48f);

            float t = Time.realtimeSinceStartup - _phaseT;
            if (!_shotStart && t > 1.3f)
            {
                Shot("vr0_hud_start.png");
                Say("头显提示条：可见=" + _vr.hud.Visible + "，内容=" + _vr.hud.CurrentHint);

                // 手柄代理模型：真机里「看得见自己的手」是操作前提
                bool handsOk = _visLeft != null && _visRight != null &&
                               _visLeft.Built && _visRight.Built &&
                               _visLeft.Visible && _visRight.Visible;
                Say("双手柄模型：左手 Built=" + (_visLeft != null && _visLeft.Built)
                    + " 可见=" + (_visLeft != null && _visLeft.Visible)
                    + " / 右手 Built=" + (_visRight != null && _visRight.Built)
                    + " 可见=" + (_visRight != null && _visRight.Visible)
                    + "（各 " + (_visRight != null ? _visRight.PartCount : 0) + " 个几何体）");
                if (!handsOk) Fail("头显接管时双手柄模型未生成或不可见");

                _shotStart = true;
            }
            if (t < 1.6f) return;

            // 按 A：真人也会按一两下（开始面板后面可能还压着一层引导页），这里发 4 次短脉冲
            const float kPulse = 0.45f;           // 0.20 s 按下 + 0.25 s 松开
            float local = t - 1.6f;
            int idx = Mathf.FloorToInt(local / kPulse);
            bool started = _vr.startScreen != null && _vr.startScreen.Started;
            if (idx < 4 && !started)
            {
                float phase = local - idx * kPulse;
                _vr.vPrimary = phase < 0.20f;
                if (phase < 0.20f) Say("第 " + (idx + 1) + " 次按 A …");
                return;
            }
            _vr.vPrimary = false;
            if (t < 1.6f + kPulse * 4f + 0.1f) return;

            started = _vr.startScreen != null && _vr.startScreen.Started;
            Say("A 键开始演练：Started=" + started
                + "（startScreen=" + (_vr.startScreen != null) + "，引导页打开="
                + (_vr.onboarding != null && _vr.onboarding.IsOpen) + "）");
            if (!started) Fail("按 A 之后演练没有开始");
            _walkStart = CamPos();
            _dirStart = _walkStart;
            _dirIndex = 0;
            _dirTimes[0] = _dirTimes[1] = _dirTimes[2] = _dirTimes[3] = 0f;
            _walkYaw = float.NaN;
            NextPhase();
        }

        // 摇杆四向各推 1 秒，取最大位移（起始朝向可能正对隔墙/桌子）
        static readonly float[] _dirTimes = new float[4];
        static Vector3 _dirStart;
        static int _dirIndex;

        static void P2_Locomotion()
        {
            _vr.vPrimary = false;
            float t = Time.realtimeSinceStartup - _phaseT;

            // 每次批处理的起始朝向都不保证一致（场景根可能带旋转），先把 rig 摆正朝主火源，
            // 否则四向位移与后面的站位换算每次都会不同。
            if (!_oriented)
            {
                _oriented = true;
                Vector3 d = _fire1.transform.position - _rigRoot.position; d.y = 0f;
                if (d.sqrMagnitude > 0.01f) _rigRoot.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
                _dirStart = CamPos();
            }

            if (t < 4.0f)
            {
                int i = Mathf.FloorToInt(t);                 // 0..3
                if (i > _dirIndex) { _dirIndex = i; _dirStart = CamPos(); }
                Vector2 input = Vector2.zero;
                switch (_dirIndex)
                {
                    case 0: input = new Vector2(0f, 1f); break;
                    case 1: input = new Vector2(0f, -1f); break;
                    case 2: input = new Vector2(1f, 0f); break;
                    case 3: input = new Vector2(-1f, 0f); break;
                }
                _loco.SetVirtualInput(input, 0f);

                float seg = t - _dirIndex;
                if (seg > 0.9f && _dirTimes[_dirIndex] == 0f)
                {
                    float d = Flat(CamPos(), _dirStart);
                    _dirTimes[_dirIndex] = d;
                    Say("左摇杆推" + (_dirIndex == 0 ? "前" : _dirIndex == 1 ? "后" : _dirIndex == 2 ? "右" : "左")
                        + " 0.9 秒：位移 " + d.ToString("F2") + " 米");
                    if (_dirIndex == 0)
                        Say("推满摇杆小跑：IsSprinting=" + _loco.IsSprinting
                            + "，当前速度 " + _loco.CurrentSpeed.ToString("F2") + " 米/秒（基础 2.2 × 倍率 1.6）");
                }
                return;
            }

            _loco.SetVirtualInput(Vector2.zero, 0f);
            float best = 0f;
            foreach (var v in _dirTimes) best = Mathf.Max(best, v);
            Say("摇杆移动实测：三向最大位移 " + best.ToString("F2") + " 米/0.9 秒（约 " + (best / 0.9f).ToString("F1")
                + " 米/秒，配置 moveSpeed 2.2）");
            if (best < 0.8f) Fail("四向推杆都被挡住，最大位移 " + best.ToString("F2") + " 米");

            // 右摇杆瞬转（以相机 yaw 为准）
            if (t < 4.3f)
            {
                if (float.IsNaN(_walkYaw)) _walkYaw = _cam.transform.eulerAngles.y;
                _loco.SetVirtualInput(Vector2.zero, 1f);
                return;
            }

            _loco.SetVirtualInput(Vector2.zero, 0f);
            float yaw = Mathf.DeltaAngle(_walkYaw, _cam.transform.eulerAngles.y);
            Say("右摇杆瞬转：相机 Y 轴 " + yaw.ToString("F1") + "°（配置 snapAngle 45°）");
            if (Mathf.Abs(Mathf.Abs(yaw) - 45f) > 3f) Fail("瞬转不是 45°，实测 " + yaw.ToString("F1") + "°");

            TeleportNear(_ext1.transform.position, _fire1.transform.position, 0.9f, "主灭火器旁");
            NextPhase();
        }

        static bool _oriented;

        /// <summary>站到 from 点旁 0.9 米，并且是背对 awayTo 的一侧。</summary>
        static void TeleportNear(Vector3 from, Vector3 awayTo, float dist, string why)
        {
            Vector3 dir = from - awayTo;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = _cam.transform.forward;
            dir.y = 0f;
            Vector3 baseDir = dir.normalized;

            // 与 PlaceOnPlayerSide 同样的采样：只落在脚下有地板、身体不卡进墙/家具的位置。
            // 直接按"离物体 dist 米"落位会站到桌子/墙里面（实测整屏发黑，只能看到 HUD）。
            for (int i = 0; i < 16; i++)
            {
                float ang = (i % 2 == 0 ? 1f : -1f) * ((i + 1) / 2) * 22.5f;
                Vector3 d = Quaternion.Euler(0f, ang, 0f) * baseDir;
                for (float dd = dist; dd <= dist + 0.6f; dd += 0.2f)
                {
                    Vector3 cand = from + d * dd;
                    if (Standable(cand, from.y)) { Teleport(cand, why); return; }
                }
            }
            Fail("找不到能站人的落点：" + why);
        }

        /// <summary>站到 obj 旁 dist 米，并且落在玩家当前这一侧（不会穿到火源背后去）。</summary>
        static void PlaceOnPlayerSide(Vector3 obj, float dist, string why)
        {
            Vector3 dir = _rigRoot.position - obj;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = -_cam.transform.forward;
            dir.y = 0f;
            Vector3 baseDir = dir.normalized;

            // 只在"脚下确实有地板、身体没卡进墙/家具"的位置落位，否则会站到房间外，
            // 手持物掉落并一路下坠（实测能把喷嘴甩到 1.5 km 外）。
            for (int k = 0; k < 16; k++)
            {
                float ang = k == 0 ? 0f : ((k % 2 == 1) ? 1 : -1) * ((k + 1) / 2) * 22.5f;
                Vector3 cand = obj + (Quaternion.Euler(0f, ang, 0f) * baseDir).normalized * dist;
                if (!Standable(cand, obj.y)) continue;
                Teleport(cand, why);
                return;
            }
            Teleport(obj + baseDir * dist, why + "·无合适落点");
        }

        /// <summary>该点是否踩在地板上且身体没被墙/家具占住。</summary>
        static bool Standable(Vector3 p, float floorY)
        {
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(p.x, floorY + 3f, p.z), Vector3.down, out hit, 6f, ~0,
                                 QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(hit.point.y - floorY) > 0.8f) return false;
            return !Physics.CheckSphere(new Vector3(p.x, floorY + 0.9f, p.z), 0.25f, ~0,
                                        QueryTriggerInteraction.Ignore);
        }

        /// <summary>诊断问答面板的实际世界尺寸（判图看到文字跑到面板外时用来定位）。</summary>
        static void DumpQuiz(ExtinguisherQuizPanel q)
        {
            var canvas = q.transform.Find("Canvas_Quiz") as RectTransform;
            if (canvas == null) { Say("问答面板：找不到 Canvas_Quiz"); return; }
            var panel = canvas.Find("QuizPanel") as RectTransform;
            if (panel == null) { Say("问答面板：找不到 QuizPanel"); return; }
            panelForDebug = panel;
            var pimg = panel.GetComponent<UnityEngine.UI.Image>();
            if (pimg != null)
                Say("面板 Image：sprite=" + (pimg.sprite == null ? "null" : pimg.sprite.name)
                    + " type=" + pimg.type + " preserveAspect=" + pimg.preserveAspect
                    + " rect=" + pimg.rectTransform.rect.ToString("F0")
                    + " anchoredPos=" + pimg.rectTransform.anchoredPosition.ToString("F0")
                    + " 本地scale=" + pimg.rectTransform.localScale.ToString("F4")
                    + " canvasRect=" + canvas.rect.ToString("F0")
                    + " canvasWorldScale=" + canvas.lossyScale.ToString("F5"));

            foreach (var cv in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
            {
                if (cv.gameObject.name != "Canvas_Quiz") continue;
                Say("Canvas_Quiz 实例：active=" + cv.gameObject.activeInHierarchy
                    + " lossyScale=" + cv.transform.lossyScale.ToString("F5")
                    + " 父=" + (cv.transform.parent != null ? cv.transform.parent.name : "root")
                    + " 父lossy=" + (cv.transform.parent != null ? cv.transform.parent.lossyScale.ToString("F3") : "-"));
            }
            var corners = new Vector3[4];
            panel.GetWorldCorners(corners);
            float minX = float.MaxValue, maxX = float.MinValue, minY = float.MaxValue, maxY = float.MinValue;
            foreach (var c in corners)
            {
                Vector3 sp = _cam.WorldToScreenPoint(c);
                minX = Mathf.Min(minX, sp.x); maxX = Mathf.Max(maxX, sp.x);
                minY = Mathf.Min(minY, sp.y); maxY = Mathf.Max(maxY, sp.y);
            }
            Say("面板实测：canvasScale=" + canvas.localScale.ToString("F4")
                + " panelRect=" + panel.rect.size.ToString("F0")
                + " 世界宽=" + (corners[3] - corners[0]).magnitude.ToString("F2") + " 米"
                + " 世界高=" + (corners[1] - corners[0]).magnitude.ToString("F2") + " 米"
                + " 距相机=" + (panel.position - _cam.transform.position).magnitude.ToString("F2") + " 米"
                + " 屏幕=(" + minX.ToString("F0") + ".." + maxX.ToString("F0") + ", "
                + minY.ToString("F0") + ".." + maxY.ToString("F0") + ")");
            foreach (var tx in panel.GetComponentsInChildren<UnityEngine.UI.Text>(true))
            {
                var rt = tx.GetComponent<RectTransform>();
                Vector3[] tc = new Vector3[4];
                rt.GetWorldCorners(tc);
                string s = tx.text == null ? "" : tx.text.Replace("\n", " ");
                if (s.Length > 14) s = s.Substring(0, 14);
                Vector3 spo = _cam.WorldToScreenPoint(tc[0]);
                Say("  文本[" + s + "] 世界宽=" + (tc[3] - tc[0]).magnitude.ToString("F2")
                    + " 父=" + rt.parent.name
                    + " 屏幕左=" + spo.x.ToString("F0") + " 屏幕y=" + spo.y.ToString("F0"));
            }
        }

        static void P3_Quiz()
        {
            var q = _ext1.quizPanel;
            if (q == null) { Fail("灭火器缺问答面板"); NextPhase(); return; }
            float t = Time.realtimeSinceStartup - _phaseT;

            if (!q.IsOpen && !q.Passed && t < 2f) { q.Open(_rightHand); Say("右手射线点选灭火器 → 问答面板打开"); return; }
            if (q.IsOpen && _quizOpenT < 0f) _quizOpenT = Time.realtimeSinceStartup;
            if (!_shotQuiz && q.IsOpen && Time.realtimeSinceStartup - _quizOpenT > 0.45f)
            {
                Shot("vr2_quiz_panel.png"); _shotQuiz = true;
                DumpQuiz(q);

                // 调试用：把面板底色临时改成亮绿，确认背景矩形到底在哪（不进交付物）
                var bg = panelForDebug != null ? panelForDebug.GetComponent<UnityEngine.UI.Image>() : null;
                if (bg != null)
                {
                    var keep = bg.color;
                    bg.color = new Color(0f, 1f, 0f, 1f);
                    Canvas.ForceUpdateCanvases();
                    Shot("_quiz_bg_debug.png");

                    // 再看一遍：强制重建布局后拍一张，判断是不是布局/网格没跟上 rect
                    UnityEngine.UI.LayoutRebuilder.ForceRebuildLayoutImmediate(panelForDebug);
                    Canvas.ForceUpdateCanvases();
                    Shot("_quiz_rebuild.png");

                    // 再验一次：把面板拉近到 0.72 米重放，判断左边被挡是不是墙壁遮住了面板
                    q.distanceFromCamera = 0.72f;
                    q.PlaceInFrontOfCamera();
                    Canvas.ForceUpdateCanvases();
                    Shot("_quiz_near.png");
                    bg.color = keep;
                }
                _shotQuizFrame = Time.frameCount;
            }

            if (_shotQuizFrame > 0 && Time.frameCount == _shotQuizFrame + 10)
            {
                Shot("vr2b_quiz_late.png");
                Say("问答面板 10 帧后再拍一张（排查是否单帧批处理滞后）");
            }

            if (q.IsOpen && _shotQuiz && !q.IsAnsweredThisQuestion && q.QuestionIndex < q.questions.Count)
            {
                int c = q.questions[q.QuestionIndex].correctIndex;
                q.AnswerOption(c);
                Say("第 " + (q.QuestionIndex + 1) + " 题按 " + (char)('A' + c) + "，累计答对 " + q.CorrectCount);
            }

            if (_ext1.QuizPassed)
            {
                Say("问答通过（" + q.CorrectCount + "/5 = " + (q.CorrectCount / 5f * 100f).ToString("F0") + "%，阈值 60%）");
                NextPhase();
                return;
            }
            if (t > 30f) { Fail("问答未通过，答对 " + q.CorrectCount); NextPhase(); }
        }

        static void P4_GrabAndPress()
        {
            float t = Time.realtimeSinceStartup - _phaseT;
            if (!_ext1.grab.IsHeld)
            {
                _ext1.grab.GrabExternal(_rightHand, HandSide.Right);
                _held = _ext1;
                Say("Grip 抓取（XRI Select）→ IsHeld=" + _ext1.grab.IsHeld);
                _phaseT = Time.realtimeSinceStartup;
                return;
            }
            if (t < 0.5f) { _vr.vTrigger = true; return; }
            if (t < 0.9f) { _vr.vTrigger = false; return; }
            if (t < 1.4f) return;
            _vr.vTrigger = true;                 // 第二下压把并保持 → 持续喷射
            if (t < 2.0f) return;

            Say("扳机：第一下 PinPulled=" + _ext1.PinPulled
                + "，第二下压把=" + (_ext1.handleLever != null && _ext1.handleLever.PressedOnce)
                + "，IsSpraying=" + _ext1.IsSpraying);
            if (!_ext1.PinPulled) Fail("扳机未拔出保险销");
            if (!_ext1.IsSpraying) Fail("扳机压下压把后未开始喷射");

            PlaceOnPlayerSide(_fire1.transform.position, 1.9f, "火焰根部前 1.9 米");
            NextPhase();
        }

        static void P5_SprayOne()
        {
            _vr.vTrigger = true;
            if (!_held.grab.IsHeld)                  // 传送等异常导致脱手时先抓回，否则喷嘴会一路下坠
            {
                _held.grab.GrabExternal(_rightHand, HandSide.Right);
                Say("手持物脱手，已重新抓回：IsHeld=" + _held.grab.IsHeld);
                return;
            }
            AimAt(_held, _fire1.AimPoint);

            float prog = _fire1.progress01;
            float nd = DistanceNozzle(_held, _fire1.AimPoint);
            if (nd > 2.6f && !_diagDone)
            {
                _diagDone = true;
                Say("诊断：火源 " + _fire1.transform.position.ToString("F2")
                    + "，瞄准点 " + _fire1.AimPoint.ToString("F2")
                    + "，喷嘴 " + (_held.nozzle != null ? _held.nozzle.position.ToString("F2") : "null")
                    + "，灭火器 " + _held.transform.position.ToString("F2")
                    + "，相机 " + CamPos().ToString("F2")
                    + "，rig " + _rigRoot.position.ToString("F2")
                    + "，距离 " + nd.ToString("F2"));
            }
            // 传送被家具/墙挤开时（喷嘴离得太远），重摆一次再喷，避免"站位不达标"造成假失败
            if (nd > 2.6f && Time.realtimeSinceStartup - _phaseT < 8f)
            {
                PlaceOnPlayerSide(_fire1.transform.position, 1.6f, "火焰根部前 1.6 米（重摆）");
                return;
            }
            if (!_shotSpray && prog > 0.25f)
            {
                LookAtPoint(_fire1.AimPoint + Vector3.up * 0.15f);
                Shot("vr3_spray_at_fire.png");
                Say("玩家视角：白雾喷向火焰根部，进度 " + prog.ToString("F3") + "，喷嘴距离 " + nd.ToString("F2") + " 米");
                _shotSpray = true;
            }
            // 手柄模型 + 手持灭火器同框：验证「手上看得见东西」
            if (_shotSpray && !_shotHands && prog > 0.3f)
            {
                Shot("vr3b_hands_hold.png");
                Say("玩家视角：右手柄代理模型与手持灭火器同框（模型 " +
                    (_visRight != null ? _visRight.PartCount.ToString() : "0") + " 个几何体，可见=" +
                    (_visRight != null && _visRight.Visible) + "）");
                Say("触觉反馈：调用 " + VRHaptics.AttemptCount + " 次，实际下发 " + VRHaptics.SendCount
                    + " 次，无设备跳过 " + VRHaptics.SkipCount + " 次（" + VRHaptics.LastInfo + "）");
                if (_visRight == null || !_visRight.Visible) Fail("手持灭火器时右手柄模型不可见");
                if (VRHaptics.AttemptCount <= 0) Fail("抓取/拔销/压把没有触发任何触觉调用");
                _shotHands = true;
            }
            if (_lastProgress < 0f || Mathf.Abs(prog - _lastProgress) > 0.1f)
            {
                Say("灭火进度 " + prog.ToString("F3") + "（喷嘴 " + nd.ToString("F2") + " 米）");
                _lastProgress = prog;
            }

            if (_fire1.IsExtinguished)
            {
                _vr.vTrigger = false;
                Say("火焰熄灭（Progress=" + _fire1.progress01.ToString("F3") + "），阶段一完成面板=" + (_panel != null && _panel.IsOpen));
                NextPhase();
                return;
            }
            if (Time.realtimeSinceStartup - _phaseT > 40f) { Fail("喷射 40 秒未灭火，进度 " + prog.ToString("F3") + "，喷嘴距离 " + nd.ToString("F2")); NextPhase(); }
        }

        static void P6_Continue()
        {
            _vr.vTrigger = false;
            float t = Time.realtimeSinceStartup - _phaseT;
            _vr.vPrimary = t < 0.5f;
            if (t < 1.2f) return;
            Say("A 键继续往下：Stage=" + _flow.CurrentStage);
            if (_flow.CurrentStage != StageTwoFlow.Stage.Evacuating) Fail("未进入第二阶段，当前 " + _flow.CurrentStage);
            NextPhase();
        }

        static void P7_CrouchLobby()
        {
            _vr.vPrimary = false;
            float t = Time.realtimeSinceStartup - _phaseT;

            if (t < 0.3f) { _vr.vStickClick = true; return; }
            if (t < 1.0f) { _vr.vStickClick = false; return; }
            if (t >= 1.1f && !_lobbyDone)
            {
                _lobbyDone = true;
                Say("左摇杆按下切蹲姿：DesktopCrouch=" + _flow.DesktopCrouch + "，IsCrouched=" + _flow.IsCrouched);
                if (!_flow.DesktopCrouch) Fail("左摇杆按下未切换蹲姿");
                Transform lobby = _flow.arrows != null ? _flow.arrows.lobbyTarget : null;
                if (lobby == null) { Fail("找不到大厅指引目标"); NextPhase(); return; }
                Teleport(lobby.position, "大厅入口（蹲行终点）");
                _lobbyT = Time.realtimeSinceStartup;
                _lobbyNext = 0f;
                Say("到位后相机距大厅 " + Flat(CamPos(), lobby.position).ToString("F2")
                    + " 米，flow.player 距大厅 " + (_flow.player != null ? Flat(_flow.player.position, lobby.position).ToString("F2") : "null")
                    + " 米，判定阈值 " + _flow.lobbyEnterDistance.ToString("F2"));
            }

            if (_flow.CurrentStage == StageTwoFlow.Stage.StageThree ||
                _flow.CurrentStage == StageTwoFlow.Stage.ExitOpen || _flow.CurrentStage == StageTwoFlow.Stage.Done)
            {
                Say("蹲行到达大厅 → 进入第三阶段（Stage=" + _flow.CurrentStage + "），大厅复燃");
                NextPhase();
                return;
            }
            if (Time.realtimeSinceStartup - _phaseT > 12f) { Fail("蹲行到大厅后未进入第三阶段，Stage=" + _flow.CurrentStage); NextPhase(); }
            else if (_lobbyDone && Time.realtimeSinceStartup - _lobbyT > _lobbyNext)
            {
                _lobbyNext += 1f;
                Transform lb = _flow.arrows != null ? _flow.arrows.lobbyTarget : null;
                Say("等阶段三：Stage=" + _flow.CurrentStage + "，DesktopCrouch=" + _flow.DesktopCrouch
                    + "，IsCrouched=" + _flow.IsCrouched
                    + "，player 距大厅=" + (_flow.player != null && lb != null
                        ? Flat(_flow.player.position, lb.position).ToString("F2") : "null"));
            }
        }

        static int _s3; static float _s3T; static float _s3Prog = -1f; static bool _lobbyDone; static bool _diagDone;
        static float _lobbyT, _lobbyNext;

        static void P8_StageThree()
        {
            if (_s3 >= 7 || _flow.CurrentStage == StageTwoFlow.Stage.ExitOpen || _flow.CurrentStage == StageTwoFlow.Stage.Done)
            {
                _vr.vTrigger = false;
                Say("安全出口已解锁=" + (_door != null && _door.IsUnlocked) + "，Stage=" + _flow.CurrentStage);
                NextPhase();
                return;
            }
            if (_flow.CurrentStage != StageTwoFlow.Stage.StageThree)
            {
                if (Time.realtimeSinceStartup - _phaseT > 12f) { Fail("未进入第三阶段，当前 " + _flow.CurrentStage); NextPhase(); }
                return;
            }

            var spare = _flow.spareExtinguisher;
            _fire2 = _flow.stageTwoFire;
            if (spare == null || _fire2 == null) { Fail("第三阶段火源或备用灭火器缺失"); NextPhase(); return; }

            switch (_s3)
            {
                case 0:
                    TeleportNear(spare.transform.position, _fire2.transform.position, 0.7f, "大厅备用灭火器旁");
                    _s3 = 1; _s3T = Time.realtimeSinceStartup;
                    return;

                case 1:
                    {
                        var qp = spare.quizPanel;
                        if (qp == null) { _s3 = 2; return; }
                        if (!qp.IsOpen && !qp.Passed) { qp.Open(_rightHand); Say("备用灭火器问答面板打开"); return; }
                        if (qp.IsOpen && !qp.IsAnsweredThisQuestion && qp.QuestionIndex < qp.questions.Count)
                        {
                            int c = qp.questions[qp.QuestionIndex].correctIndex;
                            qp.AnswerOption(c);
                            return;
                        }
                        if (spare.QuizPassed) { Say("备用灭火器问答通过 → 可拔销"); _s3 = 2; _s3T = Time.realtimeSinceStartup; return; }
                        if (Time.realtimeSinceStartup - _s3T > 30f)
                        {
                            Fail("备用灭火器问答未通过，答对 " + qp.CorrectCount);
                            _s3 = 7;
                        }
                    }
                    return;

                case 2:
                    spare.grab.GrabExternal(_rightHand, HandSide.Right);
                    _held = spare;
                    Say("Grip 抓取备用灭火器：IsHeld=" + spare.grab.IsHeld);
                    _s3 = 3; _s3T = Time.realtimeSinceStartup;
                    return;

                case 3:   // 第一下：拔销
                    _vr.vTrigger = true;
                    if (Time.realtimeSinceStartup - _s3T > 0.5f) { _vr.vTrigger = false; _s3 = 4; _s3T = Time.realtimeSinceStartup; }
                    return;

                case 4:   // 第二下：压把并保持
                    if (Time.realtimeSinceStartup - _s3T < 0.5f) return;
                    _vr.vTrigger = true;
                    _s3 = 5; _s3T = Time.realtimeSinceStartup;
                    return;

                case 5:
                    if (Time.realtimeSinceStartup - _s3T < 0.6f) return;
                    Say("备用灭火器扳机：PinPulled=" + spare.PinPulled
                        + "，压把=" + (spare.handleLever != null && spare.handleLever.PressedOnce)
                        + "，IsSpraying=" + spare.IsSpraying);
                    if (!spare.PinPulled) Fail("备用灭火器未拔销");
                    if (!spare.IsSpraying) Fail("备用灭火器未喷射");
                    PlaceOnPlayerSide(_fire2.transform.position, 1.9f, "复燃火前 1.9 米");
                    _s3 = 6; _s3T = Time.realtimeSinceStartup;
                    return;

                case 6:
                    _vr.vTrigger = true;
                    AimAt(spare, _fire2.AimPoint);
                    if (DistanceNozzle(spare, _fire2.AimPoint) > 2.6f && Time.realtimeSinceStartup - _s3T < 8f)
                    {
                        PlaceOnPlayerSide(_fire2.transform.position, 1.6f, "复燃火前 1.6 米（重摆）");
                        return;
                    }
                    if (_s3Prog < 0f || Mathf.Abs(_fire2.progress01 - _s3Prog) > 0.1f)
                    {
                        _s3Prog = _fire2.progress01;
                        Say("复燃火进度 " + _fire2.progress01.ToString("F3") + "（喷嘴 " + DistanceNozzle(spare, _fire2.AimPoint).ToString("F2") + " 米）");
                    }
                    if (_fire2.IsExtinguished || _flow.CurrentStage != StageTwoFlow.Stage.StageThree)
                    {
                        _vr.vTrigger = false;
                        Say("复燃火扑灭，安全出口开启");
                        _s3 = 7; _s3T = Time.realtimeSinceStartup;
                        return;
                    }
                    if (Time.realtimeSinceStartup - _s3T > 40f) { Fail("复燃火未灭，进度 " + _fire2.progress01.ToString("F3")); _s3 = 7; }
                    return;
            }
        }

        static void P9_Exit()
        {
            _vr.vTrigger = false;
            float t = Time.realtimeSinceStartup - _phaseT;

            // 门开之后等门扇摆到位再出图（此时玩家正对着门站），出完图才走进门
            if (!_shotDoor)
            {
                if (_doorOpenAt < 0f) { _doorOpenAt = Time.realtimeSinceStartup; AimCameraAt(_door.transform.position + Vector3.up * 1.0f); }
                if (Time.realtimeSinceStartup - _doorOpenAt > 0.9f)
                {
                    Shot("vr4_door_open.png");
                    Say("玩家视角截图 vr4_door_open.png（门已开启，Stage=" + _flow.CurrentStage + "）");
                    _shotDoor = true;
                }
                return;
            }

            if (_flow.CurrentStage == StageTwoFlow.Stage.Done)
            {
                if (!_shotEnd && _ending != null && _ending.IsOpen)
                {
                    if (_endingAt < 0f) _endingAt = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - _endingAt > 0.5f) { Shot("vr5_ending.png"); _shotEnd = true; }
                    return;
                }
                Say("走进安全出口 → 演练完成：Stage=Done，结束页=" + (_ending != null && _ending.IsOpen));
                NextPhase();
                return;
            }

            if (t < 0.4f)
            {
                return;
            }
            _door.TryEnter();   // 等价于走进 0.9 米判定圈
            if (_flow.CurrentStage != StageTwoFlow.Stage.ExitOpen)
                Say("出口尚未开启，Stage=" + _flow.CurrentStage);
            if (t > 10f)
            {
                Fail("走到门口仍未通关，Stage=" + _flow.CurrentStage);
                NextPhase();
            }
        }

        // ------------------------------------------------------------------
        static float DistanceNozzle(Extinguisher e, Vector3 aim)
        {
            if (e == null || e.nozzle == null) return -1f;
            return Vector3.Distance(e.nozzle.position, aim);
        }

        /// <summary>转右手腕，让喷嘴朝向火焰根部（=玩家抬手瞄准）。</summary>
        static void AimAt(Extinguisher e, Vector3 aim)
        {
            if (e == null || e.nozzle == null || _rightHand == null) return;
            Vector3 dir = aim - e.nozzle.position;
            if (dir.sqrMagnitude < 0.0001f) return;
            Quaternion d = Quaternion.FromToRotation(e.nozzle.forward, dir.normalized);
            _rightHand.rotation = d * _rightHand.rotation;
        }

        /// <summary>只转水平朝向，让玩家视角正对某个点（用于出图取景），并把俯仰拉回水平。</summary>
        static void AimCameraAt(Vector3 p)
        {
            Vector3 cam = CamPos();
            Vector3 d = p - cam; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            float cur = _cam.transform.eulerAngles.y;
            float want = Quaternion.LookRotation(d.normalized, Vector3.up).eulerAngles.y;
            _rigRoot.rotation = Quaternion.Euler(0f, want - cur, 0f) * _rigRoot.rotation;
            _cam.transform.localRotation = Quaternion.identity;
        }

        /// <summary>连俯仰一起对准（模拟玩家低头/抬头看目标）。</summary>
        static void LookAtPoint(Vector3 p)
        {
            Vector3 d = p - _cam.transform.position;
            if (d.sqrMagnitude < 0.01f) return;
            Vector3 flat = d; flat.y = 0f;
            if (flat.sqrMagnitude > 0.01f)
            {
                _rigRoot.rotation = Quaternion.Euler(0f,
                    Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y - _cam.transform.eulerAngles.y, 0f)
                    * _rigRoot.rotation;
            }
            _cam.transform.rotation = Quaternion.LookRotation(d.normalized, Vector3.up);
        }

        static void Shot(string name)
        {
            if (_cam == null) return;
            var rt = new RenderTexture(1280, 720, 24, RenderTextureFormat.ARGB32);
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = null;
            var bytes = tex.EncodeToPNG();
            UnityEngine.Object.Destroy(tex);
            rt.Release();
            UnityEngine.Object.Destroy(rt);
            File.WriteAllBytes(Path.Combine(ShotDir, name), bytes);
            Say("玩家视角截图 " + name);
        }
    }
}
#endif
