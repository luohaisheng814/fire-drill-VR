# fire-drill-VR

办公室消防知识演练 VR 项目（Unity）

## 项目简介

基于 Unity（6000.6.0f1）+ XR Interaction Toolkit（3.6.1）开发的办公室消防知识 VR 演练项目。
玩家在办公室场景中完成「警报响起 → 寻找灭火器 → 常识考核 → 拔销 → 压把 → 喷射灭火 → 沿箭头撤离到大厅」的完整流程，
结束后弹出成绩单。同一份工程支持 **VR 双手柄** 与 **桌面鼠标键盘** 两条完整输入链。

## 玩法流程

1. **开始界面**：世界空间面板锚定在右手手柄正前方 1.0 米、上抬 0.15 米；三个入口：`开始演练` / `操作教学`（分页引导）/ `操作演练`（原第一阶段的完整实操，只练流程不计成绩）。
2. **操作教学（引导页）**：4 页（演练目标 / 桌面操作 / VR 手柄操作 / 安全考核规则），末页按钮变为「开始演练」。
3. **操作演练（第 23 轮起）**：在办公室中央点燃一团地板火，跑完整流程（5 题问答 → 抓取 → 拔销 → 压把 → 喷射灭火），结束后弹「操作演练完成 / 进入正式演练」，不计入成绩。
4. **操作演练（原第一阶段改造）**：见上，目标是让玩家先弄懂整条流程。
5. **正式第 1 阶段（三套情景，第 24 轮起）**：点「开始演练」后按所选情景随机落到该情景的 3 个出生点之一，**终点统一是安全出口门**，只有初始点不同。
   - **情景 1 · 电气设备起火（配电柜）**：设备柜顶起火（`DeviceFire`）+ 常驻烟雾，旁边墙上挂电源总闸，就近用 `Extinguisher01`（C 组·电气火灾题库 2 号）；
   - **情景 2 · 办公桌纸张起火**：南侧工位桌面纸堆起火（`PaperFire` @ 1.42 / -3.45）+ `Stage1Smoke_Paper` 薄烟，**无需断电**，就近用 `Extinguisher02`（E 组·A 类固体题库 4 号）；
   - **情景 3 · 茶水间小家电起火**：茶水台台面小家电起火（`PantryFire` @ 6.30 / -0.74）+ `Stage1Smoke_Pantry` 油烟，**无需断电**，就近用 `Extinguisher03`（F 组·油类/小家电题库 5 号）；
   - 开始界面第二排三个按钮直接选情景；拿灭火器答对 5 题后拔销；需要断电的情景在**未断电前喷射无效**（火焰锁定，HUD 提示「火焰仍带电，喷射无效」）；
   - 右键单击 / 扳机射线点电源总闸 → 闸柄扳下、指示灯转绿 → 火焰解锁 → 喷射灭火；火焰上方常驻 **血量条**（HP 100/100 → 0）。
6. **第 2 阶段（撤离 + 出口火情）**：灭火后烟雾停止、地面出现「安全出口 →」标识与引导箭头；走到大厅检查点后出口门前复燃（D 组疏散题库），拿出口旁的灭火器 → 5 题 → 拔销 → 压把 → 喷射；火灭后门解锁。
   **第 25 轮起**：纸张 / 茶水间两套情景按「就近原则」**跳过第 2 阶段**，第 1 阶段火一灭就直接在安全出口门前升起第三阶段的火（`skipEvacuation`），只有电气情景保留完整两段式撤离。
7. **走出门外才算通关（第 25 轮）**：门解锁后右键 / 扳机点门 → 门扇转开；必须真的跨出门洞 0.9 米（`ExitDoor.beyondDepth`）才判定撤离成功，站在门内不算。
8. **门外开阔场地**：安全出口门外是三面矮墙围合的 6.5 × 8 米广场（集合圈 + 集合点立牌 + 庭院灯 + 花箱），走廊东端墙拆出一个 3.8 米宽开口通向广场。
9. **结束页**：成功或失败都会弹出成绩单，显示总用时/剩余时间、答对题数、火源处理数、完成步骤，可「重新演练」或「关闭」。


## 第 24 轮：封住天空盒缺口与三情景演练

### 补墙（`FireSafetySetup.SetupWalls()`）

实测场景围合后发现地板是 L 形（`z∈[-4,4]` 全宽 `x∈[-6.75,7.25]`；`z∈[-8,-4]` 只有西翼 `x∈[-6.75,-0.75]`），四处边界直接漏到天空盒。新建父物体 `OfficeWalls`，用 4 段 3 米高 Cube（含 `BoxCollider`，材质 `OfficeWallMat` 浅米色，踢脚线 `OfficeWallSkirtingMat`）封住：

| 墙段 | 位置 | 尺寸 | 封住的缺口 |
|---|---|---|---|
| `Wall_SouthWing` | (-3.75, 1.50, -8.10) | 6.00 × 3.00 × 0.20 | 西翼南端 z=-8 |
| `Wall_WingEast` | (-0.85, 1.50, -6.00) | 0.20 × 3.00 × 4.00 | x=-0.75 的 z∈[-8,-4] |
| `Wall_LobbySouth` | (3.15, 1.50, -4.10) | 8.20 × 3.00 × 0.20 | 大厅南边界 z=-4 |
| `Wall_CorridorEast` | (7.15, 1.50, 0.00) | 0.20 × 3.00 × 8.00 | 东端 x=7.25 |

### 灭火器等距布点

三台灭火器由原先的 3.2 m / 6.9 m 不均匀间距改为沿「大厅 → 中部房间 → 茶水台」铺开，固定题库组各自独立：

| 瓶 | 位置 | 题库组 |
|---|---|---|
| `Extinguisher01` | (-3.60, 0.16, 0.60) | C（2）· 电气 |
| `Extinguisher02` | (1.60, 0.16, -1.60) | E（4）· A 类固体（纸张） |
| `Extinguisher03` | (6.00, 0.16, -3.20) | F（5）· 油类/小家电 |

实测间距 `01↔02 = 5.65 m`、`02↔03 = 4.68 m`（相邻两段比值 ≤ 1.4，自检断言）。第 2 阶段仍按原设计把该瓶题库切到 D 组（疏散），并在出口旁自动补一台备用瓶（`autoMoveSpareExtinguisher` + `spareExtinguisherSpot = (3.70, 0, 1.60)`）。

### 多情景骨架（`DrillScenario.FireScenario`）

```
scenarios[3] = { 电气（DeviceFire / 需断电 / 题库 2 / 出生组 1）
                 纸张（PaperFire  / 无需断电 / 题库 4 / 出生组 2）
                 茶水间（PantryFire / 无需断电 / 题库 5 / 出生组 3） }
```

- `StartFormal(forceSpawn, forceScenario)`：先 `ApplyScenario()` 收掉其它情景的火与烟、切该瓶题库、设置是否需要断电，再按 `DrillSpawnPoints/SpawnGroupNN` 随机取出生点，用 `Quaternion.LookRotation(lookAt - pos)` 让玩家一出生就正对火源；
- 出生点每组 3 个候选（`Spawn01..03`），**终点统一为 `ExitDoor`**，第 2 阶段流程与之前完全一致；
- `BeginTutorial()` 会先把全部情景火收掉、切回 A 组题库，保证「操作演练 / 操作教学」不受情景残留影响。

### 题库扩到 30 题（六组）

`ExtinguisherQuizPanel` 由 20 题四组扩到 **30 题六组**：A 通用 / B 厨房 / C 电气 / D 疏散 / **E 纸张·A 类固体** / **F 小家电·油类**，`questionSetIndex` 的 `[Range(0,5)]` 同步放宽，`StageTwoFlow.spareQuizSetIndex` 同样放宽。

### 玩家视角验收（第 24 轮）

真 Play + 离屏渲染逐张判读（不是编辑模式断言）：三套情景出生/起火/答题/喷射中段共 9 张、补墙 4 张、主流程 20 张，全部 `FAIL_COUNT=0`。其中两处**玩家视角才暴露的真实缺陷**已修：

1. 情景二起火点原在打印机包围盒内部，玩家只看得到一张浮在机器上方的血量条 → 移到打印机右侧空桌面 (1.42, 1.06, -3.45)，烟雾终点同步；
2. 情景三取瓶区（东端 x≈6、z≈-3.2）无灯，画面近乎全黑 → 属已知照明缺口，取瓶路径上靠火光照明确认可见（记录待优化）。

## 第 25 轮：可打开的安全出口门 + 就近原则跳阶段 + 门外广场 + 灭火器贴墙

### 5.1 安全出口门「可打开」，并且必须真正走出门外

用户截图里那扇浅棕木门（`door_b_variant` @ 5.25 / 1.02，门框 `wall_standard_door_frame (1)`）由「装饰门」改成**可交互的终点门**：

| 新增（`ExitDoor.cs`） | 作用 |
|---|---|
| `requireBeyond` / `beyondDepth = 0.9` / `beyondLateral = 1.6` | 通关判据从「靠近门 0.9 米」改为「越过门洞 0.9 米、横向不超出 1.6 米」 |
| `IsOpen` / `TryOpenByPlayer(maxDistance)` / `IsPassable` | 门扇可被玩家转开；`IsPassable = 已解锁 && 门扇已开` |
| `IsDoorPart(Collider)` | 射线命中判定：桌面右键、VR 扳机射线都能认出「这是那扇门」 |
| `DetectPlayerEnter()` 用本地 `forward` 求 `along` | 门内 `along = 0`、门外为负；`-along ≥ 0.9` 才置 `PlayerEntered` 并触发 `onEntered` |

交互：

- **桌面**：右键点门（6 米内）→ 未解锁提示「这扇安全出口还锁着：先把门前的火灭掉」；已解锁则门扇缓缓转开并提示「直接走出这扇门即算撤离成功」；
- **VR**：未持灭火器时右手射线扳机指向门扇，同样两档提示；
- 探针实测：站在门内侧 0.9 米时 `PlayerEntered=false`（`vr6b_door_threshold.png`），走到门外 1.35 米才结算（`vr6c_outside_plaza.png`）。

### 5.2 纸张 / 茶水间第 1 阶段灭火后跳过第 2 阶段（就近原则）

- `StageTwoFlow` 新增 `skipEvacuation`；`Begin()` 里若为真，则 `arrows.BeginExitGuidance()`（箭头直指安全出口门）→ 提示「本情景就近撤离：直接前往安全出口门，门前已复燃」→ 立即 `EnterStageThree()`，**不再要求走进大厅**；
- `DrillScenario.FireScenario.skipEvacuation` 随情景下发，`ApplyScenario()` 同步到流程脚本；
- 情景二（纸张）/ 情景三（茶水间）= `true`，情景一（电气）保持 `false`（电气仍需断电 → 撤离 → 出口复燃的完整两段式）。

### 5.3 门外改为「开阔场所」（`FireSafetySetup.SetupOutdoorPlaza()`）

原 `Wall_CorridorEast`（x=7.25、z∈[-4,4] 整面封死）把门外封成 2 × 3.8 米死角。现在：

- 东端墙只封茶水间东侧 `z∈[-4.05,0.15]`（`(7.15, 1.50, -1.95)`, `0.20 × 3.00 × 4.10`），走廊东端 `z 0.1~3.9` 留出开口；
- 门外新建父物体 `OutdoorPlaza`：`PlazaGround`（6.50 × 8.00，顶面 y=0，x 7.25~13.75 / z -4~4）、三面 1.1 米矮墙 `PlazaFence_South/North/East`（西侧敞开接走廊开口）、黄色集合圈 `PlazaAssembleRing`、绿色集合点立牌 `PlazaAssembleSign`、2 盏庭院灯（`PointLight` intensity 1.6 / range 7.0）、2 个花箱。

### 5.4 灭火器一律贴墙 / 挂墙（不占走道）

| 瓶 | 贴墙位置 | 墙面 | 到本情景起火点 | 题库组 |
|---|---|---|---|---|
| `Extinguisher01` | (0.85, 0.16, 3.45) | 北墙 | 3.31 m（配电柜） | C（2）· 电气 |
| `Extinguisher02` | (3.45, 0.16, -3.55) | 大厅南墙 | 2.04 m（纸张火） | E（4）· A 类固体 |
| `Extinguisher03` | (5.95, 0.16, -3.80) | 茶水间南墙 | 3.08 m（小家电火） | F（5）· 油类/小家电 |

每台后面新增一块墙面挂钩板（父物体 `ExtinguisherBrackets`，`<Name>_Bracket` 背板 + `<Name>_Bracket_Hook` 挂钩横杆，深红 `ExtinguisherBracketMat`，带轻微自发光 emission 0.26/0.02/0.02，昏暗的南墙与茶水间也能一眼看到挂板位置），看起来就是「挂在墙上」；三处机位取证见 `vr17_ext_mount_01.png` / `vr18_ext_mount_02.png` / `vr19_ext_mount_03.png`。第三阶段备用瓶落点也从门前的过道挪到贴墙位 `(4.95, 0, 3.20)`。

自检断言随之改写：三台各自 `Physics.Raycast` 四方向 0.6 米内必须有立面（贴墙）、到本情景起火点 ≤3.6 米（就近）、两两间距 ≥2.0 米（不重叠）。


## 主要功能

- **手柄锚定 UI**：开始界面（前 1.0 m / 上 0.15 m）、倒计时（上 0.42 m / 前 0.12 m）、引导页（前 1.0 m / 上 0.14 m）都跟随右手手柄并朝向玩家（`StartScreen.cs` / `DrillCountdown.cs` / `OnboardingScreen.cs`）
- **引导页与结束页**：分页教学 + 成功/失败成绩单（`OnboardingScreen.cs` / `EndingScreen.cs`）
- **9 步顺序状态机**：①身处火场 → ②警报 → ③确认火点 → ④移动到灭火器 → ⑤拿起 → ⑥拔销 → ⑦压把 → ⑧喷射 → ⑨扑灭，`enforceOrder` 禁止跳步（`FireDrillManager.cs`）
- **限时问答门控**：5 题 × 10 秒、60% 通过才允许拔销与喷射，答错给知识点讲解（`ExtinguisherQuizPanel.cs`）
- **粒子累积灭火**：喷射锥内逐帧累积，达阈值 2.5 秒立即清空火焰粒子、灯光归零并上报 ⑨（`Extinguisher.cs` / `FireSource.cs`）
- **两阶段地面箭头**：警报后指向灭火器；阶段一完成后指向大厅，到达 0.6 m 触发结束页（`GuidanceArrows.cs`）
- **桌面鼠标完整操作链**：右键单击灭火器=答题/拿起、左键单击=拔销、左键双击=压把、右键双击=喷射/停止、P=放下、回车/空格=开始、问答 1/2/3（`DesktopDrillInput.cs`）
- **背景音乐与音效**：办公环境音 → 火灾警笛交叉淡入，拾取/拔销/喷射/答对/答错/通关等全套音效（`BackgroundMusic.cs`）
- **抓取手型匹配**：抓取时手部姿态与物体外形匹配（`GrabPoseMatcher.cs`）
- **天花板与吸顶灯**：14×12 米顶板（内表面 3.00 米）+ 4 盏吸顶灯，纯视觉件不带碰撞体，避免挡光（`FireSafetySetup.SetupCeiling()`）
- **断电门控**：`PowerSwitch.cs`（闸柄扳下 / 指示灯转绿 / 标签更新 / 4 米内射线或鼠标点击触发）；`FireSource.extinguishLocked` 让带电火焰吞掉所有喷射进度
- **火焰血量条**：`FireHealthBar.cs`（世界空间画布，读 `progress01`，billboard + 熄灭淡出）
- **流程总控**：`DrillScenario.cs`（`Tutorial / Formal` 两种模式、随机出生点、开始即烟雾、断电解锁、灭火转阶段、`ReleaseHeldExtinguishers()` 切换时放下手持瓶）
- **结束页成绩单**：`EndingScreen.cs`（世界画布朝向玩家正面，显示用时/剩余/答对/火源/步骤）

## VR 头显接入（第 21 轮）

真机头显接上后无需改场景，直接按下面的输入映射操作；无头显时同一套逻辑走「虚拟驱动」通道，可批处理自测。

| 操作 | 桌面（键鼠） | VR（手柄） |
|---|---|---|
| 移动 | W/A/S/D | 左摇杆推杆（`moveSpeed 2.2 m/s`，蹲姿 1.05 m/s） |
| 转向 | 鼠标移动 | 右摇杆瞬转（`snapAngle 45°`，冷却 0.35 s） |
| 抓取灭火器 | 右键单击 | Grip（XRI Select） |
| 拔销 / 压把 | 左键单击 / 双击 | Trigger 第一下拔销、第二下压把并保持 |
| 停止喷射 | 右键双击 | 松开 Trigger |
| 开始 / 翻页 / 继续 | 回车 / 空格 | A 键 |
| 答题选项 | 数字 1 / 2 / 3 | B / X 键切换，A 确认 |
| 蹲下 / 站起 | Ctrl | 左摇杆按下 |

- **OpenXR 运行时**：`Assets/Editor/VROpenXRConfig.cs` 装配 OpenXRLoader 与 11 项手柄 profile（Oculus Touch / Meta Quest Touch Plus / Vive / Index / 微软 MR 等），批处理入口 `FireSafety.EditorTools.VROpenXRConfig.RunFromBatch`，校验入口 `...VROpenXRConfig.Verify`
- **输入层**：`Assets/Scripts/FireSafety/VRDrillInput.cs`（按键上升沿 + `_triggerConsumed` 锁，避免一按到底连拔销带压把）
- **位移与瞬转**：`XRDrillLocomotion.cs`（CharacterController 位移/重力/蹲姿，`ResolveReferences()` 自动补组件）
- **世界空间 HUD**：`VRDrillHud.cs`（视线下方提示条，模态面板打开时自动让位）+ `HudPlacement.cs`（把面板夹进视野的纯函数）
- **相机层级**：`FireSafetySetup.FindOrCreateRig()` 只认 `XR Origin (Drill)` 直接子物体下的 Camera Offset，并自建 Main Camera，避免移动逻辑挪的是另一套 rig


## 目录结构

```
Assets/
├── Scenes/FireSafetyOffice.unity   # 主场景（一键搭建脚本已装配好全部 VR 设施）
├── Scripts/FireSafety/             # 玩法脚本（开始界面/引导页/结束页/倒计时/箭头/问答/灭火/输入）
├── Editor/FireSafetySetup.cs       # 一键搭建（菜单 FireSafety/一键搭建并保存场景）
├── Editor/FireSafetySelfTest.cs    # 自动化自检（400+ 项断言，菜单 FireSafety/自检）
├── Editor/VROpenXRConfig.cs        # OpenXR 运行时与手柄 profile 装配/校验
├── Editor/VRPlayProbe.cs           # 玩家视角验收探针（播放态离屏渲染 15+ 张截图 + 全流程断言）
└── Resources/Sounds/               # 背景音乐与音效（BGM/ SFX/）

Docs/
├── 01-先行研究调查-既有消防训练程序.md   # 既有消防训练程序（VR/商用/韩国现状）调查
└── 02-本项目差异化与独创性.md           # 与既有程序的逐维对比、独创性主张与边界
```

## 一键搭建与自检

场景已通过 `FireSafety/一键搭建并保存场景` 菜单（或批处理 `-executeMethod FireSafety.EditorTools.FireSafetySetup.RunSetupFromMenu`）完成装配，包括：

- XR Origin（Drill）+ 左右手柄控制器 + XR 交互管理器
- 用户移动（CharacterController + XRDrillLocomotion）
- StartScreen 开始界面（锚定右手手柄正前方；文案可在 StartScreen 组件的 Instructions 字段修改）
- OnboardingScreen 引导页（4 页教学，锚定右手手柄）
- FireAlarm 火灾警报（警笛 + 红色爆闪灯）
- BackgroundMusic 背景音乐切换
- DrillCountdown 倒计时（锚定右手手柄正上方）
- GuidanceArrows 地面引导箭头（灭火器 → 大厅两阶段）
- StageOneCompletePanel 阶段一完成面板
- EndingScreen 结束页（到大厅=成功 / 倒计时耗尽=失败）
- 天花板 + 4 盏吸顶灯（纯视觉，内表面 3.00 米）
- OfficeWalls 四面补墙（4 段 3 米高墙板 + 踢脚线，带 BoxCollider）
- DrillScenario 流程总控 + 三套情景（起火点 / 灭火器 / 题库 / 出生点）
- 出生点 3 组 × 每组 3 个候选（`DrillSpawnPoints/SpawnGroup01..03`）
- DeviceCabinet / DeviceFire / DeviceSmoke / PowerBox（情景一：电气设备起火 + 电源总闸）
- PaperFire / PaperSmoke / Stage1Smoke_Paper（情景二：办公桌纸张起火）
- PantryFire / PantrySmoke / Stage1Smoke_Pantry（情景三：茶水间小家电起火）
- Stage1Smoke 设备旁烟雾（开始即冒烟）与 SmokeZone 撤离通道雾
- FireHealthBar 火焰血量条（设备火 / 出口火各一条）
- Extinguisher01 / Extinguisher02 / Extinguisher03（抓取/问答/拔销/压把/粒子喷射/手型匹配）
- EventSystem + XRUIInputModule（手柄射线点按 UI）
- DesktopDrillInput（桌面鼠标完整操作链）

自检（批处理）：`-executeMethod FireSafety.EditorTools.FireSafetySelfTest.RunAll`，成功判据为日志出现 `[TEST] ==== 全部通过 ====`、`error CS` 为 0、退出码为 0。

## 先行研究与差异化

- 既有消防训练程序调查（含 Go Fire、FLAIM、LION BullsEye、ADMS/XVR、国内 VR 仿真等 13 个来源）见
  [`summary/Docs/01-先行研究调查-既有消防训练程序.md`](summary/Docs/01-先行研究调查-既有消防训练程序.md)
- 本项目差异化与独创性论证（含对比表、六项可验证差异、主张边界与待补实验）见
  [`summary/Docs/02-本项目差异化与独创性.md`](summary/Docs/02-本项目差异化与独创性.md)
