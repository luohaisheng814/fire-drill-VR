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
    /// 以玩家视角(相机)渲染关键帧，逐段验证本轮重构后的流程：
    ///   开始页三个入口 → 天花板 → 操作演练（原第一阶段：问答/拔销/压把/喷射）→ 完成面板 →
    ///   正式第 1 阶段（随机出生 + 立即烟雾 + 设备火 + 火焰血量条 + 未断电喷射无效 → 断电 →
    ///   灭火器问答 → 灭火）→ 第 2 阶段（安全标识/箭头 → 出口火 → 疏散问答 → 灭火开门）→ 结束页。
    /// 批处理：-executeMethod FireSafety.EditorTools.VRPlayProbe.RunFromBatch（不要加 -nographics / -quit）
    /// </summary>
    public static class VRPlayProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 240f;
        const float PhaseTimeout = 100f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static readonly HashSet<string> _failSeen = new HashSet<string>();

        static XROrigin _rig;
        static Transform _rigRoot, _rightHand, _leftHand;
        static Camera _cam;
        static VRDrillInput _vr;
        static XRDrillLocomotion _loco;
        static DrillScenario _sc;
        static StartScreen _start;
        static StageTwoFlow _flow;
        static StageOneCompletePanel _panel;
        static EndingScreen _ending;
        static ExitDoor _door;
        static PowerSwitch _power;
        static Extinguisher _ext1, _ext2, _ext3, _held;
        static FireSource _tutFire, _deviceFire, _exitFire;
        static FireHealthBar _hpDevice;
        static VRControllerVisual _visLeft, _visRight;

        static bool _started;
        static int _phase, _sub;
        static bool _subInit;
        static float _phaseT, _clock0, _lastProgress = -1f, _lastMsgT, _subT;
        static string _lastMsg = "";

        // 单个「灭火单元」内部状态机
        static int _uStep, _uSub;
        static float _uStepT, _uSubT, _progA = -1f;
        static bool _unitShotQuiz, _unitShotMid, _unitShotLocked;

        static bool _shotStart, _shotCeiling, _shotTutorial, _shotPanel;
        static bool _shotSpawn, _shotDevice, _shotStage2, _shotExitFire, _shotDoor, _shotEnd;
        static float _shotAt;
        static float _walkYaw;

        // ------------------------------------------------------------------
        [MenuItem("FireSafety/VR 播放态探针（虚拟头显 · 玩家视角）")]
        public static void RunFromMenu() => RunFromBatch();

        public static void RunFromBatch()
        {
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear(); _failSeen.Clear();
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
            if (!_failSeen.Add(s)) return;
            Debug.LogError("[VRPROBE] ✘ " + s); Log.Add("✘ " + s); Fails.Add(s);
        }

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
            _sub = 0;
            _subInit = false;
            _shotAt = 0f;
            _vr.vTrigger = false;
            _vr.vPrimary = false;
            _vr.vStickClick = false;
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

            if (Time.realtimeSinceStartup - _phaseT > PhaseTimeout) { Fail("阶段 " + _phase + " 超时"); Finish(1); return; }

            PoseHand();

            switch (_phase)
            {
                case 1: P1_Start(); break;
                case 2: P2_CeilingAndMove(); break;
                case 3: P3_TutorialUnit(); break;
                case 4: P4_EnterFormal(); break;
                case 5: P5_DeviceLook(); break;
                case 6: P6_Stage1Unit(); break;
                case 7: P7_Stage2(); break;
                case 8: P8_ExitUnit(); break;
                case 9: P9_Exit(); break;
                default: Finish(Fails.Count == 0 ? 0 : 1); break;
            }
        }

        /// <summary>无真机时把右手摆在右前下方，避免手持灭火器盖住视线中心。</summary>
        static float _handDrop;
        static void PoseHand()
        {
            if (_rightHand == null || _cam == null) return;
            Transform ct = _cam.transform;
            _rightHand.position = ct.position + ct.rotation * new Vector3(0.26f, -0.24f - _handDrop, 0.48f);
            if (_leftHand != null)
                _leftHand.position = ct.position + ct.rotation * new Vector3(-0.26f, -0.24f - _handDrop, 0.48f);
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
            _sc = DrillScenario.Instance != null ? DrillScenario.Instance
                : UnityEngine.Object.FindFirstObjectByType<DrillScenario>(FindObjectsInactive.Include);
            _flow = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>(FindObjectsInactive.Include);
            _panel = UnityEngine.Object.FindFirstObjectByType<StageOneCompletePanel>(FindObjectsInactive.Include);
            _ending = UnityEngine.Object.FindFirstObjectByType<EndingScreen>(FindObjectsInactive.Include);
            _door = ExitDoor.Instance;
            if (_sc != null)
            {
                _start = _sc.startScreen;
                _power = _sc.powerSwitch;
                _tutFire = _sc.tutorialFire;
                _deviceFire = _sc.deviceFire;
                _ext1 = _sc.tutorialExtinguisher;
                _ext2 = _sc.deviceExtinguisher;
                _ext3 = _sc.exitExtinguisher;
            }
            if (_start == null) _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>(FindObjectsInactive.Include);

            foreach (var e in UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
            {
                if (_ext2 == null && e.name.Contains("02")) _ext2 = e;
                if (_ext3 == null && e.name.Contains("03")) _ext3 = e;
            }

            foreach (var tr in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (tr.name == "RightHand Controller") _rightHand = tr;
            foreach (var tr in UnityEngine.Object.FindObjectsByType<Transform>(FindObjectsSortMode.None))
                if (tr.name == "LeftHand Controller") _leftHand = tr;
            foreach (var v in UnityEngine.Object.FindObjectsByType<VRControllerVisual>(FindObjectsSortMode.None))
                if (v.side == HandSide.Left) _visLeft = v; else _visRight = v;

            if (_deviceFire != null)
                foreach (var b in UnityEngine.Object.FindObjectsByType<FireHealthBar>(FindObjectsSortMode.None))
                    if (b.fire == _deviceFire) _hpDevice = b;

            Debug.Log("[VRPROBE] 诊断：scenario=" + (_sc != null)
                + " spawnPoints=" + (_sc != null ? _sc.SpawnCount.ToString() : "null")
                + " deviceFire=" + (_deviceFire != null)
                + " powerSwitch=" + (_power != null)
                + " ext1/2/3=" + (_ext1 != null) + "/" + (_ext2 != null) + "/" + (_ext3 != null)
                + " hpBar=" + (_hpDevice != null)
                + " flow=" + (_flow != null));

            return _vr != null && _loco != null && _sc != null && _deviceFire != null
                && _ext1 != null && _ext2 != null && _ext3 != null && _rightHand != null && _cam != null;
        }

        // ------------------------------------------------------------------
        // 1. 开始页：三个入口（不按 A，改点「操作演练」）
        // ------------------------------------------------------------------
        static void P1_Start()
        {
            _vr.virtualMode = true;
            _vr.vTrigger = false; _vr.vStickClick = false; _vr.vPrimary = false;
            _loco.SetVirtualInput(Vector2.zero, 0f);
            PoseHand();

            float t = Time.realtimeSinceStartup - _phaseT;
            if (!_shotStart && t > 1.2f)
            {
                Shot("vr0_hud_start.png");
                _shotStart = true;

                bool handsOk = _visLeft != null && _visRight != null &&
                               _visLeft.Built && _visRight.Built &&
                               _visLeft.Visible && _visRight.Visible;
                Say("双手柄模型：左 Built=" + (_visLeft != null && _visLeft.Built)
                    + " 可见=" + (_visLeft != null && _visLeft.Visible)
                    + " / 右 Built=" + (_visRight != null && _visRight.Built)
                    + " 可见=" + (_visRight != null && _visRight.Visible));
                if (!handsOk) Fail("头显接管时双手柄模型未生成或不可见");

                Say("开始页入口：主按钮「" + (_start != null ? _start.buttonLabel : "?")
                    + "」· 教学「" + (_start != null ? _start.onboardingLabel : "?")
                    + "」· 操作演练「" + (_start != null ? _start.tutorialLabel : "?")
                    + "」（显示=" + (_start != null && _start.showTutorialButton) + "）");
                if (_start == null || !_start.showTutorialButton) Fail("开始页没有「操作演练」入口");

                // 出生点随机性：候选 2~3 个，连取 12 次应出现 ≥2 个不同下标
                var seen = new HashSet<int>();
                for (int i = 0; i < 12; i++) seen.Add(_sc.PickSpawnIndex());
                Say("出生点候选 " + _sc.SpawnCount + " 个，随机取样出现 " + seen.Count + " 种下标（" + seen + "）");
                if (_sc.SpawnCount < 2 || _sc.SpawnCount > 3) Fail("出生点数量不是 2~3 个：" + _sc.SpawnCount);
                if (seen.Count < 2) Fail("随机出生点没有随机效果（只出现 " + seen.Count + " 种）");
            }
            if (t < 1.6f) return;

            Say("点「操作演练」：进入原第一阶段的实操流程（只练流程，不计成绩）");
            _start.OpenTutorial();
            NextPhase();
        }

        // ------------------------------------------------------------------
        // 2. 天花板 + 移动/瞬转（既有玩法不退化）
        // ------------------------------------------------------------------
        static void P2_CeilingAndMove()
        {
            float t = Time.realtimeSinceStartup - _phaseT;

            if (_sub == 0)
            {
                // 抬头 55°：天花板必须出现在玩家视角里
                _cam.transform.localRotation = Quaternion.Euler(-55f, 0f, 0f);
                if (!_shotCeiling && t > 0.5f)
                {
                    Shot("vr1_ceiling.png"); _shotCeiling = true;

                    // 天花板是纯视觉件（无碰撞体，避免挡光），不能用射线判断；
                    // 改为校验：顶面 MeshRenderer 存在、包围盒覆盖玩家站位、顶面高度≈3.0，
                    // 且抬头时该点确实落在相机画面内（真的是「玩家视角看得见」）。
                    var ceilGo = GameObject.Find("Ceiling");
                    var ceilMr = ceilGo != null ? ceilGo.GetComponent<MeshRenderer>() : null;
                    bool covers = false, onScreen = false;
                    float topY = 0f;
                    if (ceilMr != null)
                    {
                        Bounds b = ceilMr.bounds;
                        Vector3 cam = _cam.transform.position;
                        covers = cam.x >= b.min.x && cam.x <= b.max.x && cam.z >= b.min.z && cam.z <= b.max.z;
                        topY = b.min.y;
                        // 头顶正上方的点抬高 55° 后本来就超出 60° FOV，不能拿它判「在不在画面里」；
                        // 改成：沿相机实际视线与天花板平面求交，交点落回天花板范围内 → 玩家抬头真的看得见。
                        Vector3 dir = _cam.transform.forward;
                        if (dir.y > 0.02f)
                        {
                            float tHit = (b.min.y - cam.y) / dir.y;
                            float dis = dir.magnitude * tHit;
                            Vector3 hit = cam + dir.normalized * dis;
                            onScreen = dis > 0.5f && dis < 12f
                                && hit.x >= b.min.x && hit.x <= b.max.x
                                && hit.z >= b.min.z && hit.z <= b.max.z;
                            Debug.Log("[VRPROBE] 视线与天花板交点 (" + hit.x.ToString("F2") + ", " + hit.y.ToString("F2")
                                + ", " + hit.z.ToString("F2") + ")，距离 " + dis.ToString("F2") + " 米");
                        }
                    }
                    Say("抬头视角：天花板渲染体=" + (ceilMr != null)
                        + "，内表面 y=" + topY.ToString("F2") + " 米，覆盖玩家站位=" + covers
                        + "，抬头时在画面内=" + onScreen);
                    if (ceilMr == null) Fail("场景里没有天花板（找不到 Ceiling 的渲染体）");
                    else if (!covers) Fail("天花板没有覆盖玩家所处位置");
                    else if (Mathf.Abs(topY - 3.0f) > 0.1f) Fail("天花板内表面高度不是 3.0 米，实测 " + topY.ToString("F2"));
                    else if (!onScreen) Fail("抬头看不到天花板（不在相机画面内）");
                }
                if (t < 1.3f) return;
                _cam.transform.localRotation = Quaternion.identity;
                AimCameraAt(_tutFire != null ? _tutFire.transform.position : CamPos() + _cam.transform.forward);
                if (!_shotTutorial && t > 1.6f)
                {
                    Shot("vr1b_tutorial_view.png"); _shotTutorial = true;
                    Say("操作演练视角：地板火已点燃=" + (_tutFire != null && _tutFire.IsBurning)
                        + "，HUD=" + (_vr.hud != null ? _vr.hud.CurrentHint : "null"));
                    if (_tutFire == null || !_tutFire.IsBurning) Fail("操作演练开始后火焰没有点燃");
                }
                if (t < 2.1f) return;
                _sub = 1; _subT = Time.realtimeSinceStartup; _subInit = false;
                return;
            }

            if (_sub == 1)
            {
                // 左摇杆前推 0.9 秒，检查位移仍在（改了输入逻辑也不能推不动）
                _loco.SetVirtualInput(new Vector2(0f, 1f), 0f);
                if (Time.realtimeSinceStartup - _subT > 0.9f)
                {
                    _loco.SetVirtualInput(Vector2.zero, 0f);
                    Say("左摇杆前推 0.9 秒：速度 " + _loco.CurrentSpeed.ToString("F2") + " 米/秒（配置 2.2）");
                    _sub = 2; _subT = Time.realtimeSinceStartup; _subInit = false;
                }
                return;
            }

            if (_sub == 2)
            {
                // 基准只在进入本段的第一帧取一次：之前每帧刷新会把已转过的角度「吃掉」，永远测成 0°
                if (!_subInit)
                {
                    _walkYaw = _cam.transform.eulerAngles.y;
                    _subInit = true;
                    _subT = Time.realtimeSinceStartup;
                }
                _loco.SetVirtualInput(Vector2.zero, 1f);
                if (Time.realtimeSinceStartup - _subT > 0.35f)
                {
                    _loco.SetVirtualInput(Vector2.zero, 0f);
                    float yaw = Mathf.DeltaAngle(_walkYaw, _cam.transform.eulerAngles.y);
                    Say("右摇杆瞬转：相机 Y 轴 " + yaw.ToString("F1") + "°（配置 45°）");
                    if (Mathf.Abs(Mathf.Abs(yaw) - 45f) > 4f) Fail("瞬转不是 45°，实测 " + yaw.ToString("F1") + "°");
                    _sub = 3; _subT = Time.realtimeSinceStartup;
                }
                return;
            }

            NextPhase();
        }

        // ------------------------------------------------------------------
        // 3. 操作演练：拿瓶 → 5 题问答 → 拔销 → 压把 → 喷射灭火 → 完成面板
        // ------------------------------------------------------------------
        static void P3_TutorialUnit()
        {
            if (_sub == 0)
            {
                BeginUnit();
                _sub = 1;
            }
            if (_sub == 1)
            {
                if (!RunUnit(_ext1, _tutFire, "操作演练", false, "vr1c_tutorial_quiz.png",
                             null, null, null, out _held)) return;
                Say("操作演练火焰已扑灭；完成面板=" + (_panel != null && _panel.IsOpen)
                    + "（文案：操作演练完成 / 进入正式演练）");
                _sub = 2; _subT = Time.realtimeSinceStartup;
                return;
            }
            if (!_shotPanel && _panel != null && _panel.IsOpen && Time.realtimeSinceStartup - _subT > 0.5f)
            {
                AimCameraAt(_panel.transform.position);
                Shot("vr1d_tutorial_done.png"); _shotPanel = true;
            }
            if (Time.realtimeSinceStartup - _subT < 1.2f) return;
            NextPhase();
        }

        // ------------------------------------------------------------------
        // 4. 进入正式演练（随机出生点）
        // ------------------------------------------------------------------
        static void P4_EnterFormal()
        {
            float t = Time.realtimeSinceStartup - _phaseT;
            if (_sub == 0)
            {
                _sc.BeginFormal();          // 等价于点完成面板上的「进入正式演练」
                _sub = 1; _subT = Time.realtimeSinceStartup;
                Say("正式演练第 1 阶段开始：出生点 index=" + _sc.LastSpawnIndex + "/" + _sc.SpawnCount
                    + "，位置 " + _rigRoot.position.ToString("F2")
                    + "，烟雾=" + (_sc.stage1Smoke != null && _sc.stage1Smoke.Active)
                    + "，设备火燃烧=" + (_deviceFire != null && _deviceFire.IsBurning)
                    + "，断电锁定=" + (_deviceFire != null && _deviceFire.extinguishLocked));
                if (_sc.LastSpawnIndex < 0 || _sc.LastSpawnIndex >= _sc.SpawnCount) Fail("出生点下标非法");
                if (_sc.stage1Smoke == null || !_sc.stage1Smoke.Active) Fail("点击开始后没有立即出现烟雾");
                if (_deviceFire == null || !_deviceFire.IsBurning) Fail("点击开始后设备没有起火");
                if (!_deviceFire.extinguishLocked) Fail("电气火灾没有默认锁定（应先断电才可灭）");
                if (_sc.arrows == null || !_sc.arrows.Active) Fail("开始后引导箭头未启动");
                return;
            }
            if (t < 0.8f) return;
            if (!_shotSpawn)
            {
                AimCameraAt(_deviceFire.transform.position + Vector3.up * 0.8f);
                Shot("vr2_spawn_view.png"); _shotSpawn = true;
                Say("出生点玩家视角：设备柜/火焰/烟雾同框（截图 vr2_spawn_view.png）");
            }
            if (t < 1.4f) return;
            NextPhase();
        }

        // ------------------------------------------------------------------
        // 5. 设备火近景 + 火焰血量条
        // ------------------------------------------------------------------
        static void P5_DeviceLook()
        {
            float t = Time.realtimeSinceStartup - _phaseT;
            if (_sub == 0)
            {
                PlaceOnPlayerSide(_deviceFire.transform.position, 1.9f, "设备火前 1.9 米");
                _sub = 1; _subT = Time.realtimeSinceStartup;
                return;
            }
            if (t < 0.9f) return;
            if (!_shotDevice)
            {
                LookAtPoint(_deviceFire.AimPoint + Vector3.up * 0.85f);
                Shot("vr2b_device_fire_hp.png"); _shotDevice = true;
                Say("火焰血量条：存在=" + (_hpDevice != null) + "，可见=" + (_hpDevice != null && _hpDevice.Visible)
                    + "，当前 HP=" + (_hpDevice != null ? _hpDevice.CurrentHp : -1)
                    + " / " + (_hpDevice != null ? _hpDevice.MaxHp : -1));
                if (_hpDevice == null) Fail("设备火焰上方没有血量条");
                else if (!_hpDevice.Visible) Fail("火焰血量条不可见");
                else if (_hpDevice.CurrentHp < 95) Fail("未灭火时血量条不是满值");
            }
            if (t < 1.5f) return;
            NextPhase();
        }

        // ------------------------------------------------------------------
        // 6. 正式第 1 阶段：先喷（锁定）→ 断电 → 问答 → 拔销 → 压把 → 灭火
        // ------------------------------------------------------------------
        static void P6_Stage1Unit()
        {
            if (_sub == 0)
            {
                BeginUnit();
                _sub = 1;
            }
            if (_sub == 1)
            {
                if (!RunUnit(_ext2, _deviceFire, "第 1 阶段设备火", true, "vr3_stage1_quiz.png",
                             "vr3b_stage1_hp_mid.png", "vr2d_locked_spray.png", "vr2c_power_off.png", out _held)) return;
                Say("设备火扑灭：HP 已归零=" + (_hpDevice == null || _hpDevice.CurrentHp <= 5)
                    + "，stage1Smoke 运行中=" + (_sc.stage1Smoke != null && _sc.stage1Smoke.Active)
                    + "，flow=" + (_flow != null ? _flow.CurrentStage.ToString() : "null"));
                if (_sc.stage1Smoke != null && _sc.stage1Smoke.Active) Fail("灭火后阶段一烟雾没有停");
                if (_flow == null || _flow.CurrentStage == StageTwoFlow.Stage.Idle)
                    Fail("设备火扑灭后没有进入第 2 阶段（flow=" + (_flow != null ? _flow.CurrentStage.ToString() : "null") + "）");
                _sub = 2; _subT = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - _subT < 1.0f) return;
            NextPhase();
        }

        // ------------------------------------------------------------------
        // 7. 第 2 阶段：安全标识 / 箭头 → 大厅 → 出口火
        // ------------------------------------------------------------------
        static void P7_Stage2()
        {
            if (_sub == 0)
            {
                Transform lobby = _flow.arrows != null ? _flow.arrows.lobbyTarget : null;
                if (lobby != null)
                {
                    AimCameraAt(lobby.position);
                    Shot("vr4_stage2_arrows.png");
                    Say("第 2 阶段玩家视角：地面「安全出口 →」标识 + 引导箭头（截图 vr4_stage2_arrows.png）");
                    Teleport(lobby.position, "大厅检查点（第 2 阶段终点）");
                }
                _sub = 1; _subT = Time.realtimeSinceStartup;
                return;
            }
            if (_flow.CurrentStage != StageTwoFlow.Stage.StageThree &&
                _flow.CurrentStage != StageTwoFlow.Stage.ExitOpen &&
                _flow.CurrentStage != StageTwoFlow.Stage.Done)
            {
                if (Time.realtimeSinceStartup - _subT > 8f)
                {
                    Fail("走到大厅后没有进入出口火情阶段，flow=" + _flow.CurrentStage);
                    NextPhase();
                }
                return;
            }
            _exitFire = _flow.stageTwoFire;
            Say("到达大厅 → 出口门附近升起火焰：" + (_exitFire != null)
                + "，位置 " + (_exitFire != null ? _exitFire.transform.position.ToString("F2") : "null")
                + "，门解锁=" + (_door != null && _door.IsUnlocked));
            if (_exitFire == null) { Fail("出口附近没有升起火焰"); NextPhase(); return; }

            AimCameraAt(_exitFire.transform.position + Vector3.up * 0.6f);
            if (Time.realtimeSinceStartup - _subT > 1.2f)
            {
                Shot("vr4b_exit_fire.png");
                Say("出口火情玩家视角（截图 vr4b_exit_fire.png）");
                NextPhase();
            }
        }

        // ------------------------------------------------------------------
        // 8. 出口火：疏散问答 → 灭火 → 门解锁
        // ------------------------------------------------------------------
        static void P8_ExitUnit()
        {
            if (_sub == 0)
            {
                BeginUnit();
                _sub = 1;
            }
            if (_sub == 1)
            {
                if (!RunUnit(_ext3, _exitFire, "第 2 阶段出口火", false, "vr5_exit_quiz.png",
                             "vr5b_exit_hp_mid.png", null, null, out _held)) return;
                Say("出口火扑灭：门解锁=" + (_door != null && _door.IsUnlocked) + "，flow=" + _flow.CurrentStage);
                if (_door == null || !_door.IsUnlocked) Fail("灭火后安全出口门没有解锁");
                _sub = 2; _subT = Time.realtimeSinceStartup;
                return;
            }
            if (Time.realtimeSinceStartup - _subT < 0.8f) return;
            NextPhase();
        }

        // ------------------------------------------------------------------
        // 9. 开门 → 走进出口 → 结束页
        // ------------------------------------------------------------------
        static void P9_Exit()
        {
            _vr.vTrigger = false;
            float t = Time.realtimeSinceStartup - _phaseT;

            if (!_shotDoor)
            {
                if (_shotAt == 0f) { _shotAt = Time.realtimeSinceStartup; AimCameraAt(_door.transform.position + Vector3.up * 1.0f); }
                if (Time.realtimeSinceStartup - _shotAt > 0.9f)
                {
                    Shot("vr6_door_open.png");
                    Say("玩家视角：门扇摆开、走廊可见、EXIT 灯牌亮（截图 vr6_door_open.png）");
                    _shotDoor = true;
                }
                return;
            }

            if (_flow.CurrentStage == StageTwoFlow.Stage.Done && _ending != null && _ending.IsOpen)
            {
                if (!_shotEnd)
                {
                    if (_shotAt == 0f) _shotAt = Time.realtimeSinceStartup;
                    if (Time.realtimeSinceStartup - _shotAt > 0.6f)
                    {
                        // 成绩单是「通关瞬间相机前方 1.2 米」的世界画布（Canvas_Ending 独立根物体）；
                        // 对着 EndingScreen 脚本根的位置转会把镜头拧到别处，拍出一张没有面板的图。
                        var endCanvas = GameObject.Find("Canvas_Ending");
                        if (endCanvas != null) LookAtPoint(endCanvas.transform.position);
                        else AimCameraAt(_ending.transform.position);
                        Shot("vr7_ending.png"); _shotEnd = true;
                        Say("结束页：用时/答对/火源/步骤与重开按钮（截图 vr7_ending.png）");
                    }
                    return;
                }
                Say("通关：Stage=Done，用时约 " + (Time.realtimeSinceStartup - _clock0).ToString("F0") + " 秒");
                NextPhase();
                return;
            }

            if (t < 0.5f) return;
            _door.TryEnter();            // 等价于走进 0.9 米判定圈
            if (t > 10f) { Fail("走到门口仍未通关，Stage=" + _flow.CurrentStage); NextPhase(); }
        }

        // ==================================================================
        // 通用「灭火单元」：站到瓶旁 → 5 题问答 → 抓取 → 拔销 → 压把 →
        //                （可选）未断电喷射验证 + 断电 → 喷射灭火
        // 返回 true 表示本单元结束（灭火成功或已判失败）
        // ==================================================================
        static void BeginUnit()
        {
            _uStep = 0; _uSub = 0;
            _uStepT = _uSubT = Time.realtimeSinceStartup;
            _progA = -1f;
            _unitShotQuiz = _unitShotMid = _unitShotLocked = false;
            _vr.vTrigger = false;
            _lastProgress = -1f;
        }

        static bool RunUnit(Extinguisher ext, FireSource fire, string tag, bool powerGate,
                            string shotQuiz, string shotMid, string shotLocked, string shotPower, out Extinguisher held)
        {
            held = _held;
            float now = Time.realtimeSinceStartup;
            if (ext == null || fire == null) { Fail(tag + "：灭火器或火源缺失"); return true; }

            switch (_uStep)
            {
                case 0:
                    TeleportNear(ext.transform.position, fire.transform.position, 0.9f, tag + "：灭火器旁");
                    _uStep = 1; _uStepT = now;
                    return false;

                case 1:   // ---- 5 题问答：10 秒/题、60% 通过 ----
                    {
                        var q = ext.quizPanel;
                        if (q == null) { _uStep = 2; _uStepT = now; return false; }
                        if (!q.IsOpen && !q.Passed && !q.Failed && now - _uStepT < 1.2f)
                        {
                            q.Open(_rightHand);
                            Say(tag + "：右手点开灭火器 → 5 题情景问答（每题 10 秒、60% 通过）· " + q.questionSetIndex + " 组");
                            return false;
                        }
                        if (q.IsOpen && !_unitShotQuiz && now - _uStepT > 0.5f)
                        {
                            if (shotQuiz != null) Shot(shotQuiz);
                            _unitShotQuiz = true;
                            Say(tag + " 问答面板：第 " + (q.QuestionIndex + 1) + "/" + q.QuestionCount
                                + " 题，剩余 " + q.TimeLeft.ToString("F1") + " 秒，已答对 " + q.CorrectCount);
                        }
                        if (q.IsOpen && !q.IsAnsweredThisQuestion && q.QuestionIndex < q.QuestionCount)
                        {
                            int c = q.questions[q.QuestionIndex].correctIndex;
                            q.AnswerOption(c);
                            Say(tag + " 第 " + (q.QuestionIndex + 1) + " 题选 " + (char)('A' + c)
                                + "（正确答案），累计答对 " + q.CorrectCount);
                            return false;
                        }
                        if (q.Passed)
                        {
                            Say(tag + "：问答通过 " + q.CorrectCount + "/" + q.QuestionCount
                                + "（阈值 60%）→ 可拔销");
                            _uStep = 2; _uStepT = now;
                            return false;
                        }
                        if (q.Failed)
                        {
                            Fail(tag + "：问答未通过（答对 " + q.CorrectCount + "）");
                            _uStep = 2; _uStepT = now;
                        }
                        if (now - _uStepT > 45f) { Fail(tag + "：问答 45 秒未结束"); _uStep = 2; _uStepT = now; }
                        return false;
                    }

                case 2:   // ---- 抓取 ----
                    if (!ext.grab.IsHeld)
                    {
                        ext.grab.GrabExternal(_rightHand, HandSide.Right);
                        held = ext;
                        Say(tag + "：Grip 抓取 → IsHeld=" + ext.grab.IsHeld);
                    }
                    _uStep = 3; _uStepT = now;
                    return false;

                case 3:   // ---- 第一下扳机：拔销 ----
                    if (now - _uStepT < 0.4f) { _vr.vTrigger = true; return false; }
                    _vr.vTrigger = false;
                    if (now - _uStepT < 0.7f) return false;
                    Say(tag + "：扳机一下 → 保险销 PinPulled=" + ext.PinPulled);
                    if (!ext.PinPulled) Fail(tag + "：扳机未拔出保险销");
                    _uStep = 4; _uStepT = now;
                    return false;

                case 4:   // ---- 第二下扳机：压把并保持喷射 ----
                    if (now - _uStepT < 0.3f) { _vr.vTrigger = false; return false; }
                    _vr.vTrigger = true;
                    if (now - _uStepT < 0.9f) return false;
                    Say(tag + "：压把 PressedOnce=" + (ext.handleLever != null && ext.handleLever.PressedOnce)
                        + "，IsSpraying=" + ext.IsSpraying);
                    if (!ext.IsSpraying) Fail(tag + "：压下压把后没有喷射");
                    _uStep = powerGate ? 5 : 6; _uStepT = now; _uSub = 0;
                    return false;

                case 5:   // ---- 电气火灾：未断电喷射无效 → 断电 ----
                    if (fire.extinguishLocked)
                    {
                        switch (_uSub)
                        {
                            case 0:
                                _vr.vTrigger = false;
                                PlaceForSpray(fire, 2.0f, tag + "：火前 2.0 米（未断电）");
                                _uSub = 1; _uSubT = now;
                                return false;

                            case 1:
                                if (now - _uSubT < 0.4f) return false;
                                _vr.vTrigger = true; AimAt(ext, fire.AimPoint);
                                if (_progA < 0f) { _progA = fire.progress01; _uSubT = now; }
                                if (now - _uSubT < 1.4f) return false;
                                {
                                    float progB = fire.progress01;
                                    Say(tag + "：未断电喷射 1.4 秒，火焰进度 " + _progA.ToString("F3")
                                        + " → " + progB.ToString("F3") + "（锁定=" + fire.extinguishLocked + "）");
                                    if (shotLocked != null) Shot(shotLocked);
                                    if (Mathf.Abs(progB - _progA) > 0.05f)
                                        Fail(tag + "：未断电时火焰进度仍在下降（应锁定）");
                                    _uSub = 2; _uSubT = now;
                                }
                                return false;

                            case 2:
                                _vr.vTrigger = false;
                                _handDrop = 0.34f;   // 低头看闸门时手自然放下，别让灭火器瓶身糊住整个画面
                                PlaceOnPlayerSide(_power.transform.position, 1.7f, tag + "：电源箱前 1.7 米");
                                _uSub = 3; _uSubT = now;
                                return false;

                            case 3:
                                if (now - _uSubT < 0.4f) return false;
                                // 直接连俯仰一起对准电源箱（AimCameraAt 只转水平，相机若带俯仰会拍到天花板）
                                LookAtPoint(_power.transform.position + Vector3.up * 0.10f);
                                bool ok = _sc.TryCutPower();
                                Say(tag + "：点电源箱 → IsCut=" + _power.IsCut + "（调用结果 " + ok
                                    + "），火焰锁定=" + fire.extinguishLocked);
                                LookAtPoint(_power.transform.position + Vector3.up * 0.10f);
                                if (shotPower != null) Shot(shotPower);
                                if (!_power.IsCut) Fail(tag + "：切断电源失败");
                                if (fire.extinguishLocked) Fail(tag + "：断电后火焰仍处于锁定状态");
                                _uSub = 4; _uSubT = now;
                                return false;

                            case 4:
                                _handDrop = 0f;
                                PlaceForSpray(fire, 2.0f, tag + "：断电后回到火前");
                                _uStep = 6; _uStepT = now; _uSub = 0; _vr.vTrigger = false;
                                return false;
                        }
                        return false;
                    }
                    _uStep = 6; _uStepT = now;
                    return false;

                case 6:   // ---- 持续喷射灭火：血量条随进度下降 ----
                    _vr.vTrigger = true;
                    if (!ext.grab.IsHeld)
                    {
                        ext.grab.GrabExternal(_rightHand, HandSide.Right);
                        held = ext;
                        Say(tag + "：手持物脱手，已重新抓回");
                        return false;
                    }
                    AimAt(ext, fire.AimPoint);

                    float nd = DistanceNozzle(ext, fire.AimPoint);
                    if (nd > 2.6f && now - _uStepT < 8f)
                    {
                        PlaceForSpray(fire, 1.9f, tag + "：火前 1.9 米（重摆）");
                        return false;
                    }
                    if (!_unitShotMid && fire.progress01 >= 0.30f)
                    {
                        LookAtPoint(fire.AimPoint + Vector3.up * 0.55f);
                        if (shotMid != null) Shot(shotMid);
                        _unitShotMid = true;
                        Say(tag + "：喷射进度 " + fire.progress01.ToString("F3")
                            + "（喷嘴 " + nd.ToString("F2") + " 米）· 血量条 HP="
                            + (_hpDevice != null ? _hpDevice.CurrentHp : -1));
                    }
                    if (_lastProgress < 0f || Mathf.Abs(fire.progress01 - _lastProgress) > 0.1f)
                    {
                        _lastProgress = fire.progress01;
                        Say(tag + " 灭火进度 " + fire.progress01.ToString("F3"));
                    }
                    if (fire.IsExtinguished)
                    {
                        _vr.vTrigger = false;
                        Say(tag + "：火焰熄灭（progress=" + fire.progress01.ToString("F3") + "）");
                        return true;
                    }
                    if (now - _uStepT > 45f)
                    {
                        // 排障用：把「为什么喷不到」写进结果，免得只看到一句进度停滞
                        string block = "none";
                        if (ext.nozzle != null)
                        {
                            Vector3 a = ext.nozzle.position;
                            Vector3 t = fire.AimPoint - a;
                            RaycastHit h2;
                            if (Physics.Raycast(a, t.normalized, out h2, t.magnitude, ~0, QueryTriggerInteraction.Ignore))
                                block = h2.collider.name + " @" + h2.distance.ToString("F2") + " 米";
                        }
                        Say(tag + "：诊断 喷射中=" + ext.IsSpraying + " 已拔销=" + ext.PinPulled
                            + " 持握=" + ext.grab.IsHeld + " 火在燃烧=" + fire.IsBurning
                            + " 喷嘴到火=" + DistanceNozzle(ext, fire.AimPoint).ToString("F2")
                            + " 前方遮挡=" + block);
                        Fail(tag + "：45 秒未灭火，进度 " + fire.progress01.ToString("F3")
                             + "，喷嘴距离 " + nd.ToString("F2"));
                        return true;
                    }
                    return false;
            }
            return true;
        }

        // ==================================================================
        // 站位 / 取景工具
        // ==================================================================
        static void Teleport(Vector3 p, string why)
        {
            _loco.SetVirtualInput(Vector2.zero, 0f);
            Vector3 cam = CamPos();
            Vector3 d = new Vector3(p.x - cam.x, 0f, p.z - cam.z);

            RaycastHit hit;
            float groundY = _rigRoot.position.y;
            if (Physics.Raycast(new Vector3(p.x, p.y + 3f, p.z), Vector3.down, out hit, 30f, ~0,
                                 QueryTriggerInteraction.Ignore))
                groundY = hit.point.y + 0.02f;

            var ccs = _rigRoot.GetComponentsInChildren<CharacterController>(true);
            foreach (var c in ccs) c.enabled = false;
            _rigRoot.position = new Vector3(_rigRoot.position.x + d.x, groundY, _rigRoot.position.z + d.z);
            Physics.SyncTransforms();
            foreach (var c in ccs) c.enabled = true;

            Say("机位安置到" + why + "（办公场景隔墙，直线推杆会被挡住，就近站位）· 眼高 "
                + (_cam.transform.position.y - groundY).ToString("F2") + " 米");
        }

        static void TeleportNear(Vector3 from, Vector3 awayTo, float dist, string why)
        {
            Vector3 dir = from - awayTo;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.001f) dir = _cam.transform.forward;
            dir.y = 0f;
            Vector3 baseDir = dir.normalized;

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

        /// <summary>灭火站位：绕火源 12 个方位挑一个「能站人 + 喷嘴到火焰根部没有遮挡」的落点。</summary>
        static void PlaceForSpray(FireSource fire, float dist, string why)
        {
            if (fire == null) return;
            Vector3 aim = fire.AimPoint;
            const float floorY = 0.02f;

            for (int k = 0; k < 12; k++)
            {
                Vector3 dir = Quaternion.Euler(0f, k * 30f, 0f) * Vector3.back;
                Vector3 cand = new Vector3(aim.x + dir.x * dist, floorY, aim.z + dir.z * dist);
                if (!Standable(cand, floorY)) continue;

                Vector3 eye = new Vector3(cand.x, floorY + 1.35f, cand.z);
                Vector3 d = aim - eye;
                RaycastHit hit;
                bool clear = true;
                if (Physics.Raycast(eye, d.normalized, out hit, d.magnitude, ~0, QueryTriggerInteraction.Ignore))
                    clear = hit.distance >= d.magnitude - 0.35f;   // 命中点落在火焰之后 = 中间没挡东西
                if (!clear) continue;

                Teleport(cand, why + "（方位 " + (k * 30) + "°，无遮挡）");
                return;
            }
            PlaceOnPlayerSide(fire.transform.position, dist, why + "·无合格站位");
        }

        static void PlaceOnPlayerSide(Vector3 obj, float dist, string why)
        {
            Vector3 dir = _rigRoot.position - obj;
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = -_cam.transform.forward;
            dir.y = 0f;
            Vector3 baseDir = dir.normalized;

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

        static bool Standable(Vector3 p, float floorY)
        {
            RaycastHit hit;
            if (!Physics.Raycast(new Vector3(p.x, floorY + 3f, p.z), Vector3.down, out hit, 6f, ~0,
                                 QueryTriggerInteraction.Ignore)) return false;
            if (Mathf.Abs(hit.point.y - floorY) > 0.8f) return false;
            return !Physics.CheckSphere(new Vector3(p.x, floorY + 0.9f, p.z), 0.25f, ~0,
                                        QueryTriggerInteraction.Ignore);
        }

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

        /// <summary>只转水平朝向，让玩家视角正对某个点（出图取景），并把俯仰拉回水平。</summary>
        static void AimCameraAt(Vector3 p)
        {
            Vector3 cam = CamPos();
            Vector3 d = p - cam; d.y = 0f;
            if (d.sqrMagnitude < 0.01f) return;
            float cur = _cam.transform.eulerAngles.y;
            float want = Quaternion.LookRotation(d.normalized, Vector3.up).eulerAngles.y;
            _rigRoot.rotation = Quaternion.Euler(0f, want - cur, 0f) * _rigRoot.rotation;
            _cam.transform.localRotation = Quaternion.identity;
            // 相机上还有一层 Camera Offset，鼠标视角/上一段抬头会把它的 X 轴压翘起来；
            // 只清相机自身的话画面仍然朝天（实测「断电」截图拍成了天花板 + 天空盒），必须连它一起放平。
            var camOffset = _cam.transform.parent;
            if (camOffset != null && camOffset != _rigRoot)
                camOffset.localRotation = Quaternion.identity;   // 清掉鼠标视角留下的俯仰/横滚
        }

        /// <summary>连俯仰一起对准（模拟玩家抬头/低头看目标）。</summary>
        static void LookAtPoint(Vector3 p)
        {
            Vector3 cam = CamPos();
            Vector3 d = p - cam;
            Vector3 flat = d; flat.y = 0f;
            if (flat.sqrMagnitude < 0.01f) return;

            // 只对「rig 的 yaw + Camera Offset 的 pitch」下手，不给相机写世界旋转：
            // 相机是 rig 的子物体，任何一次 rig 转向都会把世界旋转带走（实测断电取景几帧后偏了 90°）。
            float wantYaw = Quaternion.LookRotation(flat.normalized, Vector3.up).eulerAngles.y;
            float curYaw = _cam.transform.eulerAngles.y;
            _rigRoot.rotation = Quaternion.Euler(0f, wantYaw - curYaw, 0f) * _rigRoot.rotation;

            float pitch = -Mathf.Asin(Mathf.Clamp(d.normalized.y, -1f, 1f)) * Mathf.Rad2Deg;
            var camOffset = _cam.transform.parent;
            if (camOffset != null && camOffset != _rigRoot)
                camOffset.localRotation = Quaternion.Euler(pitch, 0f, 0f);   // 横滚归零，画面不歪
            _cam.transform.localRotation = Quaternion.identity;
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
