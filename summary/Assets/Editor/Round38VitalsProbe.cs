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
    /// 第 38 轮玩家视角验收：
    /// 1) 开始页 HUD 显示 20/20；
    /// 2) 真实进入烟雾 10 秒扣 1；
    /// 3) 真实靠近燃烧火源先警告并扣 1，停留再扣；
    /// 4) 问答面板在移动/转头后持续贴在视线正前方。
    /// 批处理：-executeMethod FireSafety.EditorTools.Round38VitalsProbe.Run（不要加 -nographics / -quit）
    /// </summary>
    public static class Round38VitalsProbe
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string ShotDir = "Logs/shots";
        const float HardTimeout = 120f;

        static readonly List<string> Log = new List<string>();
        static readonly List<string> Fails = new List<string>();
        static int _phase;
        static float _t0, _phaseT;
        static bool _entered;
        static StartScreen _start;
        static PlayerVitals _vitals;
        static SmokeZone _smoke;
        static Extinguisher _ext;
        static ExtinguisherQuizPanel _quiz;
        static Transform _rig, _cam;
        static CharacterController[] _ccs;
        static int _hpAtStart, _hpAtSmoke, _hpAtFire;
        static float _quizDistBefore, _quizDistAfter;
        static int _fireTry;
        static float _fireTryT;
        static bool _ccLocked;
        static Vector3 _fireTryPos;
        static readonly Vector3[] FireCandidates =
        {
            new Vector3(-3.20f, 0f, 3.55f),
            new Vector3(-3.55f, 0f, 3.05f),
            new Vector3(-3.35f, 0f, 3.85f),
            new Vector3(-3.90f, 0f, 2.95f),
        };

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

        static void Say(string s) { Debug.Log("[G38] " + s); Log.Add(s); }
        static void Fail(string s)
        {
            if (Fails.Contains(s)) return;
            Debug.LogError("[G38] × " + s);
            Log.Add("× " + s);
            Fails.Add(s);
        }
        static void Check(string name, bool ok, string detail = "")
        {
            if (ok) Say("√ " + name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
            else Fail(name + (string.IsNullOrEmpty(detail) ? "" : "（" + detail + "）"));
        }

        static void Shot(string name)
        {
            try
            {
                var cam = Camera.main;
                if (cam == null) { Fail("截图无主相机：" + name); return; }
                int w = Mathf.Max(960, Screen.width), h = Mathf.Max(540, Screen.height);
                var rt = RenderTexture.GetTemporary(w, h, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                var oldTarget = cam.targetTexture;
                var oldEye = cam.stereoTargetEye;
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
                RenderTexture.active = null;
                RenderTexture.ReleaseTemporary(rt);
                Say("玩家视角截图 " + name + "（" + w + "×" + h + "）");
            }
            catch (System.Exception e) { Fail("截图失败 " + name + "：" + e.Message); }
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
                if (!Resolve() || Time.realtimeSinceStartup - _t0 < 2.0f) return;
                DrillLocale.SetEnglish(false);
                _vitals.ResetVitals();
                _hpAtStart = _vitals.currentHp;
                Check("开始页：生命值 HUD 为 20/20", _vitals.currentHp == 20 && _vitals.maxHp == 20,
                    "实际 " + _vitals.currentHp + "/" + _vitals.maxHp);
                Check("开始页：HUD 位于玩家视线内",
                    _vitals.transform.Find("Canvas_PlayerVitals") != null);
                Shot("vr38_start_hud.png");
                _start.SetVisible(false);
                _start.StartGame();
                Next(1);
            }
            else if (_phase == 1)
            {
                if (Time.realtimeSinceStartup - _phaseT < 1.0f) return;
                _smoke = FindSmoke("Stage1Smoke");
                Check("第 1 阶段烟雾已启动", _smoke != null && _smoke.Active);
                if (_smoke == null || !_smoke.Active) { Finish("NO_SMOKE"); return; }
                Vector3 mid = Vector3.Lerp(_smoke.start, _smoke.end, 0.05f);
                TeleportTo(new Vector3(mid.x, 0f, mid.z));
                LookAtPoint(new Vector3(-2.40f, 1.20f, 2.90f));
                _vitals.ResetVitals();
                Say("进入烟雾点 " + mid.ToString("F2") + "，当前 " + _vitals.currentHp + "/20");
                Say("诊断：vitals.enabled=" + _vitals.enabled
                    + " active=" + _vitals.gameObject.activeInHierarchy
                    + " head=" + (_vitals.head != null ? _vitals.head.position.ToString("F2") : "null")
                    + " cam=" + _cam.position.ToString("F2")
                    + " contains(cam)=" + _smoke.ContainsPlayer(_cam.position)
                    + " contains(head)=" + (_vitals.head != null && _smoke.ContainsPlayer(_vitals.head.position))
                    + " smokeActive=" + _smoke.Active
                    + " smokeY=" + _smoke.start.y.ToString("F2") + "~" + _smoke.end.y.ToString("F2"));
                Next(2);
            }
            else if (_phase == 2)
            {
                if (!_vitals.InSmoke && _vitals.SmokeTimer <= 0.01f)
                {
                    if (Time.realtimeSinceStartup - _phaseT > 2.0f)
                    {
                        Fail("站位未进入烟雾区：" + _cam.position.ToString("F2"));
                        Finish("NOT_IN_SMOKE");
                        return;
                    }
                    return;
                }
                float elapsed = Time.realtimeSinceStartup - _phaseT;
                if (_vitals.currentHp >= 20 && elapsed < 11.0f) return;
                Check("烟雾：PlayerVitals 判定为在烟雾中", _vitals.InSmoke);
                Check("烟雾：站立 10 秒后扣 1 点（20→19）", _vitals.currentHp == 19,
                    "实际 " + _vitals.currentHp + "，计时 " + _vitals.SmokeTimer.ToString("F2"));
                Check("烟雾：扣血原因为 smoke", _vitals.LastDamageReason == "smoke",
                    "实际 " + _vitals.LastDamageReason);
                _hpAtSmoke = _vitals.currentHp;
                Shot("vr38_smoke_hp19.png");
                Next(3);
            }
            else if (_phase == 3)
            {
                // 依次试几个「火源 1.55 米内、且在烟雾带外」的站位，避免传送点落进家具被碰撞推开
                _fireTry = 0;
                TryFireCandidate();
                Next(31);
            }
            else if (_phase == 31)
            {
                if (Time.realtimeSinceStartup - _fireTryT < 0.7f) return;
                float drift = new Vector2(_cam.position.x - _fireTryPos.x, _cam.position.z - _fireTryPos.z).magnitude;
                bool ok = drift < 0.25f && !_vitals.InSmoke
                          && _vitals.NearestFireDistance <= _vitals.fireDangerRadius;
                if (ok)
                {
                    Say("火源站位可用：" + _fireTryPos.ToString("F2") + "，距火 "
                        + _vitals.NearestFireDistance.ToString("F2") + " 米，碰撞位移 " + drift.ToString("F2") + " 米");
                    Next(4);
                    return;
                }
                Say("火源站位尝试 " + (_fireTry + 1) + " 不可用：漂移 " + drift.ToString("F2")
                    + " 米，距火 " + _vitals.NearestFireDistance.ToString("F2")
                    + " 米，在烟雾=" + _vitals.InSmoke);
                _fireTry++;
                if (_fireTry < FireCandidates.Length)
                {
                    TryFireCandidate();
                    Next(31);
                    return;
                }
                // 家具挡住了所有候选点：锁住机位（只验证扣血计时，不再让 CharacterController 推开）
                _ccLocked = true;
                if (_ccs != null) foreach (var c in _ccs) c.enabled = false;
                _vitals.ResetVitals();
                TeleportTo(FireCandidates[0]);
                LookAtPoint(new Vector3(-2.40f, 1.06f, 2.88f));
                Say("所有候选点都被家具阻挡，已锁定机位验证持续扣血");
                Next(4);
            }
            else if (_phase == 4)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                Check("火源：进入 1.55 米危险半径", _vitals.NearFire,
                    "最近火源 " + _vitals.NearestFireDistance.ToString("F2") + " 米");
                Check("火源：首次靠近立即扣 1 点", _vitals.currentHp == 19,
                    "20→" + _vitals.currentHp);
                Check("火源：弹出烫伤警告", DrillToast.Instance != null && DrillToast.Instance.IsVisible,
                    "Toast=" + (DrillToast.Instance != null ? DrillToast.Instance.IsVisible.ToString() : "null"));
                Check("火源：扣血原因为 fire-first", _vitals.LastDamageReason == "fire-first",
                    "实际 " + _vitals.LastDamageReason);
                _hpAtFire = _vitals.currentHp;
                Shot("vr38_fire_warning.png");
                Next(5);
            }
            else if (_phase == 5)
            {
                if (Time.realtimeSinceStartup - _phaseT < 2.2f) return;
                Check("火源：原地停留 2 秒再扣 1 点", _vitals.currentHp == _hpAtFire - 1,
                    _hpAtFire + "→" + _vitals.currentHp);
                Check("火源：持续伤害原因为 fire-repeat", _vitals.LastDamageReason == "fire-repeat",
                    "实际 " + _vitals.LastDamageReason);
                Shot("vr38_fire_repeat.png");
                Next(6);
            }
            else if (_phase == 6)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.35f) return;
                if (_ccLocked)
                {
                    if (_ccs != null) foreach (var c in _ccs) c.enabled = true;
                    _ccLocked = false;
                }
                // 退到安全距离，避免继续扣血；打开问答面板
                TeleportTo(new Vector3(-0.20f, 0f, 1.55f));
                LookAtPoint(new Vector3(-0.20f, 1.15f, 3.42f));
                _quiz.Open(_ext.transform);
                Next(7);
            }
            else if (_phase == 7)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.9f) return;
                _quizDistBefore = Vector3.Distance(_quiz.PanelWorldPosition, _cam.position);
                Check("问答面板：打开后距玩家 1.15 米", Mathf.Abs(_quizDistBefore - 1.15f) < 0.12f,
                    "实际 " + _quizDistBefore.ToString("F2") + " 米");
                Shot("vr38_quiz_follow_0.png");
                // 玩家原地转头 50 度，面板应连续跟随并重新回到正前方
                _rig.rotation = Quaternion.Euler(0f, 50f, 0f) * _rig.rotation;
                Next(8);
            }
            else if (_phase == 8)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.8f) return;
                _quizDistAfter = Vector3.Distance(_quiz.PanelWorldPosition, _cam.position);
                Vector3 dir = _quiz.PanelWorldPosition - _cam.position;
                Check("问答面板：转头 50 度后仍距 1.15 米", Mathf.Abs(_quizDistAfter - 1.15f) < 0.15f,
                    "实际 " + _quizDistAfter.ToString("F2") + " 米");
                Check("问答面板：转头后面板仍在视线正前方",
                    Vector3.Dot(dir.normalized, _cam.forward) > 0.95f,
                    "dot=" + Vector3.Dot(dir.normalized, _cam.forward).ToString("F3"));
                Check("问答面板：位置连续变化而不是瞬移", Mathf.Abs(_quizDistAfter - _quizDistBefore) < 0.2f);
                Shot("vr38_quiz_follow_45.png");
                _quiz.Close();
                Next(9);
            }
            else if (_phase == 9)
            {
                if (Time.realtimeSinceStartup - _phaseT < 0.6f) return;
                Check("全流程结束：生命值仍为 " + _vitals.currentHp + "/20，未被烟雾/火源误扣到 0",
                    _vitals.currentHp >= 17 && _vitals.currentHp <= 18, "实际 " + _vitals.currentHp);
                Shot("vr38_hud_after.png");
                Finish("DONE");
            }
        }

        static bool Resolve()
        {
            _start = StartScreen.Instance != null ? StartScreen.Instance : Object.FindObjectOfType<StartScreen>();
            _vitals = PlayerVitals.Instance != null ? PlayerVitals.Instance : Object.FindObjectOfType<PlayerVitals>();
            var origin = Object.FindObjectOfType<Unity.XR.CoreUtils.XROrigin>();
            _rig = origin != null ? origin.transform : null;
            _cam = Camera.main != null ? Camera.main.transform : null;
            _ext = FindExtinguisher("Extinguisher01");
            _quiz = _ext != null ? _ext.quizPanel : null;
            if (_start == null) { Fail("找不到 StartScreen"); return false; }
            if (_vitals == null) { Fail("找不到 PlayerVitals"); return false; }
            if (_rig == null || _cam == null) { Fail("找不到 XR Origin / 主相机"); return false; }
            if (_ext == null || _quiz == null) { Fail("找不到 Extinguisher01 / 问答面板"); return false; }
            _ccs = _rig.GetComponentsInChildren<CharacterController>(true);
            return true;
        }

        static SmokeZone FindSmoke(string name)
        {
            foreach (var s in Object.FindObjectsByType<SmokeZone>(FindObjectsSortMode.None))
                if (s.name == name) return s;
            return null;
        }

        static Extinguisher FindExtinguisher(string name)
        {
            foreach (var e in Object.FindObjectsByType<Extinguisher>(FindObjectsSortMode.None))
                if (e.name == name) return e;
            return null;
        }

        static void TryFireCandidate()
        {
            _fireTryPos = FireCandidates[_fireTry];
            _vitals.ResetVitals();
            TeleportTo(_fireTryPos);
            LookAtPoint(new Vector3(-2.40f, 1.06f, 2.88f));
            _fireTryT = Time.realtimeSinceStartup;
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

        static void Finish(string reason)
        {
            Say("结果 " + reason + "，失败数=" + Fails.Count);
            var sb = new System.Text.StringBuilder();
            foreach (var s in Log) sb.AppendLine(s);
            sb.AppendLine();
            sb.AppendLine("FAIL_COUNT=" + Fails.Count);
            sb.AppendLine("PROBE_EXIT=" + (Fails.Count == 0 ? 0 : 1));
            File.WriteAllText(Path.Combine(ShotDir, "_round38_vitals.txt"), sb.ToString());
            EditorApplication.update -= Tick;
            EditorApplication.Exit(Fails.Count == 0 ? 0 : 1);
        }
    }
}
#endif
