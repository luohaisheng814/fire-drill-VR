using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine.UI;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 27 轮：房间门（door_b）玩家视角回归探针。
    /// 全程用真实 Play 相机站位 + 离屏出图，按玩家视角判读：
    ///   ① 情景二/三刚进演练时门是关的、右键只给「先把第一阶段的火灭掉」；
    ///   ② 第一阶段火灭之后门变成可打开，右键真的把门扇转开；
    ///   ③ 门开之后玩家能真的穿过门洞走到大厅/出口一侧（不是被看不见的碰撞挡住）。
    /// 结果写 Logs/shots/_roomdoor.txt，末两行 FAIL_COUNT / PROBE_EXIT。
    /// </summary>
    public static class RoomDoorProbe
    {
        static string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        static string ShotDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs/shots");

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static int _phase;
        static float _t0, _phaseT;
        static Mouse _mouse;
        static Keyboard _kb;
        static Camera _cam;
        static DesktopDrillInput _di;
        static DrillScenario _sc;
        static StartScreen _start;
        static RoomDoor _door;
        static int _scenario = 1;      // 1=情景二(纸张) 2=情景三(茶水间)

        static Vector3 _doorPos;
        static float _angleBefore;

        public static void RunSc2() => Start(1);
        public static void RunSc3() => Start(2);

        static void Start(int scenario)
        {
            _scenario = scenario;
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _t0 = Time.realtimeSinceStartup; _phaseT = _t0;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static string Tag() => _scenario == 1 ? "sc2" : "sc3";

        static void Say(string s) { Debug.Log("[ROOMDOOR] " + s); Log.Add(s); }

        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[ROOMDOOR] ✘ " + s);
            Log.Add("✘ " + s); Fails.Add(s);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ShotDir, "_roomdoor.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Next() { _phase++; _phaseT = Time.realtimeSinceStartup; }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (Time.realtimeSinceStartup - _t0 > 300f) { Fail("总超时"); Finish(1); return; }
            if (Time.realtimeSinceStartup - _phaseT > 45f) { Fail("阶段 " + _phase + " 超时"); Finish(1); return; }

            switch (_phase)
            {
                case 0: if (Ready()) Next(); break;
                case 1: StartScenario(); break;
                case 2: StandAtDoor(); break;
                case 3: ClickBeforeStageOne(); break;
                case 4: ClearStageOne(); break;
                case 5: ClickAfterStageOne(); break;
                case 6: CheckDoorOpened(); break;
                case 7: WalkThrough(); break;
                case 8: Finish(Fails.Count == 0 ? 0 : 1); break;
            }
        }

        static bool Ready()
        {
            if (_di == null) _di = UnityEngine.Object.FindFirstObjectByType<DesktopDrillInput>();
            if (_sc == null) _sc = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (_start == null) _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
            if (_cam == null) _cam = Camera.main;
            if (_di == null || _sc == null || _start == null || _cam == null) return false;

            if (_mouse == null) { _mouse = InputSystem.AddDevice<Mouse>("ProbeMouse"); _mouse.MakeCurrent(); }
            if (_kb == null) _kb = InputSystem.AddDevice<Keyboard>("ProbeKeyboard");

            _door = UnityEngine.Object.FindFirstObjectByType<RoomDoor>();
            if (_door == null) { Fail("场景里没有 RoomDoor（door_b 没挂上）"); Finish(1); return false; }
            _doorPos = _door.transform.position;
            Say("房间门 = " + _door.gameObject.name + " @ " + _doorPos.ToString("F2")
                + " · openAngle=" + _door.openAngle + " · 门扇子碰撞=" + _door.GetComponentsInChildren<Collider>(true).Length);
            return true;
        }

        static void StartScenario()
        {
            Say("======== 情景 " + (_scenario == 1 ? "二·办公桌纸张" : "三·茶水间小家电") + " ========");
            _start.StartScenario(_scenario);
            Say("进入演练：ScenarioIndex=" + _sc.CurrentScenarioIndex
                + " skipEvacuation=" + (_sc.CurrentScenario != null && _sc.CurrentScenario.skipEvacuation)
                + " Phase=" + _sc.CurrentPhase);

            // 这里只等出生点落定，不做任何「作弊」：玩家仍站在情景出生点（南侧）
            Next();
        }

        /// <summary>站到门南侧 1.6 米处，眼睛看向门把手高度——和玩家走到门前看到的一样。</summary>
        static void StandAtDoor()
        {
            Vector3 stand = new Vector3(_doorPos.x - 0.45f, 0f, _doorPos.z - 1.75f);
            Vector3 aim = new Vector3(_doorPos.x - 0.45f, 1.05f, _doorPos.z);
            var rig = _cam.transform.root;
            rig.position = stand;

            // 先按玩家自然走进来的视角拍一张（正对门，门占画面中央）：
            // 这张用来对照用户截图里那扇「没有火的棕色玻璃窗房间门」。
            _cam.transform.rotation = Quaternion.LookRotation(aim - _cam.transform.position, Vector3.up);
            Shot("vr27_roomdoor_" + Tag() + "_0doorview.png");

            // 批处理里塞不进鼠标位置（InputState.Change / WarpCursorPosition 都不生效，读回一直是 (0,0)），
            // 那就反过来让「屏幕左下角 (0,0) 那条射线」正好指向门 —— 玩家把鼠标停在画面左下角点门，就是这个视角。
            float hy = Mathf.Tan(_cam.fieldOfView * 0.5f * Mathf.Deg2Rad);
            float hx = hy * _cam.aspect;
            Vector3 vLocal = new Vector3(-hx, -hy, 1f).normalized;
            Vector3 dir = (aim - _cam.transform.position).normalized;
            _cam.transform.rotation = Quaternion.LookRotation(dir, Vector3.up)
                                    * Quaternion.Inverse(Quaternion.LookRotation(vLocal, Vector3.up));

            float d = Vector3.Distance(new Vector3(_cam.transform.position.x, 0f, _cam.transform.position.z), _doorPos);
            Say("玩家站位=" + stand.ToString("F2") + " · 眼高=" + _cam.transform.position.y.ToString("F2")
                + " · 到门水平距离=" + d.ToString("F2") + " 米");

            // 关着的时候先拍一张：这就是玩家一阶段灭火后走到门前看到的画面
            Shot("vr27_roomdoor_" + Tag() + "_1closed.png");
            DumpRay();
            Next();
        }

        /// <summary>把「鼠标那条射线」（批处理里恒为屏幕左下角 0,0）的前几个命中打出来：判点门时被什么挡住。</summary>
        static void DumpRay()
        {
            Ray ray = _cam.ScreenPointToRay(Mouse.current != null ? Mouse.current.position.ReadValue() : Vector2.zero);
            var hits = Physics.RaycastAll(ray, 30f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            var sb = new System.Text.StringBuilder();
            sb.Append("中心射线命中 ").Append(hits.Length).Append(" 个：");
            for (int i = 0; i < hits.Length && i < 6; i++)
            {
                var h = hits[i];
                sb.Append(" [").Append(i).Append("] ").Append(h.collider.name)
                  .Append(" d=").Append(h.distance.ToString("F2"))
                  .Append(h.collider.isTrigger ? "(trigger)" : "")
                  .Append(" 是门件=").Append(_door.IsDoorPart(h.collider));
            }
            Say(sb.ToString());
            Say("射线起点=" + ray.origin.ToString("F2") + " 方向=" + ray.direction.ToString("F2")
                + " · 门中心=" + _doorPos.ToString("F2"));
        }

        static void ClickBeforeStageOne()
        {
            Say("[阶段一未完成] CanOpenNow=" + _door.CanOpenNow + " IsOpen=" + _door.IsOpen
                + " · Stage1Cleared=" + _sc.Stage1Cleared);
            if (_door.CanOpenNow) Fail("第一阶段火还没灭，门就已经是可打开状态（应当锁着）");

            InvokeMethod(_di, "OnRightClick", null);
            _angleBefore = _door.transform.localEulerAngles.y;
            Say("右键后：IsOpen=" + _door.IsOpen + " · HUD=" + HudLine());
            Say("方法探针：TryOpenExitDoorUnderMouse=" + InvokeMethod(_di, "TryOpenExitDoorUnderMouse", null)
                + " TryOpenRoomDoorUnderMouse=" + InvokeMethod(_di, "TryOpenRoomDoorUnderMouse", null)
                + " · RoomDoor.Instance=" + (RoomDoor.Instance != null ? RoomDoor.Instance.name : "null")
                + " · 探针拿到的是不是同一个=" + (RoomDoor.Instance == _door));
            var camUsed = InvokeMethod(_di, "GetCamera", null) as Camera;
            Say("输入层相机=" + (camUsed != null ? camUsed.name + " @ " + camUsed.transform.position.ToString("F2") : "null")
                + " · cameraTransform=" + (_di.cameraTransform != null ? _di.cameraTransform.position.ToString("F2") : "null")
                + " · Mouse.current=" + (Mouse.current != null ? Mouse.current.name : "null")
                + " 我们的是=" + (Mouse.current == _mouse)
                + " 鼠标位置=" + (Mouse.current != null ? Mouse.current.position.ReadValue().ToString("F0") : "-"));
            if (_door.IsOpen) Fail("第一阶段未完成时右键竟然把门打开了");
            if (!HudLine().Contains("第一阶段")) Fail("门锁着时右键没有提示「先把第一阶段的火灭掉」");

            Shot("vr27_roomdoor_" + Tag() + "_2locked.png");
            Next();
        }

        static void ClearStageOne()
        {
            if (_sc.deviceFire == null) { Fail("情景里没有 deviceFire，无法模拟灭火"); Finish(1); return; }
            _sc.deviceFire.Extinguish();
            Say("模拟第一阶段灭火：deviceFire=" + _sc.deviceFire.name + " IsExtinguished=" + _sc.deviceFire.IsExtinguished);
            Next();
        }

        static void ClickAfterStageOne()
        {
            Say("[阶段一已完成] Stage1Cleared=" + _sc.Stage1Cleared + " CanOpenNow=" + _door.CanOpenNow);
            if (!_sc.Stage1Cleared) Fail("灭火后 Stage1Cleared 仍为 false");
            if (!_door.CanOpenNow) Fail("第一阶段火已灭，门却仍然打不开");

            InvokeMethod(_di, "OnRightClick", null);
            Say("右键开门后：IsOpen(动画)=" + _door.IsOpen + " · HUD=" + HudLine());
            Next();
        }

        static void CheckDoorOpened()
        {
            if (Time.realtimeSinceStartup - _phaseT < 1.4f) return;   // 等 0.8 秒转门动画走完

            float y = _door.transform.localEulerAngles.y;
            float delta = Mathf.DeltaAngle(_angleBefore, y);
            Say("门扇 Y 角：" + _angleBefore.ToString("F1") + " → " + y.ToString("F1")
                + "（Δ=" + delta.ToString("F1") + "，期望接近 " + _door.openAngle + "）· IsOpen=" + _door.IsOpen);
            if (!_door.IsOpen) Fail("右键之后门没有真正转开");
            if (Mathf.Abs(Mathf.DeltaAngle(delta, _door.openAngle)) > 6f) Fail("门开到位后的角度与设定不符（Δ=" + delta.ToString("F1") + "）");

            Shot("vr27_roomdoor_" + Tag() + "_3opened.png");
            Next();
        }

        /// <summary>门开之后，玩家从南侧真的往北走过去，看是否被挡住（这是本轮最关键的验收）。</summary>
        static void WalkThrough()
        {
            var cc = _cam.transform.root.GetComponent<CharacterController>();
            if (cc == null) cc = _cam.GetComponentInParent<CharacterController>();
            if (cc == null) { Fail("玩家身上没有 CharacterController，无法验证走位"); Finish(1); return; }

            var rig = cc.transform;
            float startZ = _doorPos.z - 2.0f;
            rig.position = new Vector3(_doorPos.x - 0.45f, 0f, startZ);
            _cam.transform.rotation = Quaternion.LookRotation(new Vector3(0f, 0f, 1f), Vector3.up);

            var sb = new System.Text.StringBuilder();
            float z = startZ;
            int blockedSteps = 0;
            for (int i = 0; i < 60; i++)
            {
                Vector3 before = rig.position;
                cc.Move(new Vector3(0f, 0f, 0.1f));
                Vector3 after = rig.position;
                z = after.z;
                if (Mathf.Abs(after.z - before.z) < 0.02f) { blockedSteps++; if (blockedSteps > 3) break; }
                if (z > _doorPos.z + 1.0f) break;
            }
            sb.Append("逐步前进 60 步（每步 0.1 米）：终点 z=").Append(z.ToString("F2"))
              .Append("（起点 z=").Append(startZ.ToString("F2")).Append("，门在 z=").Append(_doorPos.z.ToString("F2")).Append("）");
            Say("穿过门洞：" + sb);
            if (z <= _doorPos.z + 0.2f) Fail("门虽然转开了，玩家仍然走不过去（走到 z=" + z.ToString("F2") + " 就被挡住）");

            // 走完回头拍一张：站在大厅一侧看出口门
            _cam.transform.rotation = Quaternion.LookRotation(new Vector3(5.25f, 1.2f, 1.02f) - _cam.transform.position, Vector3.up);
            Shot("vr27_roomdoor_" + Tag() + "_4crossed.png");
            Say("大厅一侧可见：" + HudLine());
            Next();
        }

        static object InvokeMethod(object target, string name, object[] args)
        {
            var m = target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return m != null ? m.Invoke(target, args) : "方法不存在";
        }

        static string HudLine()
        {
            var f = typeof(DesktopDrillInput).GetField("_hudText",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var t = f != null ? f.GetValue(_di) as Text : null;
            if (t == null) return "?";
            string s = (t.text ?? "").Replace("\n", " ");
            return s.Length > 400 ? s.Substring(0, 400) : s;
        }

        static void Shot(string file)
        {
            if (_cam == null) return;
            var rt = new RenderTexture(1280, 720, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB) { antiAliasing = 4 };
            var prev = _cam.targetTexture;
            _cam.targetTexture = rt;
            _cam.Render();
            RenderTexture.active = rt;
            var tex = new Texture2D(1280, 720, TextureFormat.RGB24, false);
            tex.ReadPixels(new Rect(0, 0, 1280, 720), 0, 0);
            tex.Apply();
            RenderTexture.active = null;
            _cam.targetTexture = prev;
            File.WriteAllBytes(Path.Combine(ShotDir, file), tex.EncodeToPNG());
            UnityEngine.Object.DestroyImmediate(tex);
            rt.Release();
        }
    }
}
