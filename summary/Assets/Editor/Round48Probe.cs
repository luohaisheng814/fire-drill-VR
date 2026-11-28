// 第 48 轮玩家视角验收：操作流程页 → 两种火情情形 × 两种身份
//   RunCase1Employee：情形 1（火势初起）+ 普通员工 → 墙上报警按钮 → 撤离 → 出口无火右击开门
//   RunCase1Lead：    情形 1 + 初期应对负责人 → 灭火 → 撤离 → 出口无火右击开门
//   RunCase2：        情形 2（火势已发展）→ 只报警 → 出口门前有火焰 → 灭火后出门
// 截图统一 1600×900，相机高度 1.62 米（玩家眼高）。
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;
using FireSafety;

namespace FireSafety.EditorTools
{
    public static class Round48Probe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = @"D:\43763\VR\summary\Logs\shots";
        const float HardTimeout = 200f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static float _t0, _phaseT;
        static bool _entered;
        static int _phase;
        static Transform _cam;
        static Camera _camera;
        static StartScreen _start;
        static DrillScenario _scenario;
        static DrillFlowDirector _flow;
        static StageTwoFlow _stage;
        static ExitDoor _door;

        public static void LogOnly()
        {
            Debug.Log("[G48] LogOnly 进入");
            EditorApplication.Exit(0);
        }

        public static void OpenOnly()
        {
            Debug.Log("[G48] 打开场景开始");
            var sc = EditorSceneManager.OpenScene(ScenePath);
            Debug.Log("[G48] 打开场景完成：" + (sc.IsValid() ? sc.name : "invalid"));
            EditorApplication.Exit(0);
        }

        public static void RunCase1Employee() => Run(1, 0);
        public static void RunCase1Lead() => Run(1, 1);
        public static void RunCase2() => Run(2, 0);

        static int _case;
        static int _role;

        static void Run(int opCase, int role)
        {
            _case = opCase; _role = role;
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _phaseT = 0; _entered = false; _t0 = 0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[G48] " + s); Log.Add(s); }
        static void Fail(string s) { if (Fails.Contains(s)) return; Debug.LogError("[G48] × " + s); Log.Add("× " + s); Fails.Add(s); }
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
            if (!_entered) { _entered = true; _t0 = Time.realtimeSinceStartup; Say("进入 Play 模式（玩家视角 1.62 米）"); }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            var cam = Camera.main;
            if (cam == null) return;
            if (_cam == null) { _cam = cam.transform; _camera = cam; }

            switch (_phase)
            {
                case 0:
                    if (Time.realtimeSinceStartup - _t0 < 3f) return;
                    var lang = UnityEngine.Object.FindFirstObjectByType<LanguageSelectScreen>();
                    if (lang != null) lang.Choose(DrillLocale.Lang.Chinese);
                    Next(1);
                    return;

                case 1:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
                    _flow = UnityEngine.Object.FindFirstObjectByType<DrillFlowDirector>();
                    _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
                    Check("场景里有 StartScreen / DrillScenario / DrillFlowDirector", _start != null && _scenario != null && _flow != null);
                    if (_start == null || _scenario == null) { Finish("缺组件"); return; }

                    // 身份由探针固定，模拟「本轮随机抽到该身份」
                    _flow.SetRole(_role == 1 ? DrillFlowDirector.DrillRole.ResponseLead : DrillFlowDirector.DrillRole.Employee);
                    Check("身份已设定：" + _flow.RoleName, true);
                    Shot("g48v_start_cases.png", new Vector3(-5.20f, 1.62f, 1.60f), new Vector3(-3.70f, 1.70f, 3.40f), 60f);
                    Next(2);
                    return;
                }

                case 2:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                    bool clicked = ClickButton("情形 " + _case);
                    Check("点开操作流程「情形 " + _case + "」", clicked);
                    if (!clicked) { Finish("找不到情形按钮"); return; }
                    Next(3);
                    return;
                }

                case 3:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.5f) return;
                    var want = _case == 2 ? DrillScenario.OperationCase.Developed : DrillScenario.OperationCase.Incipient;
                    Check("进入正式演练", _scenario.CurrentMode == DrillScenario.Mode.Formal);
                    Check("火情情形正确", _scenario.operationCase == want, _scenario.operationCase.ToString());
                    Check("报警前处于第 1 阶段", _scenario.CurrentPhase == DrillScenario.Phase.Stage1);
                    bool shouldForbid = _case == 2 || _role == 0;
                    Check("本情形/身份是否允许灭主火", _scenario.Stage1AttackForbidden == shouldForbid,
                          "forbidden=" + _scenario.Stage1AttackForbidden);

                    // 站到壁挂报警按钮前（按钮在 z=-3.98 的南墙，面向 +Z）
                    _door = UnityEngine.Object.FindFirstObjectByType<ExitDoor>();
                    _stage = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
                    Check("出口门与第 2 阶段流程存在", _door != null && _stage != null);
                    Check("旧流程不再自动开门",
                          !_flow.unlockOnEvacuate && !_flow.unlockOnExtinguished,
                          "evac=" + _flow.unlockOnEvacuate + " ext=" + _flow.unlockOnExtinguished);
                    Check("探针门与流程门是同一扇", _stage.exitDoor == _door);
                    PlacePlayer(new Vector3(2.55f, 0f, -2.55f), new Vector3(2.55f, 1.30f, -3.98f));
                    Next(4);
                    return;
                }

                case 4:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                    var alarm = UnityEngine.Object.FindFirstObjectByType<AlarmCallButton>();
                    Check("墙上有手动报警按钮", alarm != null, alarm != null ? alarm.transform.position.ToString("F2") : "null");
                    Check("报警按钮是壁挂式（无落地立柱）", alarm != null && alarm.transform.Find("Column") == null);
                    Shot("g48v_alarm_wall.png", new Vector3(2.55f, 1.62f, -2.55f), new Vector3(2.55f, 1.30f, -3.98f), 55f);
                    if (alarm == null) { Finish("无报警按钮"); return; }
                    alarm.PressByScript();
                    Next(5);
                    return;
                }

                case 5:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    bool alarmStartsEvac = !(_case == 1 && _role == 1);
                    Check("按下按钮后流程阶段符合身份/情形",
                          _scenario.CurrentPhase == (alarmStartsEvac ? DrillScenario.Phase.Stage2 : DrillScenario.Phase.Stage1),
                          _scenario.CurrentPhase.ToString());
                    Check("出口门前火情与情形一致",
                          _stage.exitFirePresent == (_case == 2),
                          "exitFirePresent=" + _stage.exitFirePresent);

                    if (_case == 1 && _role == 1)
                    {
                        // 负责人：允许灭火 → 直接完成灭火（等价于玩家喷完）
                        Check("负责人可以灭主火", !_scenario.Stage1AttackForbidden);
                        _scenario.deviceFire.SetExtinguishLocked(false);
                        _scenario.deviceFire.Extinguish();
                    }

                    // 走到出口门前（门在 x=5.25, z=1.02）
                    PlacePlayer(new Vector3(5.25f, 0f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f));
                    Next(6);
                    return;
                }

                case 6:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                    if (_case == 1)
                    {
                        // 情形 1：门前无火 → 先解锁但门保持关闭，玩家右击才开
                        _stage.TryArriveLobby(false);
                        Check("情形 1 出口已解锁", _door.IsUnlocked);
                        Check("情形 1 门仍关闭（需右击）", !_door.IsOpen);
                        Shot("g48v_exit_nofire_closed.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                        bool opened = _door.TryOpenByPlayer(6f);
                        Check("右击门扇后开始开门", opened);
                        Shot("g48v_exit_nofire_open.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                    }
                    else
                    {
                        // 情形 2：门前有初期火焰 → 不灭火不能出去
                        Check("情形 2 出口未解锁", !_door.IsUnlocked);
                        _stage.TryArriveLobby(false);
                        Check("情形 2 门前生成初期火焰", _stage.stageTwoFire != null && _stage.stageTwoFire.IsBurning);
                        Shot("g48v_exit_fire.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                        Check("门前有火时不能直接判定出门", !_stage.TryEnterExit());
                        _stage.stageTwoFire.Extinguish();
                    }
                    Next(7);
                    return;
                }

                case 7:
                {
                    if (Time.realtimeSinceStartup - _phaseT < 1.2f) return;
                    if (_case == 2)
                    {
                        Check("灭掉门前火焰后出口开启", _door.IsUnlocked);
                        Shot("g48v_exit_fire_out.png", new Vector3(5.25f, 1.62f, 0.05f), new Vector3(5.25f, 1.20f, 1.02f), 60f);
                    }
                    else
                    {
                        Check("情形 1 右击后门扇已打开", _door.IsOpen);
                    }
                    bool done = _stage.TryEnterExit();
                    Check("走出安全出口判定成功", done);
                    Check("流程进入完成", _stage.CurrentStage == StageTwoFlow.Stage.Done || _door.PlayerEntered,
                          "stage=" + _stage.CurrentStage);
                    Shot("g48v_outside.png", new Vector3(7.20f, 1.62f, 1.02f), new Vector3(10.40f, 0.60f, 1.35f), 70f);
                    Finish("DONE");
                    return;
                }
            }
        }

        static bool ClickButton(string contains)
        {
            foreach (var b in UnityEngine.Object.FindObjectsByType<Button>(FindObjectsInactive.Exclude, FindObjectsSortMode.None))
            {
                if (b == null) continue;
                var labels = b.GetComponentsInChildren<Text>(true);
                foreach (var t in labels)
                {
                    if (t != null && t.text != null && t.text.Contains(contains))
                    {
                        b.onClick.Invoke();
                        return true;
                    }
                }
            }
            return false;
        }

        static void PlacePlayer(Vector3 feet, Vector3 lookAt)
        {
            var origin = GameObject.Find("XR Origin (Drill)");
            if (origin != null)
            {
                var t = origin.transform;
                t.position = new Vector3(feet.x, t.position.y, feet.z);
            }
            if (_cam != null)
            {
                var dir = (lookAt - new Vector3(feet.x, 1.62f, feet.z)).normalized;
                _cam.rotation = Quaternion.LookRotation(dir, Vector3.up);
            }
        }

        static void Finish(string tag)
        {
            try
            {
                File.WriteAllText(Path.Combine(ShotDir, "_round48_probe_" + _case + "_" + _role + ".txt"),
                    string.Join("\n", Log.ToArray()) + "\n\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1) + "\n");
            }
            catch (Exception e) { Debug.LogError("[G48] 报告写入失败：" + e.Message); }
            Debug.Log("[G48] " + tag + "，失败数=" + Fails.Count);
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }

        static void Shot(string name, Vector3 pos, Vector3 lookAt, float fov = 0f)
        {
            try
            {
                var tr = _cam;
                var oldPos = tr.position; var oldRot = tr.rotation; var oldParent = tr.parent;
                tr.SetParent(null, true);
                tr.position = pos; tr.rotation = Quaternion.LookRotation((lookAt - pos).normalized, Vector3.up);

                var cam = _camera != null ? _camera : tr.GetComponent<Camera>();
                const int w = 1600, h = 900;
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture; var oldEye = cam.stereoTargetEye; float oldFov = cam.fieldOfView;
                if (fov > 1f) cam.fieldOfView = fov;
                cam.stereoTargetEye = StereoTargetEyeMask.None;
                cam.targetTexture = rt;
                cam.Render();
                RenderTexture.active = rt;
                var tex = new Texture2D(w, h, TextureFormat.RGB24, false);
                tex.ReadPixels(new Rect(0, 0, w, h), 0, 0);
                tex.Apply();
                File.WriteAllBytes(Path.Combine(ShotDir, name), tex.EncodeToPNG());
                Say("截图 " + name);
                UnityEngine.Object.Destroy(tex);
                cam.targetTexture = oldTarget; cam.stereoTargetEye = oldEye;
                if (fov > 1f) cam.fieldOfView = oldFov;
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                tr.position = oldPos; tr.rotation = oldRot;
                if (oldParent != null) tr.SetParent(oldParent, true);
            }
            catch (Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
        }
    }
}
