#if UNITY_EDITOR
using System;
using System.IO;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using FireSafety;

namespace FireSafety.EditorTools
{
    /// <summary>
    /// 第 39 轮：录一段约 60 秒的玩家视角演示。
    /// 做法：批处理进 Play 模式，按时间轴把主相机摆到玩家站位，逐帧抓 JPEG，
    /// 之后用 ffmpeg（imageio-ffmpeg 自带）合成 MP4。不依赖 OBS / 录屏软件。
    /// </summary>
    public static class ShowcaseRecorder
    {
        const string ScenePath = "Assets/Scenes/FireSafetyOffice.unity";
        const string FrameDir = "Logs/frames";
        const string ShotDir = "Logs/shots";
        const int W = 1600;
        const int H = 900;
        const float Fps = 24f;
        const float Duration = 62f;

        static DrillScenario _scenario;
        static StartScreen _start;
        static Extinguisher _ext03;
        static StageTwoFlow _stageTwo;
        static PlayerVitals _vitals;
        static ExitDoor _exitDoor;
        static EndingScreen _ending;
        static Transform _cam;
        static Transform _handAnchor;
        static RenderTexture _rt;
        static Texture2D _tex;
        static int _frame;
        static float _t0;
        static float _lastCapture = -1f;
        static bool _entered;
        static bool _uiShown, _quizOpened, _pinPulled, _leverPressed, _sprayStarted;
        static bool _spareTaken, _sparePinPulled, _spareLeverPressed, _spareSpraying;
        static bool _exitOpened;
        static int _answered;
        static float _answerT;
        static bool[] _answeredAt = new bool[8];
        static bool _cleaned;
        static string _lastStage = "";
        static float _camFov = 72f;
        static float _tagKillT = -10f;
        static Canvas _cdCanvas;
        static readonly System.Collections.Generic.List<GameObject> _tagRoots = new System.Collections.Generic.List<GameObject>();
        static bool _tagCached;

        [MenuItem("FireSafety/录制玩家视角演示视频")]
        public static void RunFromMenu() => RunAll();

        public static void RunAll()
        {
            if (Directory.Exists(FrameDir)) Directory.Delete(FrameDir, true);
            Directory.CreateDirectory(FrameDir);
            Directory.CreateDirectory(ShotDir);
            _frame = 0; _lastCapture = -1f; _answered = 0; _entered = false;
            _uiShown = _quizOpened = _pinPulled = _leverPressed = _sprayStarted = false;
            _spareTaken = _sparePinPulled = _spareLeverPressed = _spareSpraying = false;
            _exitOpened = false; _lastStage = "";
            EditorSceneManager.OpenScene(ScenePath);
            EditorApplication.update -= Tick;
            EditorApplication.update += Tick;
            EditorApplication.EnterPlaymode();
        }

        static void Say(string s) { Debug.Log("[REC] " + s); }

        static void Tick()
        {
            if (!Application.isPlaying) return;
            if (!_entered)
            {
                _entered = true;
                _t0 = Time.realtimeSinceStartup;
                Say("进入 Play 模式，开始录制");
            }

            float t = Time.realtimeSinceStartup - _t0;
            if (t > Duration + 4f) { Finish("TIMEOUT"); return; }

            if (t < 0.5f) return;
            if (!Resolve()) return;

            Step(t);

            if (t >= 1.6f)
            {
                float due = Mathf.Floor((t - 1.6f) * Fps) / Fps + 1.6f;
                if (due > _lastCapture + 0.0001f && _frame < (int)(Fps * Duration) + 8)
                {
                    _lastCapture = due;
                    Capture();
                }
            }

            if (t >= Duration) Finish("DONE");
        }

        static bool Resolve()
        {
            if (_scenario == null) _scenario = UnityEngine.Object.FindFirstObjectByType<DrillScenario>();
            if (_start == null) _start = UnityEngine.Object.FindFirstObjectByType<StartScreen>();
            if (_stageTwo == null) _stageTwo = UnityEngine.Object.FindFirstObjectByType<StageTwoFlow>();
            if (_vitals == null) _vitals = UnityEngine.Object.FindFirstObjectByType<PlayerVitals>();
            if (_exitDoor == null) _exitDoor = UnityEngine.Object.FindFirstObjectByType<ExitDoor>();
            if (_ending == null) _ending = UnityEngine.Object.FindFirstObjectByType<EndingScreen>();
            if (_ext03 == null)
            {
                var go = GameObject.Find("Extinguisher03");
                if (go != null) _ext03 = go.GetComponent<Extinguisher>();
            }
            var cam = Camera.main;
            if (cam != null)
            {
                if (_cam == null)
                {
                    _cam = cam.transform;
                    _cam.SetParent(null, true);          // 接管相机，模拟第一人称机位
                    cam.fieldOfView = 72f;
                    cam.stereoTargetEye = StereoTargetEyeMask.None;
                    var rig = GameObject.Find("XR Origin") ?? GameObject.Find("XR Rig");
                    if (rig != null) rig.transform.position = new Vector3(0f, -200f, 0f);
                }
            }
            if (_handAnchor == null && _cam != null)
            {
                var go = new GameObject("RecHandAnchor");
                go.transform.SetParent(_cam, false);
                go.transform.localPosition = new Vector3(0.26f, -0.28f, 0.62f);
                go.transform.localRotation = Quaternion.identity;
                _handAnchor = go.transform;
            }
            if (!_cleaned)
            {
                _cleaned = true;
                var cd = UnityEngine.Object.FindFirstObjectByType<DrillCountdown>();
                if (cd != null) _cdCanvas = cd.GetComponentInChildren<Canvas>(true);
                // 灭火器种类大牌与实际状态 HUD 贴脸挡镜头，演示视频里先关掉
                foreach (var tag in UnityEngine.Object.FindObjectsByType<ExtinguisherKindTag>(FindObjectsSortMode.None))
                    tag.gameObject.SetActive(false);
                if (_vitals != null)
                    foreach (var c in _vitals.GetComponentsInChildren<Canvas>(true))
                        if (c.transform != _cam) c.gameObject.SetActive(false);
                Say("已隐藏种类牌与生命值 HUD");
            }
            return _cam != null && _scenario != null && _ext03 != null && _stageTwo != null;
        }

        static void Cam(Vector3 pos, Vector3 look)
        {
            if (_cam == null) return;
            _cam.position = pos;
            _cam.rotation = Quaternion.LookRotation((look - pos).normalized, Vector3.up);
            AimHand(look);
        }

        /// <summary>把手里的喷嘴摆到视线正前并指向目标，保证喷流不偏。</summary>
        static void AimHand(Vector3 look)
        {
            if (_handAnchor == null || _cam == null) return;
            Vector3 dir = (look - _cam.position).normalized;
            _handAnchor.position = _cam.position + dir * 0.30f
                                   + _cam.right * 0.10f + _cam.up * -0.18f;
            // 喷嘴轴与锚点前向一致（补 yaw 会喷不中，实测 79° 偏差只是提示，不影响判定）
            _handAnchor.rotation = Quaternion.LookRotation(dir, Vector3.up);
        }

        /// <summary>镜头沿时间在两机位之间平滑推移。</summary>
        static void CamLerp(Vector3 a, Vector3 b, Vector3 la, Vector3 lb, float k)
        {
            k = Mathf.Clamp01(k);
            k = k * k * (3f - 2f * k);
            Cam(Vector3.Lerp(a, b, k), Vector3.Lerp(la, lb, k));
        }

        /// <summary>灭火器种类大牌是独立根物体（Canvas 名字 KindTag_*），手持时会贴脸挡镜头，录制时统一关掉。</summary>
        static void KillKindTags()
        {
            // 每帧扫一次：标签在开始时可能还没建好，缓存一次会漏掉
            {
                var rootField = typeof(ExtinguisherKindTag).GetField("_root",
                    System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);
                foreach (var tag in UnityEngine.Object.FindObjectsByType<ExtinguisherKindTag>(FindObjectsSortMode.None))
                {
                    tag.enabled = false;
                    var root = rootField != null ? rootField.GetValue(tag) as Transform : null;
                    if (root != null && !_tagRoots.Contains(root.gameObject)) _tagRoots.Add(root.gameObject);
                }
                foreach (var c in UnityEngine.Object.FindObjectsByType<Canvas>(FindObjectsSortMode.None))
                    if (c != null && c.gameObject.name.StartsWith("KindTag") && !_tagRoots.Contains(c.gameObject))
                        _tagRoots.Add(c.gameObject);
            }
            foreach (var go in _tagRoots)
                if (go != null && go.activeSelf) go.SetActive(false);
        }

        static void Step(float t)
        {
            if (t >= 8.6f && _camFov != 72f) { _camFov = 72f; Camera.main.fieldOfView = 72f; }
            KillKindTags();
            if (_cdCanvas != null && _cdCanvas.gameObject.activeSelf != (t >= 8.4f))
                _cdCanvas.gameObject.SetActive(t >= 8.4f);

            // 1.6–8.6 s：开始界面，操作演练按钮下方的教学类型标注
            if (t < 8.6f)
            {
                if (_camFov != 42f) { _camFov = 42f; Camera.main.fieldOfView = 42f; }
                KillKindTags();
                CamLerp(new Vector3(2.95f, 1.58f, 3.05f), new Vector3(2.85f, 1.58f, 3.20f),
                        new Vector3(4.60f, 1.35f, 1.40f), new Vector3(4.55f, 1.35f, 1.55f),
                        (t - 1.6f) / 7.0f);
                if (!_uiShown && t > 2.0f)
                {
                    _uiShown = true;
                    DrillLocale.SetEnglish(false);
                    _start.SetVisible(true);
                    Say("开始界面显示");
                }
                return;
            }

            if (!_uiShown) return;

            // 8.6 s：收起开始界面，进入茶水间情景（情景三）
            if (t < 9.0f)
            {
                _start.SetVisible(false);
                _scenario.StartFormal(-1, 2);
                _vitals.ResetVitals();
                Say("进入茶水间情景");
                return;
            }

            // 9–15 s：茶水间东墙，窗 + 窗下灭火器 + 火源
            if (t < 15.0f)
            {
                float k = (t - 9.0f) / 6.0f;
                CamLerp(new Vector3(4.90f, 1.68f, -1.30f), new Vector3(4.90f, 1.68f, -2.55f),
                        new Vector3(7.10f, 1.05f, -3.10f), new Vector3(6.30f, 0.85f, -3.40f), k);
                _ext03.quizRequired = true;
                return;
            }

            // 15–16 s：伸手拿瓶，答题面板弹出
            if (t < 16.0f)
            {
                Cam(new Vector3(4.90f, 1.68f, -2.55f), new Vector3(6.20f, 0.60f, -3.33f));
                if (!_quizOpened)
                {
                    _quizOpened = true;
                    _ext03.grab.GrabExternal(_handAnchor, HandSide.Right);
                    Say("抓瓶 IsHeld=" + _ext03.grab.IsHeld + " 面板=" + _ext03.quizPanel.IsOpen);
                }
                return;
            }

            // 16–26 s：答题（5 题，每题 2 秒，全部答对）
            if (t < 26.5f)
            {
                Cam(new Vector3(4.90f, 1.68f, -2.55f), new Vector3(6.20f, 0.95f, -3.33f));
                if (_ext03.quizPanel.IsOpen && _answered < 5 && t - _answerT > 2.0f)
                {
                    _answerT = t;
                    int idx = _ext03.quizPanel.questions != null && _answered < _ext03.quizPanel.questions.Count
                        ? _ext03.quizPanel.questions[_answered].correctIndex : 0;
                    _ext03.quizPanel.AnswerOption(idx);
                    _answered++;
                    Say("答题 " + _answered + "/5");
                }
                return;
            }

            // 26.5–28 s：收面板、拔销、压把
            if (t < 28.0f)
            {
                CamLerp(new Vector3(4.90f, 1.68f, -2.55f), new Vector3(4.55f, 1.55f, -1.30f),
                        new Vector3(6.20f, 0.95f, -3.33f), new Vector3(6.30f, 1.00f, -0.74f),
                        (t - 26.5f) / 1.5f);
                if (_ext03.quizPanel.IsOpen) _ext03.quizPanel.Close();
                if (!_pinPulled && t > 26.8f) { _ext03.PullPin(); _pinPulled = true; Say("拔销=" + _ext03.PinPulled); }
                if (!_leverPressed && t > 27.2f && _ext03.handleLever != null)
                {
                    _ext03.handleLever.PressExternal(); _leverPressed = true; Say("压把");
                }
                return;
            }

            // 28–38 s：对准火焰根部喷射，火焰血量下降直至熄灭
            if (t < 38.0f)
            {
                Cam(new Vector3(4.40f, 1.55f, -1.15f), new Vector3(6.30f, 1.00f, -0.74f));
                AimNozzle(_ext03, _scenario.deviceFire);
                if (!_sprayStarted && t > 28.6f)
                {
                    _sprayStarted = _ext03.TryStartSpray();
                    Say("开始喷射=" + _sprayStarted + " 剩余=" + FireLeft());
                }
                if (FireGone() && _lastStage != "pantryDone")
                {
                    _lastStage = "pantryDone";
                    Say("茶水间火已灭（t=" + t.ToString("F1") + "）");
                }
                return;
            }

            // 38–46 s：撤到安全出口，门前复燃火 + 出口旁备用瓶
            if (t < 46.0f)
            {
                float k = (t - 38.0f) / 8.0f;
                CamLerp(new Vector3(4.55f, 1.55f, -1.30f), new Vector3(2.80f, 1.62f, 3.35f),
                        new Vector3(6.30f, 1.00f, -0.74f), new Vector3(4.30f, 0.70f, 2.10f), k);
                if (_stageTwo.stageTwoFire != null && _lastStage != "exitFire")
                {
                    _lastStage = "exitFire";
                    Say("第 3 阶段门前火出现，备用瓶=" + SpareName() + " @ "
                        + (_stageTwo.spareExtinguisher != null ? _stageTwo.spareExtinguisher.transform.position.ToString("F2") : "null"));
                }
                return;
            }

            // 46–48 s：走到备用瓶前拿瓶
            if (t < 48.5f)
            {
                var spare = _stageTwo.spareExtinguisher;
                CamLerp(new Vector3(2.80f, 1.62f, 3.35f), new Vector3(3.45f, 1.52f, 2.35f),
                        new Vector3(4.30f, 0.70f, 2.10f), new Vector3(3.78f, 0.45f, 2.72f),
                        (t - 46.0f) / 2.5f);
                if (!_spareTaken && spare != null && t > 46.9f)
                {
                    _spareTaken = true;
                    spare.quizRequired = false;
                    spare.grab.GrabExternal(_handAnchor, HandSide.Right);
                    spare.PullPin();
                    _sparePinPulled = true;
                    if (spare.handleLever != null) { spare.handleLever.PressExternal(); _spareLeverPressed = true; }
                    Say("取备用瓶=" + spare.name + " IsHeld=" + spare.grab.IsHeld
                        + " 拔销=" + spare.PinPulled);
                }
                return;
            }

            // 48.5–58 s：对着门前火喷射并熄灭
            if (t < 58.0f)
            {
                var spare = _stageTwo.spareExtinguisher;
                Cam(new Vector3(3.25f, 1.55f, 2.55f), new Vector3(4.50f, 0.95f, 1.02f));
                AimNozzle(spare, _stageTwo.stageTwoFire);
                if (!_spareSpraying && spare != null && t > 49.2f)
                {
                    _spareSpraying = spare.TryStartSpray();
                    Say("备用瓶开始喷射=" + _spareSpraying + " 门前火剩余=" + FireLeft());
                }
                if (_stageTwo.stageTwoFire != null && _stageTwo.stageTwoFire.IsExtinguished
                    && _lastStage != "exitFireOut")
                {
                    _lastStage = "exitFireOut";
                    Say("门前火已灭（t=" + t.ToString("F1") + "）");
                }
                return;
            }

            // 58–62 s：开门、结算
            if (t < Duration)
            {
                Cam(new Vector3(2.85f, 1.60f, 3.35f), new Vector3(4.30f, 0.95f, 1.85f));
                if (!_exitOpened && t > 58.4f)
                {
                    _exitOpened = true;
                    if (_exitDoor != null) _exitDoor.TryOpenByPlayer(6f);
                    Say("开门 IsOpen=" + (_exitDoor != null && _exitDoor.IsOpen)
                        + " 结算面板=" + (_ending != null && _ending.IsOpen));
                }
            }
        }

        /// <summary>让喷嘴轴真正指向火焰根部（否则会一直弹「喷流偏了 xx°」）。</summary>
        static void AimNozzle(Extinguisher ext, FireSource fire)
        {
            if (ext == null || fire == null || ext.nozzle == null) return;
            ext.AlignNozzleTo(fire.AimPoint - ext.nozzle.position);
        }

        static float FireLeft()
        {
            var fire = _stageTwo.stageTwoFire;
            if (fire == null) return -1f;
            return fire.progress01 * 100f;
        }

        static bool FireGone()
        {
            var fire = _stageTwo.stageTwoFire;
            return fire != null && fire.IsExtinguished;
        }

        static string SpareName()
        {
            return _stageTwo.spareExtinguisher != null ? _stageTwo.spareExtinguisher.name : "null";
        }

        static void Capture()
        {
            if (_rt == null)
            {
                _rt = new RenderTexture(W, H, 24, RenderTextureFormat.ARGB32, RenderTextureReadWrite.sRGB);
                _rt.Create();
                _tex = new Texture2D(W, H, TextureFormat.RGB24, false);
            }
            var cam = Camera.main;
            if (cam == null) return;
            cam.targetTexture = _rt;
            cam.stereoTargetEye = StereoTargetEyeMask.None;
            cam.Render();
            RenderTexture.active = _rt;
            _tex.ReadPixels(new Rect(0, 0, W, H), 0, 0);
            _tex.Apply();
            RenderTexture.active = null;
            cam.targetTexture = null;
            _frame++;
            File.WriteAllBytes(Path.Combine(FrameDir, "f" + _frame.ToString("D5") + ".jpg"), _tex.EncodeToJPG(92));
        }

        static void Finish(string reason)
        {
            Say("录制结束 " + reason + "，帧数=" + _frame);
            try { if (_rt != null) _rt.Release(); } catch { }
            EditorApplication.update -= Tick;
            EditorApplication.Exit(0);
        }
    }
}
#endif
