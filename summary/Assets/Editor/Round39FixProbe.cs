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
    /// 第39轮玩家视角验收：
    /// 1) 开始界面在「操作演练」下方标出「技能操作型情景教学」；
    /// 2) 茶水间取瓶站位距火源已超出 1.55 米危险半径，站立 2.4 秒不扣血，并截图窗+瓶；
    /// 3) 单机灭火器进入答题面板，答题中不再出现「距灭火器」提示；
    /// 4) 复现茶水间「火灭瞬间手里还拿着 03 号瓶」的第三阶段，出口旁仍有备用瓶。
    /// 批处理：-executeMethod FireSafety.EditorTools.Round39FixProbe.Run（不要加 -nographics / -quit）
    /// </summary>
    public static class Round39FixProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 95f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _phase;
        static float _t0, _phaseT;
        static bool _entered;
        static StartScreen _start;
        static DrillScenario _scenario;
        static StageTwoFlow _stageTwo;
        static PlayerVitals _vitals;
        static Extinguisher _ext01, _ext02, _ext03;
        static Transform _rig, _cam;
        static CharacterController[] _ccs;
        static int _hpBefore;
        static float _nearestBefore;
        static Extinguisher _nearestAtExit;
        static bool _e3HeldAtBegin;

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

        static void Say(string s) { Debug.Log("[G39] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G39] × " + s);
            Log.Add("× " + s);
            Fails.Add(s);
        }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        static void Next(int p)
        {
            _phase = p;
            _phaseT = Time.realtimeSinceStartup;
            Say("→ 阶段 " + p);
        }

        static void Tick()
        {
            if (!Application.isPlaying)
            {
                if (_t0 > 0f && Time.realtimeSinceStartup - _t0 > HardTimeout) EditorApplication.Exit(2);
                return;
            }
            if (!_entered)
            {
                _entered = true;
                _t0 = Time.realtimeSinceStartup;
                Say("进入 Play 模式");
            }
            if (Time.realtimeSinceStartup - _t0 > HardTimeout) { Finish("TIMEOUT"); return; }

            if (_phase == 0)
            {
                if (Time.realtimeSinceStartup - _t0 < 2.0f || !Resolve()) return;
                DrillLocale.SetEnglish(false);
                _start.SetVisible(true);
                Check("开始界面：操作演练下方标出教学类型",
                    _start.TutorialTypeText.Contains("技能操作型情景教学")
                    && _start.TutorialTypeText.Contains("程序性技能训练"),
                    "实际「" + _start.TutorialTypeText + "」");
                Shot("vr39_start_tutorial_type.png");
                _start.SetVisible(false);
                _scenario.StartFormal(-1, 2);   // 茶水间情景
                Next(1);
            }
            else if (_phase == 1)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                _vitals.ResetVitals();
                _hpBefore = _vitals.currentHp;
                TeleportTo(new Vector3(4.90f, 0f, -2.50f));
                LookAtPoint(new Vector3(6.30f, 1.05f, -3.33f));
                Next(2);
            }
            else if (_phase == 2)
            {
                if (Time.realtimeSinceStartup - _phaseT < 2.4f) return;
                _nearestBefore = _vitals.NearestFireDistance;
                Check("茶水间取瓶站位：距火源 > 1.55 米危险半径",
                    _nearestBefore > _vitals.fireDangerRadius,
                    "实际 " + _nearestBefore.ToString("F2") + " 米，半径 " + _vitals.fireDangerRadius.ToString("F2"));
                Check("茶水间取瓶站位：站立 2.4 秒不触发火焰烧伤",
                    _vitals.currentHp == _hpBefore && !_vitals.NearFire,
                    "生命值 " + _hpBefore + "→" + _vitals.currentHp + "，NearFire=" + _vitals.NearFire);
                Check("茶水间灭火器：已挪到东墙南段新窗下方",
                    _ext03 != null && _ext03.transform.position.z < -3.0f
                    && _ext03.transform.position.z > -3.6f,
                    _ext03 != null ? _ext03.transform.position.ToString("F2") : "null");
                Check("茶水间东墙南段：空白墙已用窗洞/窗框封闭",
                    GameObject.Find("WindowWall_East_South") != null
                    && GameObject.Find("Window_EastSouth") != null);
                Shot("vr39_pantry_ext_window.png");
                Next(3);
            }
            else if (_phase == 3)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                _ext03.quizRequired = false;   // 只验答题面板，不让拾取再叠一层问答
                _ext03.quizPanel.Open(_ext03.transform);
                Next(4);
            }
            else if (_phase == 4)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                Check("答题面板：单机灭火器进入答题界面", _ext03.quizPanel.IsOpen);
                Check("答题面板：距离提示行不弹出", !_ext03.quizPanel.DistanceHintVisible);
                Check("答题面板：距离文案为空，不再出现「距灭火器」",
                    !_ext03.quizPanel.DistanceText.Contains("距灭火器"));
                Check("答题面板：教练距离 Toast/HUD 被抑制",
                    _ext03.GetComponent<ExtinguisherCoach>().HintsSuppressed);
                Shot("vr39_quiz_no_distance.png");
                _ext03.quizPanel.Close();
                Next(5);
            }
            else if (_phase == 5)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                // 复现：火灭瞬间 03 号瓶还在玩家手里，skipEvacuation 直接进第三阶段
                _ext03.quizRequired = false;   // 避免拾取时再次弹问答遮挡出口截图
                _ext03.grab.GrabExternal(_cam, HandSide.Right);
                Next(6);
            }
            else if (_phase == 6)
            {
                if (!_ext03.grab.IsHeld) { Fail("探针：03 号瓶未进入手持状态，无法复现"); Finish("GRAB_FAIL"); return; }
                if (Time.realtimeSinceStartup - _phaseT < 0.45f) return;
                _e3HeldAtBegin = _ext03.grab.IsHeld;
                Say("复现条件：进入第三阶段前 03 号瓶 IsHeld=" + _e3HeldAtBegin
                    + "，位置=" + _ext03.transform.position.ToString("F2"));
                _stageTwo.Begin();
                Next(7);
            }
            else if (_phase == 7)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                if (_stageTwo.stageTwoFire != null)
                {
                    Vector3 fp = _stageTwo.stageTwoFire.transform.position;
                    Vector3 sp = _stageTwo.spareExtinguisherSpot;
                    float d = Vector2.Distance(new Vector2(fp.x, fp.z), new Vector2(sp.x, sp.z));
                    Say("第三阶段火源=" + fp.ToString("F2") + "，备用瓶落点距火=" + d.ToString("F2") + " 米");
                    Check("出口备用瓶落点不在火焰危险半径内（> 1.55 + 0.3 米）", d > 1.85f, d.ToString("F2") + " 米");
                }
                _nearestAtExit = FindNearestExtinguisher(_stageTwo.spareExtinguisherSpot);
                Check("茶水间第三阶段：进入时 03 号瓶确实还在手里（根因条件成立）", _e3HeldAtBegin);
                Check("茶水间第三阶段：出口旁仍有可拾取的备用灭火器",
                    _nearestAtExit != null
                    && Vector3.Distance(new Vector3(_nearestAtExit.transform.position.x, 0f, _nearestAtExit.transform.position.z),
                                        new Vector3(_stageTwo.spareExtinguisherSpot.x, 0f, _stageTwo.spareExtinguisherSpot.z)) < 0.6f,
                    _nearestAtExit != null ? (_nearestAtExit.name + " @ " + _nearestAtExit.transform.position.ToString("F2")) : "无");
                Check("茶水间第三阶段：被手持的 03 号瓶没有被错误地当作出口备用瓶",
                    _nearestAtExit != null && _nearestAtExit != _ext03,
                    _nearestAtExit != null ? _nearestAtExit.name : "null");
                _ext03.grab.ReleaseExternal();
                _ext03.grab.ForceRelease();
                Next(8);
            }
            else if (_phase == 8)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.5f) return;
                _ext03.quizPanel.Close();
                _vitals.enabled = false;
                DrillToast.HideNow();
                var hud = Object.FindObjectOfType<VRDrillHud>();
                if (hud != null) hud.suppressed = true;
                if (DrillCountdown.Instance != null) DrillCountdown.Instance.SetHudVisible(false);
                foreach (var ext in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                {
                    var coach = ext.GetComponent<ExtinguisherCoach>();
                    if (coach != null) coach.HintsSuppressed = true;
                    // 大信息牌由 LateUpdate 每帧强制显示，必须停组件再关标签根
                    var marker = ext.GetComponentInChildren<ExtinguisherGuideMarker>(true);
                    if (marker != null)
                    {
                        marker.enabled = false;
                        if (marker.LabelTransform != null)
                            marker.LabelTransform.gameObject.SetActive(false);
                    }
                }
                // 世界空间信息牌/类型牌在近景会整个挡住瓶身；截图只保留瓶与出口
                foreach (var go in Object.FindObjectsByType<GameObject>(FindObjectsSortMode.None))
                {
                    if (go.name.StartsWith("KindTag_") || go.name == "ExtinguisherGuideLabel"
                        || go.name == "GuideArrow" || go.name == "GuideFloorRing"
                        || go.name == "Canvas_VRHud")
                        go.SetActive(false);
                }
                Next(9);
            }
            else if (_phase == 9)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                // 第39轮补拍：上一版用 rig 转角 + LookAtPoint，累积偏航把机位甩到门外，
                // 截出来全是天空和地面。这里直接给主相机设世界位姿（脱离 rig 后立即渲染），
                // 保证画面里同时有安全出口门、门前火和出口旁备用瓶。
                // 第39轮补拍第二轮：机位遮挡反复，改为一次出三张候选，选最清楚的一张。
                if (_nearestAtExit != null)
                {
                    Vector3 ep = _nearestAtExit.transform.position;
                    Say("出口备用瓶实际位置=" + ep.ToString("F2")
                        + " 落点=" + _stageTwo.spareExtinguisherSpot.ToString("F2"));
                    int dumped = 0;
                    foreach (var r in Object.FindObjectsOfType<MeshRenderer>())
                    {
                        Vector3 p = r.transform.position;
                        if (p.x < 2.5f || p.x > 6.6f || p.z < 1.0f || p.z > 5.4f) continue;
                        Vector3 bs = r.bounds.size;
                        if (bs.x < 0.12f || bs.y < 0.12f) continue;
                        Say("  邻域 " + r.name + " @" + p.ToString("F2") + " 尺寸=" + bs.ToString("F2"));
                        if (++dumped >= 26) break;
                    }
                }
                ShotAt("vr39_exit_spare_extinguisher.png",
                    new Vector3(3.30f, 1.70f, 3.45f), new Vector3(4.30f, 0.55f, 1.60f), 70f);
                ShotAt("vr39_exit_spare_extinguisher_closeup.png",
                    new Vector3(2.80f, 1.60f, 2.10f), new Vector3(3.75f, 0.30f, 2.75f), 60f);
                Finish("DONE");
            }
        }

        static bool Resolve()
        {
            _start = Object.FindObjectOfType<StartScreen>();
            _scenario = Object.FindObjectOfType<DrillScenario>();
            _stageTwo = Object.FindObjectOfType<StageTwoFlow>();
            _vitals = Object.FindObjectOfType<PlayerVitals>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            _ext01 = FindExtinguisher("Extinguisher01");
            _ext02 = FindExtinguisher("Extinguisher02");
            _ext03 = FindExtinguisher("Extinguisher03");
            if (_start == null) { Fail("找不到 StartScreen"); return false; }
            if (_scenario == null) { Fail("找不到 DrillScenario"); return false; }
            if (_stageTwo == null) { Fail("找不到 StageTwoFlow"); return false; }
            if (_vitals == null) { Fail("找不到 PlayerVitals"); return false; }
            if (_rig == null || _cam == null) { Fail("找不到 XR Origin / 主相机"); return false; }
            if (_ext03 == null) { Fail("找不到 Extinguisher03"); return false; }
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return true;
        }

        static Extinguisher FindExtinguisher(string name)
        {
            foreach (var e in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e.name == name) return e;
            return null;
        }

        static Extinguisher FindNearestExtinguisher(Vector3 spot)
        {
            Extinguisher best = null;
            float bestDist = float.MaxValue;
            foreach (var e in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
            {
                if (e == null || !e.gameObject.activeInHierarchy) continue;
                float d = Vector3.Distance(new Vector3(e.transform.position.x, 0f, e.transform.position.z),
                                           new Vector3(spot.x, 0f, spot.z));
                if (d < bestDist) { bestDist = d; best = e; }
            }
            Say("出口落点最近灭火器：" + (best != null ? best.name : "none") + "，距离 " + bestDist.ToString("F2") + " 米");
            return best;
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

        static void Shot(string name)
        {
            ShotAt(name, _cam.position, _cam.position + _cam.forward * 3f);
        }

        /// <summary>
        /// 把主相机临时从 XR rig 上摘下来，摆到这个世界位姿再渲染。
        /// XR 的 TrackedPoseDriver 每帧覆盖 local pose，但渲染前没有 Update，所以这一帧有效；
        /// 渲染完立刻挂回原父节点，不改变玩家实际站位。
        /// </summary>
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
                Say("玩家视角截图 " + name + "（" + w + "×" + h + "，机位 " + pos.ToString("F2")
                    + " → " + lookAt.ToString("F2") + "）");
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
            File.WriteAllText(Path.Combine(ShotDir, "_round39_fix.txt"), sb.ToString());
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
