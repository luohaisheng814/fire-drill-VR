#if UNITY_EDITOR
using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 28 轮玩家视角探针：
    /// ① 走到本情景那瓶灭火器前，看瓶身灭火剂类型标签；
    /// ② 拿在手上 → 拔销 → 压把 → 对着火喷，截一张喷射形态图并核对音效 clip；
    /// ③ 火灭瞬间截「收尾特效」图，核对 ExtinguishFx.SpawnCount / LastKind；
    /// ④ 走到门外广场截知识墙与地面标语，判读是否可读。
    /// 入口：RunSc1 / RunSc2 / RunSc3，结果写 Logs/shots/_extfx.txt，末两行 FAIL_COUNT / PROBE_EXIT。
    /// </summary>
    public static class ExtinguisherFxProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        static string ShotDir => Path.Combine(Directory.GetCurrentDirectory(), "Logs/shots");

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();

        static int _scenario;
        static int _phase;
        static float _t0, _phaseT, _stepT;
        static Camera _cam;
        static DrillScenario _sc;
        static StartScreen _start;
        static Extinguisher _target;
        static Transform _handAnchor;
        static int _fxBefore;
        static string _tag = "sc1";

        static readonly ExtinguisherKind[] ExpectKind =
        {
            ExtinguisherKind.CO2, ExtinguisherKind.PowderABC, ExtinguisherKind.WetChemical
        };
        static readonly string[] BottleName = { "Extinguisher01", "Extinguisher02", "Extinguisher03" };

        public static void RunSc1() => Start(0);
        public static void RunSc2() => Start(1);
        public static void RunSc3() => Start(2);

        static void Start(int scenario)
        {
            _scenario = scenario;
            _tag = "sc" + (scenario + 1);
            Directory.CreateDirectory(ShotDir);
            Log.Clear(); Fails.Clear();
            _phase = 0; _t0 = Time.realtimeSinceStartup; _phaseT = _t0; _stepT = 0f;
            _target = null; _handAnchor = null;
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[EXTFX] " + s); Log.Add(s); }

        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[EXTFX] ✘ " + s);
            Log.Add("✘ " + s); Fails.Add(s);
        }

        static void Finish(int code)
        {
            EditorApplication.update -= Tick;
            File.WriteAllText(Path.Combine(ShotDir, "_extfx.txt"),
                string.Join("\n", Log) + "\nFAIL_COUNT=" + Fails.Count + "\nPROBE_EXIT=" + code + "\n",
                new System.Text.UTF8Encoding(false));
            if (Application.isBatchMode) EditorApplication.Exit(code);
        }

        static void Next() { _phase++; _phaseT = Time.realtimeSinceStartup; _stepT = 0f; }

        static void Tick()
        {
            if (!EditorApplication.isPlaying) return;
            if (Time.realtimeSinceStartup - _t0 > 300f) { Fail("总超时"); Finish(1); return; }
            if (Time.realtimeSinceStartup - _phaseT > 60f) { Fail("阶段 " + _phase + " 超时"); Finish(1); return; }

            switch (_phase)
            {
                case 0: if (Ready()) { Say("播放态就绪：" + _tag + " · 相机=" + _cam.name); Next(); } break;
                case 1: Begin(); break;
                case 2: WaitSpawn(); break;
                case 3: LookAtBottle(); break;
                case 4: GrabAndPrep(); break;
                case 5: LookAtFire(); break;
                case 6: Spray(); break;
                case 7: WaitBurnOut(); break;
                case 8: PlazaWall(); break;
                case 9: FloorText(); break;
                case 10: Finish(Fails.Count == 0 ? 0 : 1); break;
            }
        }

        static bool Ready()
        {
            if (_sc == null) _sc = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (_start == null) _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
            if (_cam == null) _cam = Camera.main;
            return _sc != null && _start != null && _cam != null;
        }

        static void Begin()
        {
            Say("======== 情景" + (new[] { "一·电气(Ext01/CO₂)", "二·纸张(Ext02/干粉)", "三·茶水间(Ext03/水基)" })[_scenario] + " ========");
            _start.StartScenario(_scenario);
            Say("开始演练：DrillStarted=" + _start.DrillStarted + " 阶段=" + _sc.CurrentPhase
                + " 题库组=" + (_sc.deviceExtinguisher != null && _sc.deviceExtinguisher.quizPanel != null ? _sc.deviceExtinguisher.quizPanel.questionSetIndex.ToString() : "?"));

            // 电气情景必须先断电，否则灭火被锁
            if (_sc.CurrentScenario != null && _sc.CurrentScenario.requirePowerCut)
            {
                var ps = _sc.powerSwitch;
                bool cut = ps != null && ps.Cut();
                Say("电源总闸：" + (ps != null ? ps.switchName : "null") + " 切断=" + cut + " IsCut=" + (ps != null && ps.IsCut));
                if (!cut) Fail("电气情景断电失败，灭火会被锁");
            }
            Next();
        }

        static void WaitSpawn()
        {
            if (Time.realtimeSinceStartup - _phaseT < 2.5f) return;
            _target = FindBottle();
            if (_target == null) { Fail("找不到 " + BottleName[_scenario]); Finish(1); return; }
            Say("目标瓶=" + _target.name + " 类型=" + _target.KindLabel + "（适用 " + _target.SuitableLabel + "）"
                + " · 位置=" + _target.transform.position.ToString("F2")
                + " · 期望类型=" + ExtinguisherKindInfo.Name(ExpectKind[_scenario]));
            if (_target.kind != ExpectKind[_scenario]) Fail("瓶的灭火剂类型不对：" + _target.kind);
            Next();
        }

        static Extinguisher FindBottle()
        {
            foreach (var e in UnityEngine.Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e.name == BottleName[_scenario]) return e;
            return null;
        }

        /// <summary>站到瓶前 1.0 米、眼高 1.6，看瓶身中上部（含类型标签）。</summary>
        static void LookAtBottle()
        {
            Vector3 bottle = _target.transform.position;
            Vector3 look = bottle + new Vector3(0f, 0.58f, 0f);   // 标签挂在瓶顶上方
            Vector3 stand = new Vector3(bottle.x, 0f, bottle.z) + HorizontalFromWall(bottle, 1.15f);

            MoveStand(stand, look);
            foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None)) { if (c.name.StartsWith("KindTag")) Say("标签画布：" + c.name + " pos=" + c.transform.position.ToString("F2") + " 缩放=" + c.transform.lossyScale.ToString("F3") + " active=" + c.gameObject.activeInHierarchy); }
            Say("看瓶：站位=" + stand.ToString("F2") + " 眼到标签="
                + Vector3.Distance(_cam.transform.position, look).ToString("F2") + " 米");
            Shot("vr28_fx_" + _tag + "_1bottle.png");
            Next();
        }

        /// <summary>从瓶指向房间中心的方向，避免站进墙里。</summary>
        static Vector3 HorizontalFromWall(Vector3 bottle, float dist)
        {
            Vector3 center = new Vector3(1.6f, 0f, 0.6f);
            Vector3 dir = center - new Vector3(bottle.x, 0f, bottle.z);
            dir.y = 0f;
            if (dir.sqrMagnitude < 0.01f) dir = Vector3.forward;
            return dir.normalized * dist;
        }

        static void MoveStand(Vector3 stand, Vector3 lookAt)
        {
            // 探针直接摆位时先关掉玩家根上的 CharacterController：
            // 它会在随后的帧里把「站进桌柜体积」的相机顶开，导致取景悄悄退回上一个站位。
            var rig = _cam.transform.root;
            var cc = rig.GetComponent<CharacterController>();
            if (cc != null && cc.enabled) cc.enabled = false;
            rig.position = new Vector3(stand.x, 0f, stand.z);
            Vector3 eye = _cam.transform.position;
            Vector3 dir = lookAt - eye;
            if (dir.sqrMagnitude > 0.0001f)
                _cam.transform.rotation = Quaternion.LookRotation(dir.normalized, Vector3.up);
        }

        /// <summary>外部吸附到相机前的手部锚点，再走完「拔销 → 答题通过 → 压把」。</summary>
        static void GrabAndPrep()
        {
            if (_handAnchor == null)
            {
                var go = new GameObject("ProbeHandAnchor");
                go.transform.SetParent(_cam.transform, false);
                go.transform.localPosition = new Vector3(0.30f, -0.34f, 0.90f);
                go.transform.localRotation = Quaternion.identity;
                _handAnchor = go.transform;
            }

            _target.grab.GrabExternal(_handAnchor, HandSide.Right);
            if (!_target.grab.IsHeld) { Fail("外部吸附失败，灭火器没拿到手上"); Finish(1); return; }

            // 顺序按真实流程：先答题通过（才允许拔销）→ 拔销 → 压把
            _target.MarkQuizPassed();
            // 拿起瓶子会自动弹问答面板：探针已直接判定通过，这里把面板关掉，避免挡住喷射画面
            if (_target.quizPanel != null && _target.quizPanel.IsOpen) _target.quizPanel.Close();
            _target.PullPin();
            if (_target.handleLever != null) _target.handleLever.PressExternal();

            Say("拿瓶结果：IsHeld=" + _target.grab.IsHeld + " 拔销=" + _target.PinPulled
                + " 问答通过=" + _target.QuizPassed + " 压把=" + (_target.handleLever != null && _target.handleLever.PressedOnce)
                + " · 音效clip=" + (_target.sprayLoopClip != null ? _target.sprayLoopClip.name : "null"));
            if (!_target.PinPulled || !_target.QuizPassed) Fail("拔销 / 问答状态没建立起来");
            if (_target.sprayLoopClip == null || !_target.sprayLoopClip.name.Contains(ExtinguisherSfx.KindToken(_target.kind)))
                Fail("喷射音不是本类型的合成音：" + (_target.sprayLoopClip != null ? _target.sprayLoopClip.name : "null"));
            Next();
        }

        /// <summary>站到火前 2.2 米（从火指向房间内侧），视线对准火焰根部。</summary>
        static void LookAtFire()
        {
            var fire = _sc.deviceFire;
            if (fire == null) { Fail("场景里没有当前起火点"); Finish(1); return; }

            Vector3 aim = fire.AimPoint;
            Vector3 away = HorizontalFromWall(aim, 2.2f);
            Vector3 stand = new Vector3(aim.x, 0f, aim.z) + away;
            MoveStand(stand, aim);
            Say("对火：火点=" + aim.ToString("F2") + " 站位=" + stand.ToString("F2")
                + " 距离=" + Vector3.Distance(_cam.transform.position, aim).ToString("F2") + " 米"
                + " · 火焰剩余=" + (fire.progress01 * 100f).ToString("F0") + "%");
            Next();
        }

        static void Spray()
        {
            var fire = _sc.deviceFire;
            if (fire == null) { Fail("火源丢失"); Finish(1); return; }

            if (!_target.IsSpraying)
            {
                if (!_target.TryStartSpray())
                {
                    Fail("TryStartSpray 失败（拿瓶/拔销/答题/压把之一没满足）");
                    Finish(1); return;
                }
                _phaseT = Time.realtimeSinceStartup;
                _fxBefore = ExtinguishFx.SpawnCount;
                return;
            }

            if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;

            bool loopPlaying = _target.sprayLoopSource != null && _target.sprayLoopSource.isPlaying;
            int pc = _target.sprayParticles != null ? _target.sprayParticles.particleCount : -1;
            var noz = _target.nozzle;
            Vector3 aim = fire.AimPoint;
            float dot = -1f;
            if (noz != null)
            {
                Vector3 toFire = (aim - noz.position).normalized;
                dot = Vector3.Dot(noz.forward, toFire);
            }
            Say("手持诊断：相机=" + _cam.transform.position.ToString("F2") + " 相机前=" + _cam.transform.forward.ToString("F2") + " 锚点=" + (_handAnchor != null ? _handAnchor.position.ToString("F2") : "null") + " 瓶=" + _target.transform.position.ToString("F2") + " 瓶父=" + (_target.transform.parent != null ? _target.transform.parent.name : "null") + " 瓶本地=" + _target.transform.localPosition.ToString("F3"));
            Say("喷射几何：粒子数=" + pc + " 喷口=" + (noz != null ? noz.position.ToString("F2") : "null")
                + " 喷口朝向·对准火=" + dot.ToString("F2") + "（≥" + Mathf.Cos(_target.sprayAngle * Mathf.Deg2Rad).ToString("F2") + " 才算命中）"
                + " 粒子系统朝向=" + (_target.sprayParticles != null ? _target.sprayParticles.transform.forward.ToString("F2") : "null"));
            if (pc < 10) Fail("粒子数量过少（" + pc + "），喷射看不到");
            Say("喷射中：IsSpraying=" + _target.IsSpraying + " 粒子在播="
                + (_target.sprayParticles != null && _target.sprayParticles.isPlaying)
                + " 音效播放=" + loopPlaying
                + " clip=" + (_target.sprayLoopSource != null && _target.sprayLoopSource.clip != null
                    ? _target.sprayLoopSource.clip.name : "null"));
            if (!loopPlaying) Fail("喷射循环音没在播");

            string hud = HudLine();
            Say("HUD（灭火中）：" + hud);
            if (!hud.Contains(ExtinguisherKindInfo.Name(_target.kind).Split('（')[0]))
                Fail("HUD 没显示本情景灭火器类型");

            Shot("vr28_fx_" + _tag + "_2spray.png");
            Next();
        }

        static void WaitBurnOut()
        {
            var fire = _sc.deviceFire;
            if (fire == null) { Fail("火源丢失"); Finish(1); return; }

            if (!fire.IsExtinguished)
            {
                if (Time.realtimeSinceStartup - _phaseT > 12f && fire.progress01 < 0.05f)
                {
                    Fail("喷了 12 秒火焰血量几乎没掉（进度 " + fire.progress01.ToString("F2") + "）");
                    Finish(1);
                }
                return;
            }

            // 火刚灭：立刻截一张，收尾粒子应在 2~3 秒内还看得见
            Shot("vr28_fx_" + _tag + "_3burst.png");
            Say("火焰熄灭：progress=" + fire.progress01.ToString("F2")
                + " ExtinguishFx.SpawnCount " + _fxBefore + "→" + ExtinguishFx.SpawnCount
                + " LastKind=" + ExtinguishFx.LastKind);
            if (ExtinguishFx.SpawnCount <= _fxBefore) Fail("灭火瞬间没有生成收尾特效");
            if (ExtinguishFx.LastKind != _target.kind) Fail("收尾特效类型不对：" + ExtinguishFx.LastKind);

            if (_target.IsSpraying) _target.StopSprayExternal();   // 停喷
            Next();
        }

        static void PlazaWall()
        {
            if (Time.realtimeSinceStartup - _phaseT < 0.4f) return;

            // 灭火器放下再走（真实玩家也不会举着瓶子去看知识墙）
            if (_target != null && _target.grab != null && _target.grab.IsHeld)
            {
                _target.grab.ReleaseExternal();
                Say("已放下灭火器：" + _target.name + " IsHeld=" + _target.grab.IsHeld);
            }

            // 站在走廊口内侧，正对东侧知识墙（相当于玩家刚走出来抬头看）
            MoveStand(new Vector3(9.40f, 0f, 1.30f), new Vector3(13.40f, 1.30f, 0.30f));
            Say("知识墙总览：站位=(9.40,0,1.30) 视点=(13.40,1.30,0.30) 距离="
                + Vector3.Distance(_cam.transform.position, new Vector3(13.40f, 1.30f, 0.30f)).ToString("F2") + " 米");
            Shot("vr28_fx_" + _tag + "_4wall.png");

            MoveStand(new Vector3(11.70f, 0f, 0f), new Vector3(13.40f, 1.32f, 0f));
            Shot("vr28_fx_" + _tag + "_5wallclose.png");

            var board = UnityEngine.Object.FindFirstObjectByType<PlazaKnowledgeBoard>();
            if (board == null) Fail("广场上没有知识墙组件");
            else Say("知识墙组件：" + board.name + " 展板=" + board.panelTitles.Length + " 块 · 地面字=\""
                     + board.floorText + "\"");
            Next();
        }

        static void FloorText()
        {
            if (Time.realtimeSinceStartup - _phaseT < 0.3f) return;
            // 玩家从走廊出来朝东走，地面字的字头朝东，从西侧看才是正读
            MoveStand(new Vector3(8.30f, 0f, 1.55f), new Vector3(10.90f, 0.02f, 1.55f));
            Say("地面标语：站位=(8.30,0,1.55) 视点=(10.90,0.02,1.55) 朝东正读");
            Shot("vr28_fx_" + _tag + "_6floor.png");
            Next();
        }

        static string HudLine()
        {
            var di = UnityEngine.Object.FindFirstObjectByType<DesktopDrillInput>();
            if (di == null) return "?";
            var f = typeof(DesktopDrillInput).GetField("_hudText",
                BindingFlags.Instance | BindingFlags.NonPublic);
            var t = f != null ? f.GetValue(di) as UnityEngine.UI.Text : null;
            if (t == null) return "?";
            string s = (t.text ?? "").Replace("\n", " ");
            return s.Length > 500 ? s.Substring(0, 500) : s;
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
