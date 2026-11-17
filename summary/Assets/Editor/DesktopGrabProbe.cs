#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.InputSystem.LowLevel;
using UnityEngine.UI;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 桌面鼠标回归探针：从「开始页的情景按钮」真实入口进入情景二 / 情景三，
    /// 在真 Play 里把鼠标挪到贴墙灭火器上右键单击，验收
    ///   ① 点情景按钮后 DrillStarted 必须为真（否则输入层会一直拦「请先开始演练」）
    ///   ② 右键能选中该情景对应的灭火器
    ///   ③ 常识问答面板真的弹出，题库组与该情景匹配、且在玩家正前方
    ///   ④ 答对 3/5 后能拿起灭火器（grab.IsHeld）
    /// 结果写 Logs/shots/_deskgrab.txt，末两行 FAIL_COUNT / PROBE_EXIT。
    /// 入口：RunFromBatch(=情景二) / RunSc2 / RunSc3。
    /// </summary>
    public static class DesktopGrabProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        static string ShotDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs/shots");

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static int _phase;
        static float _t0, _phaseT, _stepT;
        static Mouse _mouse;
        static Camera _cam;
        static DesktopDrillInput _di;
        static DrillScenario _sc;
        static StartScreen _start;
        static Extinguisher _target;
        static int _scenario = 1;      // 1=纸张(Ext02) 2=茶水间(Ext03)
        static int _answered;
        static MouseState _press;
        static Keyboard _kb;

        public static void RunFromBatch() => Start(1);
        public static void RunSc2() => Start(1);
        public static void RunSc3() => Start(2);

        static void Start(int scenario)
        {
            _scenario = scenario;
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _t0 = Time.realtimeSinceStartup; _phaseT = _t0; _stepT = 0f;
            _answered = 0; _target = null;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[DESKGRAB] " + s); Log.Add(s); }

        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[DESKGRAB] ✘ " + s);
            Log.Add("✘ " + s); Fails.Add(s);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ShotDir, "_deskgrab.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Next() { _phase++; _phaseT = Time.realtimeSinceStartup; _stepT = 0f; }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (Time.realtimeSinceStartup - _t0 > 300f) { Fail("总超时"); Finish(1); return; }
            if (Time.realtimeSinceStartup - _phaseT > 45f) { Fail("阶段 " + _phase + " 超时"); Finish(1); return; }

            switch (_phase)
            {
                case 0: if (Ready()) { Say("播放态就绪：相机=" + _cam.name + " 眼高 " + _cam.transform.position.y.ToString("F2")); Next(); } break;
                case 1: StartScenario(); break;
                case 2: WaitEnter(); break;
                case 3: Aim(); break;
                case 4: RightClick(); break;
                case 5: CheckQuiz(); break;
                case 6: AnswerQuiz(); break;
                case 7: CheckGrab(); break;
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
            // 用自己的虚拟鼠标/键盘设备：批处理里系统鼠标每帧都会被真机状态覆盖，注入的按键留不住
            if (_mouse == null)
            {
                _mouse = InputSystem.AddDevice<Mouse>("ProbeMouse");
                _mouse.MakeCurrent();           // 必须让它成为 Mouse.current，否则输入层读的是系统鼠标
            }
            if (_kb == null) _kb = InputSystem.AddDevice<Keyboard>("ProbeKeyboard");
            Debug.Log("[DESKGRAB] 设备：Mouse=" + (Mouse.current != null) + " 是我们的吗=" + (Mouse.current == _mouse) + " Keyboard=" + (Keyboard.current != null)
                + " VRDrillInput.DrillInputActive=" + VRDrillInput.DrillInputActive);
            return true;
        }

        static string Name() => _scenario == 1 ? "Extinguisher02" : "Extinguisher03";
        static int SetIndex() => _scenario == 1 ? 4 : 5;
        static string Tag() => _scenario == 1 ? "sc2" : "sc3";

        static Extinguisher FindTarget()
        {
            foreach (var e in UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e.name == Name()) return e;
            return null;
        }

        static void StartScenario()
        {
            Say("======== 情景 " + (_scenario == 1 ? "二·办公桌纸张（Ext02 / E 组）" : "三·茶水间小家电（Ext03 / F 组）") + " ========");
            _start.StartScenario(_scenario);

            // ① 情景按钮必须让「演练已开始」成立（本轮修复的 bug 就在这里）
            bool started = _start.DrillStarted;
            Say("点情景按钮后：StartScreen.Started=" + _start.Started + " DrillStarted=" + started
                + " · DrillScenario.CurrentMode=" + _sc.CurrentMode);
            if (!started) Fail("点情景按钮后输入层仍认为「未开始」（DrillStarted=False），右键会被「请先开始演练」拦掉");
            Next();
        }

        static void WaitEnter()
        {
            if (Time.realtimeSinceStartup - _phaseT < 2.5f) return;
            Say("进入演练：阶段=" + _sc.CurrentPhase + " · 出生点下标=" + _sc.LastSpawnIndex
                + " · 目标瓶=" + Name());
            Next();
        }

        static void Aim()
        {
            _target = FindTarget();
            if (_target == null) { Fail("场景里找不到 " + Name()); Finish(1); return; }

            // 贴墙灭火器前 1.05 米、眼高 1.60，视线落在瓶身中部
            Vector3 bottle = _target.transform.position + new Vector3(0f, 0.30f, 0f);
            Vector3 stand = new Vector3(bottle.x, 0f, bottle.z + 1.05f);
            _cam.transform.root.position = new Vector3(stand.x, 0f, stand.z);
            _cam.transform.rotation = Quaternion.LookRotation(bottle - _cam.transform.position, Vector3.up);

            InputState.Change(_mouse, new MouseState { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) });

            Say("站位=" + stand.ToString("F2") + " 眼到瓶=" + Vector3.Distance(_cam.transform.position, bottle).ToString("F2")
                + " 米 · 鼠标回读=" + _mouse.position.ReadValue().ToString("F0"));
            Shot("vr20_deskgrab_" + Tag() + "_before.png");
            Next();
        }

        static void RightClick()
        {
            // 与 PickUnderMouse 同一条查询：先确认屏幕中心射线确实打在这瓶上
            Ray ray = _cam.ScreenPointToRay(new Vector2(Screen.width * 0.5f, Screen.height * 0.5f));
            var hits = Physics.RaycastAll(ray, 30f, ~0, QueryTriggerInteraction.Collide);
            Array.Sort(hits, (a, b) => a.distance.CompareTo(b.distance));
            Extinguisher first = null;
            foreach (var h in hits)
            {
                var e = h.collider.GetComponentInParent<Extinguisher>();
                if (e != null) { first = e; break; }
            }
            Say("中心射线首个灭火器=" + (first != null ? first.name : "null")
                + "（期望 " + _target.name + "），总命中=" + hits.Length);
            if (first == null || first != _target) Fail("右键射线没有打中 " + _target.name);

            Say("内部 PickUnderMouse()=" + Dbg(InvokeMethod(_di, "PickUnderMouse", null))
                + " · IsModalOpen=" + Dbg(InvokeMethod(_di, "IsModalOpen", null)));
            _press = new MouseState { position = new Vector2(Screen.width * 0.5f, Screen.height * 0.5f) };
            _press = _press.WithButton(MouseButton.Right, true);
            Press();      // 立即按一次；后面几帧若被系统复位就再补一次，保证 LateUpdate 能读到
            Say("已写入右键状态（wasPressedThisFrame=" + _mouse.rightButton.wasPressedThisFrame + "）");

            // 批处理里系统鼠标每帧会被真机状态覆盖，输入事件留不到 LateUpdate 那一帧，
            // 所以这里直接调用 LateUpdate 里右键按下时调用的同一个入口（等价于「玩家真的右键点了瓶身」）。
            InvokeMethod(_di, "OnRightClick", null);
            Say("已触发 OnRightClick（与真实右键同一入口）");
            Next();
        }

        static void CheckQuiz()
        {
            if (Time.realtimeSinceStartup - _phaseT < 1.1f) { KeepPressed(); return; }
            var p = _target != null ? _target.quizPanel : null;
            Say("右击后：面板=" + (p != null ? p.name : "null") + " IsOpen=" + (p != null && p.IsOpen)
                + " 题库组=" + (p != null ? p.questionSetIndex : -1) + "（期望 " + SetIndex() + "）"
                + " 题数=" + (p != null ? p.QuestionCount : 0)
                + " · di.quizPanel=" + (_di.quizPanel != null ? _di.quizPanel.name : "null")
                + " · HUD=" + HudLine());

            if (p == null || !p.IsOpen) Fail("右键后常识问答面板没有弹出（这就是「拿不起」的直接原因）");
            else if (p.questionSetIndex != SetIndex()) Fail("弹出的题库组不是本情景的：" + p.questionSetIndex);

            if (p != null && p.IsOpen)
            {
                p.advanceDelay = 0.05f;      // 探针连答，缩短每题结果停留时间
                Shot("vr20_deskgrab_" + Tag() + "_quiz.png");
                float d = PanelDistance(p);
                if (d < 0f) Fail("问答面板不在玩家正前方（屏幕外或跑到身后）");
                else Say("问答面板在正前方 " + d.ToString("F2") + " 米、屏幕内");
            }
            Next();
        }

        static void Press()
        {
            InputState.Change(_mouse, _press);
        }

        /// <summary>系统每帧可能把设备状态复位，这里在按键松开前持续补按，保证 LateUpdate 那帧能看到按下。</summary>
        static void KeepPressed()
        {
            if (_mouse == null) return;
            if (!_mouse.rightButton.isPressed) Press();
        }

        static void AnswerQuiz()
        {
            var p = _target.quizPanel;
            if (p == null || !p.IsOpen) { Fail("问答面板不在打开状态，无法作答"); Finish(1); return; }

            if (_stepT > 0f && Time.realtimeSinceStartup - _stepT < 0.35f) return;
            _stepT = Time.realtimeSinceStartup;

            // 面板要跑完 5 题才结算：前 3 题答对，后 2 题故意答错，最终 3/5 = 60% 通过
            int idx = Mathf.Clamp(p.QuestionIndex, 0, p.questions.Count - 1);
            int ci = p.questions[idx].correctIndex;
            int pick = _answered < 3 ? ci : (ci + 1) % 3;
            p.AnswerOption(pick);
            _answered++;
            p.AdvanceTime(0.20f);
            Say("第 " + _answered + " 题作答（" + (_answered <= 3 ? "对" : "错") + "）· 累计正确 " + p.CorrectCount + "/" + p.QuestionCount);
            if (_answered >= 5) { p.AdvanceTime(0.30f); Say("第 5 题后：Passed=" + p.Passed + " Failed=" + p.Failed); Next(); }
        }

        static void CheckGrab()
        {
            if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
            bool held = _target.grab != null && _target.grab.IsHeld;
            Say("答对 3/5 后：面板Passed=" + (_target.quizPanel != null && _target.quizPanel.Passed)
                + " 面板IsOpen=" + (_target.quizPanel != null && _target.quizPanel.IsOpen)
                + " · grab.IsHeld=" + held
                + " · Extinguisher.QuizPassed=" + _target.QuizPassed
                + " · HUD=" + HudLine());
            if (!held) Fail("问答通过后灭火器没有拿到手里");
            else Shot("vr20_deskgrab_" + Tag() + "_held.png");

            if (_target.grab != null && _target.grab.IsHeld) _target.grab.ReleaseExternal();
            Next();
        }

        static object InvokeMethod(object target, string name, object[] args)
        {
            var m = target.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            return m != null ? m.Invoke(target, args) : "方法不存在";
        }

        static string Dbg(object v) => v == null ? "null" : (v is Extinguisher e ? e.name : v.ToString());

        static string HudLine()
        {
            var f = typeof(DesktopDrillInput).GetField("_hudText",
                System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
            var t = f != null ? f.GetValue(_di) as Text : null;
            if (t == null) return "?";
            string s = (t.text ?? "").Replace("\n", " ");
            return s.Length > 400 ? s.Substring(0, 400) : s;
        }

        /// <summary>问答面板的 WorldSpace 画布是否在相机正前方可见；返回距离，-1 表示不可见。</summary>
        static float PanelDistance(ExtinguisherQuizPanel p)
        {
            if (p == null) return -1f;
            var canvas = p.GetComponentInChildren<Canvas>(true);
            Vector3 pos = canvas != null ? canvas.transform.position : p.transform.position;
            var vp = _cam.WorldToViewportPoint(pos);
            if (vp.z < 0.05f || vp.x < -0.05f || vp.x > 1.05f || vp.y < -0.05f || vp.y > 1.05f) return -1f;
            return vp.z;
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
#endif
