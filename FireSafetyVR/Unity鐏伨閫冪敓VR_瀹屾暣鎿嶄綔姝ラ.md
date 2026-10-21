# 办公室消防知识练习（VR · Unity 6）完整实现方案

基于你截图里的 `FireSafetyOffice` 场景（Unity 6 / Windows-Mac-Linux / DX11 / XR Origin (XR Rig) / `Extinguisher01`、`Extinguisher02`、`BaseFire000`）。

> 目标：手柄单击拿起灭火器（拿起/放下都有音效）→ 双击才放下 → 拔保险销 → 单击展开灭火口 → 对准火焰根部喷射粒子灭火 → 火焰有生命值、喷根部才掉血 → 拾取时弹出「消防步骤排序」限时小测 → 全局 2 分钟限时 → 火焰熄灭即通关 + 开场界面 + 背景音。

---

## 0. 文件清单（8 个脚本 + 必需资源）

### 0.1 本次提供的脚本（放进 `Assets/Scripts/FireSafety/`）

| 文件 | 挂在哪里 | 作用 |
|---|---|---|
| `GameManager.cs` | 空物体 `GameManager` | 总流程、开场界面、HUD 倒计时、通关/失败结算、重开 |
| `ExtinguisherGrab.cs` | 灭火器（如 `Extinguisher01`） | 手柄单击吸附、双击放下、拾取/放下音效 |
| `FireExtinguisher.cs` | 同一个灭火器 | 三步状态机：拔销 → 展开灭火口 → 喷射 |
| `FireTarget.cs` | 火焰 `BaseFire000` | 生命值、根部判定、越喷越小、熄灭 |
| `QuizPanel.cs` | 空物体 `QuizPanel`（或 GameManager 同物体） | 消防步骤排序小测（默认 45 秒限时） |
| `UIBuilder.cs` | 不挂（静态工具类） | 纯代码生成 UI，不需要任何预制体 |
| `Billboard.cs` | 自动挂在生成的 Canvas 上 | 面板始终朝向玩家 |
| `AudioManager.cs` | 空物体 `AudioManager` | 全部音效 + 背景音 |

### 0.2 你没提到但**必须存在**的文件与组件（缺一个就跑不起来）

**① 必装 Package（Window → Package Manager）**

| 包 | 为什么必须有 |
|---|---|
| XR Interaction Toolkit（3.x，Unity 6 适配版） | 手柄射线点击 UI、XR Origin 的手柄输入 |
| XR Plugin Management + OpenXR Plugin | 连接头显（Quest / SteamVR / Vive） |
| Input System（Unity 6 默认已带） | 脚本里用 `InputActionReference` 读 Grip / Trigger |
| TextMeshPro | 所有 UI 文字（首次使用会弹出 Import TMP Essentials，必须点导入） |

**② 输入资产（否则手柄按键读不到）**
- `XRI Default Input Actions`（装 XRI 后自带，路径 `Assets/Samples/XR Interaction Toolkit/x.x.x/Default Input Actions/`）
- 或自建 `InputActionAsset`，至少含：`XRI RightHand/Grip`、`XRI LeftHand/Grip`、`XRI RightHand/Activate(Trigger)`、`XRI LeftHand/Activate`
- **Edit → Project Settings → XR Plug-in Management** 勾选 OpenXR；OpenXR → Interaction Profiles 加对应设备（Oculus Touch / HTC Vive / Valve Index）

**③ UI 输入链路（VR 里点按钮全靠它）**
- 场景里必须有 **EventSystem**：GameObject → XR → **XR UI Input Module**（用 XRI 3.x 的 `XRUIInputModule`，**不要**用 Standalone Input Module）
- 生成的 `Canvas_Main` / `Canvas_Quiz` 上要手动追加组件：**Tracked Device Graphic Raycaster**（XRI 提供），否则手柄射线点不了按钮
- 左右手柄上要有 `XR Ray Interactor`（射线），并勾选允许 UI 交互

**④ 物理与层级**
- 灭火器：`Rigidbody`（脚本会自动补，建议手动加，mass≈4）+ `Box/Sphere Collider`
- 火焰根部 `FireRoot` 空物体（放贴地位置，作为瞄准点）
- 建议新建 Layer：`Fire`、`IgnoreRaycast`（可选）；Project Settings → Physics 里不必改也能跑

**⑤ 美术/资源文件（自备或用免费素材）**
- 保险销模型：一个细长 Cylinder 或自带 pin 模型
- 灭火口：软管（Tube/圆柱）+ 喷嘴（锥体），做成一个父物体 `NozzleGroup`
- 粒子材质：火焰用 Additive 材质（`Universal Render Pipeline/Particles/Unlit` 或 `Particles/Additive`）；喷雾用白色软圆点材质
- 火焰点光源 `Point Light`（橙色），让火有照明反馈

**⑥ 音频文件（至少 9 个 clip，脚本字段已预留）**
`BGM_Office_Fire_Drill`（背景音，loop）、`SFX_Pickup`、`SFX_Drop`、`SFX_PinPull`、`SFX_Transform`、`SFX_Spray_Loop`（循环）、`SFX_Fire_Loop`（火焰循环，挂在火焰上）、`SFX_Extinguish`、`SFX_Win`、`SFX_Fail`、`SFX_UI_Click`
（免费来源：freesound.org / pixabay.com/sound-effects，注意授权）

**⑦ 工程设置**
- File → Build Settings 里 Add Open Scenes（否则 `Restart()` 重载场景失败）
- Player Settings → Active Input Handling 设为 **Input System Package (New)**（或 Both）

---

## 1. 场景搭建（对照你的截图）

### 1.1 布局原则
> 你说"我放置 VR 手柄的地方一侧就是着火的地方"——把火焰放在**手柄初始位置旁边 0.8~1.2 米**，让玩家一转身就能看到，同时灭火器放在稍远一点的墙边/桌边，保证"找灭火器"这一步存在感。

```
FireSafetyOffice (场景根)
├── XR Origin (XR Rig)
│   └── Camera Offset
│       ├── Main Camera
│       ├── Left Controller  （下面有 LeftHand 模型/空物体）
│       └── Right Controller （下面有 RightHand 模型/空物体）
├── Table / Room / Props（原有家具）
├── Extinguisher01        ← 玩家要用的那一个（挂两个脚本）
│   ├── Pin               ← 保险销（初始显示）
│   └── NozzleGroup       ← 灭火口（初始隐藏，SetActive(false)）
│       └── NozzleTip     ← 空物体，蓝色 Z 轴 = 喷射方向
├── Extinguisher02        ← 可保留当作干扰项，不挂脚本
├── BaseFire000           ← 火焰（挂 FireTarget）
│   ├── FireRoot          ← 空物体，贴地，火焰根部
│   ├── FireParticle      ← 火焰粒子
│   └── SmokeParticle     ← 熄灭余烟（初始 Stop）
├── GameManager           ← 空物体
├── QuizPanel             ← 空物体
└── AudioManager          ← 空物体
```

### 1.2 灭火器（Extinguisher01）
1. 选中 `Extinguisher01` → 加 `Rigidbody`（mass 4，Use Gravity 勾上）、`Box Collider`
2. 建子物体 `Pin`（保险销）：细圆柱 + 红色材质，放在灭火器提把处
3. 建子物体 `NozzleGroup`：软管 + 喷嘴，**Inspector 右上角取消勾选**（初始隐藏）
4. 在 `NozzleGroup` 下建 `NozzleTip` 空物体，放到喷嘴出口，**Z 轴（蓝轴）朝外**
5. 建 `SprayParticle`（Particle System，白色/浅灰干粉，锥形喷射），放在 `NozzleTip` 上，取消 Play On Awake，勾 **Stop Action = None**
6. 在灭火器上加 `AudioSource`（放 `SFX_Spray_Loop`，**Loop 勾选**、Play On Awake 不勾），拖给 `FireExtinguisher.sprayAudio`

### 1.3 火焰（BaseFire000）
1. 加 `FireTarget` 脚本
2. `BaseFire000` 下建 `FireRoot` 空物体：放在**火焰最底部贴地**的位置（这就是"根部"）
3. 火焰粒子：`FireParticle`（勾选 Play On Awake；Shape = Cone；Emission rateOverTime ≈ 60）
   > 你截图里"火焰，目前只有运行才能看到"就是粒子系统特性——Scene 视图不预览，运行才会播放，属正常。
4. 余烟：`SmokeParticle`（灰白色，**取消** Play On Awake）
5. 加 `AudioSource` 放 `SFX_Fire_Loop`（Loop、Spatial Blend = 1）
6. 加 `Point Light`（橙色，Range 4），拖给 `fireLight`
7. **关键**：`FireTarget.onExtinguished`（UnityEvent）里点 `+`，拖入 `GameManager`，选 `GameManager → OnFireExtinguished()`。或者留空——`GameManager.OnEnable` 也会自动挂上监听。

### 1.4 手柄锚点
在 `Left Controller` / `Right Controller` 下各建一个空物体 `HandAnchor`（或直接用已有的手柄模型），拖给 `ExtinguisherGrab.leftHand / rightHand`。

---

## 2. Inspector 配置（逐字段）

### 2.1 AudioManager（空物体 `AudioManager`）
挂 `AudioManager`，把 11 个音频 clip 一一拖进去，BGM Volume 建议 0.2~0.3（别盖过提示音）。

### 2.2 GameManager（空物体 `GameManager`）

| 字段 | 值 |
|---|---|
| Total Time | `120`（整场 2 分钟；超时即失败） |
| Ui Canvas | 留空（自动生成 `Canvas_Main`）；若你想自己摆 UI，就拖自己的 World Space Canvas |
| Quiz Panel | 拖场景里的 `QuizPanel` 物体 |
| Fire | 拖 `BaseFire000` |
| Extinguisher | 拖 `Extinguisher01` |
| Reload Scene On Restart | 勾选（重开会重载场景，最干净） |

### 2.3 QuizPanel（空物体 `QuizPanel`）

| 字段 | 值 |
|---|---|
| Ui Canvas | 留空（自动生成 `Canvas_Quiz`） |
| Panel Offset | `(0, 1.35, 0)`（面板出现在灭火器上方 1.35 米） |
| Time Limit | `45`（你说 30 秒或 1 分钟都可以，这里默认 45） |

题目在 `QuizPanel.steps` 数组里，6 条，可随意改文案：
1. 按下火灾警报并大声呼救
2. 判断火势，确认安全出口方向
3. 找到最近的灭火器，站到上风处
4. 与着火点保持 2~3 米安全距离
5. 拔出保险销，喷口对准火焰根部
6. 压下压把左右扫射，确认不复燃

### 2.4 ExtinguisherGrab（灭火器上）

| 字段 | 值 |
|---|---|
| Right Hand / Left Hand | 拖左右手柄的 `HandAnchor` |
| Grip Right | `XRI Default Input Actions → XRI RightHand/Grip`（或 Select） |
| Grip Left | `XRI LeftHand/Grip` |
| Grab Range | `0.35`（米，手柄离多近能吸附） |
| Double Click Window | `0.4`（秒，两下间隔超过它就重新计） |
| Hold Position / Rotation Offset | 调握持手感：位置 `(0, -0.05, 0.10)`，旋转按模型朝向调 |
| Keep Upright On Drop | 勾选（放下时立正不倒） |
| Home Point | 可选：放一个空物体当地点，放下时飞回原位 |
| Keyboard Debug | 勾上，PC 上按 **F** 模拟抓取/双击放下 |

### 2.5 FireExtinguisher（同一个灭火器）

| 字段 | 值 |
|---|---|
| Pin Object | 拖 `Pin` |
| Nozzle Group | 拖 `NozzleGroup` |
| Nozzle Tip | 拖 `NozzleTip` |
| Spray Particle | 拖 `SprayParticle` |
| Spray Audio | 拖灭火器上的 AudioSource |
| Target Fire | 拖 `BaseFire000` |
| Aim Max Distance | `3`（米，超过这个距离喷不到） |
| Aim Max Angle | `35`（度，灭火口朝向与"到根部方向"的夹角容差） |
| Damage Per Second | `40`（100 血 → 约 2.5 秒灭掉；想更久就调小） |
| Require Quiz Passed | **勾选**（不通过小测不能喷） |
| Use Action Right / Left | `XRI RightHand/Activate`（Trigger）、`XRI LeftHand/Activate` |
| Limit Spray Time | 想加"干粉有限"就勾上，设 `Spray Seconds = 12` |
| Keyboard Debug | 勾上，PC 上按 **E** 走三步流程 |

### 2.6 FireTarget（BaseFire000）

| 字段 | 值 |
|---|---|
| Max Health | `100` |
| Root Point | 拖 `FireRoot`（**最关键**，灭火口必须指向它） |
| Root Radius | `0.30`（命中容差，火焰大就调大） |
| Fire Particle / Smoke Particle | 拖对应粒子 |
| Fire Audio | 拖火焰的 AudioSource |
| Fire Light | 拖 Point Light |
| Min Scale | `0`（熄灭后销毁）；想留个焦黑痕迹就填 `0.2` |
| On Extinguished | `+` → GameManager → `OnFireExtinguished()` |

---

## 3. 交互时序（最终跑起来的样子）

```
[开场] Canvas_Main 上显示「办公室消防知识练习」+ 6 条任务说明 +「开始练习」按钮
   ↓ 点击按钮（手柄射线）
[总倒计时 120 秒开始 + BGM 播放] HUD 顶部显示倒计时与提示
   ↓ 走到灭火器旁，按 Grip（单击）
[灭火器瞬间吸附到手柄，播放 pickup 音效] → 自动弹出消防步骤排序页（Canvas_Quiz）
   ↓ 6 个打乱的步骤，45 秒内点击排序 → 提交
   ├ 顺序正确 → 「回答正确」→ 面板 1.2 秒后收起，GameManager.QuizPassed = true
   ├ 顺序错误 → 提示重排，计时继续
   └ 45 秒到   → GameManager.Fail("时间到") → 整局失败
   ↓ （小测通过）
按 Trigger 第 1 次 → Pin 消失 + 拔销音（Stage = PinRemoved）
按 Trigger 第 2 次 → NozzleGroup 出现 + 变形音（Stage = NozzleReady）
对准火焰根部（≤3m、夹角 ≤35°）按 Trigger 第 3 次 → 喷雾粒子 + 循环喷射音
   ↓ 每帧对根部造成 40 DPS；火焰随血量变小、变暗、音量降低
血量归零 → 火焰停止 + 余烟 + 熄灭音 → onExtinguished → GameManager.Win()
   ↓
[结算] 「演练成功 / 用时 XX 秒」+ 通关音 +「重新开始」按钮
```

**放下灭火器**：握着的时候**快速按两下 Grip**（0.4 秒内），单击不会掉——这正是你要的"两次点击才能放下"。

---

## 4. 判定逻辑说明（哪里可以调难度）

| 需求 | 实现位置 | 调参 |
|---|---|---|
| 只能喷根部才掉血 | `FireTarget.IsAimedBy()` | `aimMaxDistance`、`aimMaxAngle`、`rootRadius` |
| 火焰越喷越小 | `FireTarget.ApplyVisual()` | `minScale`、血量阈值 |
| 小测限时 | `QuizPanel.timeLimit` | 30 / 45 / 60 |
| 整局限时 | `GameManager.totalTime` | 120 |
| 喷射时长限制 | `FireExtinguisher.limitSprayTime` | 关闭 = 无限干粉 |
| 握持手感 | `ExtinguisherGrab.holdPositionOffset/RotationOffset` | 边玩边调 |
| 双击放下窗口 | `doubleClickWindow` | 0.4 秒，手抖玩家可调 0.6 |

---

## 5. 无头显调试（很重要）

默认开了 `Keyboard Debug`：
- **F** = 模拟 Grip（按一次拿起，再连按两次放下）
- **E** = 模拟 Trigger（拔销 → 展开灭火口 → 喷射）
- UI 按钮用鼠标点（Canvas 上记得保留 `Graphic Raycaster`，没头显时它生效）

真机测试时把两个 `Keyboard Debug` 取消勾选即可。

---

## 6. 常见坑排查

| 现象 | 原因 / 解决 |
|---|---|
| 手柄按键没反应 | InputActionReference 没拖、或 action 没 Enable。确认拖的是 XRI Default Input Actions 里的条目；脚本 `OnEnable` 会自动 Enable |
| 拿不起来 | 手柄离灭火器 > `grabRange`；或 `rightHand/leftHand` 拖错（拖成了 Controller 根节点而不是手柄模型） |
| 拿了就掉 | 你在 0.4 秒内按了两次 Grip（这是放下的双击）。把 `doubleClickWindow` 调小或按慢一点 |
| 喷不出 | ① 小测没通过（`Require Quiz Passed` 勾着）② 没对准 `FireRoot`（看 Console，会打印"灭火口没有对准火焰根部"）③ `NozzleTip` 的蓝轴方向反了 |
| 火焰不掉血 | `targetFire` 没拖 / `rootPoint` 位置离火焰太远 / `damagePerSecond` 太小 |
| UI 点不动 | Canvas 缺 `Tracked Device Graphic Raycaster`，或 EventSystem 上还是 Standalone Input Module |
| 文字是方块/空白 | TextMeshPro 没 Import TMP Essentials（Window → TextMeshPro → Import TMP Essential Resources） |
| 重启后状态没清 | 确认 Build Settings 里有当前场景；`Reload Scene On Restart` 勾选 |
| 灭火器拿起来姿态歪 | 调 `holdRotationOffset`；或在手柄下建一个空物体摆好姿态，把位置/旋转数值抄进 offset |

---

## 7. 建议的后续扩展（现在没做，但很容易加）

1. **火势蔓延**：给 `FireTarget` 加"不灭就变大 / 引发第二处火点"，配合 120 秒限时更有压迫感
2. **错误操作惩罚**：未拔销就压把 → 提示"先拔保险销"；对着火焰上部喷 → 掉血减半
3. **多火点**：复制 `BaseFire000` 做 2~3 处，用 `List<FireTarget>`，全部熄灭才通关
4. **评分**：记录用时、喷射命中率、小测用时，结算面板显示 S/A/B
5. **保险销物理拔出**：把 Pin 做成 `XRGrabInteractable`，真的用手把它拽出来（比按键更有教学价值）
