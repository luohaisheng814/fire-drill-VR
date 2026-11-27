using System.Collections.Generic;

namespace FireSafety
{
    /// <summary>
    /// 第 42 轮：事前教育题库池（按情景分三套，每套 10 题：公共 7 + 本情景 3）。
    /// 第 47 轮：实际上课时由 ForRole() 组 5 题 —— 报警装置 / 出口与集合点 / 灭火器类型与用法 / 本情景知识点 共 4 道通用题，
    /// 再按身份追加 1 道角色题（普通员工：无初期处置权限；初期应对负责人：四项条件全满足才可尝试）。
    /// 情景顺序与 DrillScenario 一致：0 = 电气设备起火，1 = 办公桌纸张起火，2 = 茶水间小家电起火。
    /// </summary>
    public static class PreTrainingBanks
    {
        public static int Count => 3;

        public static string Name(int index)
        {
            switch (index)
            {
                case 0: return DrillLocale.T("电气设备起火", "Electrical equipment fire", "전기 설비 화재");
                case 1: return DrillLocale.T("办公桌纸张起火", "Desk paper fire", "책상 종이 화재");
                case 2: return DrillLocale.T("茶水间小家电起火", "Pantry appliance fire", "탕비실 소형 가전 화재");
                default: return DrillLocale.T("通用", "General", "일반");
            }
        }

        static PreTrainingQuiz.Question Q(string cn, string en, string ko,
                                          string[] oCn, string[] oEn, string[] oKo, int correct,
                                          string eCn, string eEn, string eKo)
        {
            return new PreTrainingQuiz.Question
            {
                qCn = cn, qEn = en, qKo = ko,
                optionsCn = oCn, optionsEn = oEn, optionsKo = oKo,
                correctIndex = correct,
                expCn = eCn, expEn = eEn, expKo = eKo
            };
        }

        public static List<PreTrainingQuiz.Question> For(int index)
        {
            var list = new List<PreTrainingQuiz.Question>();
            list.AddRange(Common());
            switch (index)
            {
                case 0: list.AddRange(Electrical()); break;
                case 1: list.AddRange(Paper()); break;
                case 2: list.AddRange(Pantry()); break;
                default: list.AddRange(Electrical()); break;
            }
            return list;
        }

        // ---------------- 第 47 轮：按身份组卷（每套 5 题） ----------------
        // 结构：报警装置（公共）+ 出口与集合点（公共）+ 灭火器类型与用法（公共）+ 本情景知识点 + 角色专属题。
        // 普通员工与初期应对负责人只差最后一题，其余 4 题完全相同 → 「有一点区分，但通用灭火常识必须都在」。

        static List<PreTrainingQuiz.Question> Specific(int index)
        {
            var list = new List<PreTrainingQuiz.Question>();
            switch (index)
            {
                case 0: list.AddRange(Electrical()); break;
                case 1: list.AddRange(Paper()); break;
                case 2: list.AddRange(Pantry()); break;
                default: list.AddRange(Electrical()); break;
            }
            return list;
        }

        /// <summary>按身份取 5 题：4 道通用/情景 + 1 道角色题。</summary>
        public static List<PreTrainingQuiz.Question> ForRole(int index, DrillFlowDirector.DrillRole role)
        {
            var common = new List<PreTrainingQuiz.Question>(Common());
            var spec = Specific(index);
            var list = new List<PreTrainingQuiz.Question>
            {
                common[1],                                  // ② 报警装置
                common[2],                                  // ③ 出口与集合点
                common[3],                                  // ④ 灭火器类型与用法
                spec.Count > 0 ? spec[0] : common[4],        // 本情景知识点
                role == DrillFlowDirector.DrillRole.ResponseLead ? RoleLead() : RoleEmployee()
            };
            return list;
        }

        /// <summary>角色题（普通员工）：没有初期处置权限，条件再好也疏散。</summary>
        static PreTrainingQuiz.Question RoleEmployee()
        {
            return Q(
                "本次身份是普通员工。发现初期小火、灭火器就在 2 米内，正确做法是？",
                "Your role is general employee. You find a small incipient fire with an extinguisher 2 m away — what is correct?",
                "이번 역할은 일반 직원입니다. 초기 소규모 화재, 소화기가 2 m 앞에 있을 때 올바른 행동은?",
                new[] { "先尝试灭火，灭不掉再走", "按现场消防计划报警并优先疏散，不尝试灭火", "先回工位拿上个人物品再走" },
                new[] { "Try to put it out first, leave only if it fails", "Raise the alarm and evacuate first per the site plan — do not attempt an attack", "Go back to your desk for personal items first" },
                new[] { "먼저 소화를 시도하고 안 되면 대피", "현장 소방 계획에 따라 경보 후 대피 우선, 소화 시도 금지", "먼저 자리로 돌아가 개인 물건을 챙긴다" },
                1,
                "普通员工没有初期处置权限：即使火势小、器材近、视野清楚，也按计划报警并疏散。只有受过训练并被指定为初期应对负责人、且四项条件全部满足时才可尝试。",
                "General employees have no initial-attack authority: even with a small fire and equipment at hand, alarm and evacuate per plan. Only trained designated responders may attempt, and only with all four conditions met.",
                "일반 직원은 초기 대응 권한이 없습니다. 화재가 작고 장비가 가까워도 계획대로 경보·대피합니다. 교육받은 지정 초기 대응 책임자만 네 조건 충족 시 시도할 수 있습니다.");
        }

        /// <summary>角色题（初期应对负责人）：四条件全满足才可尝试，任一不确定即疏散。</summary>
        static PreTrainingQuiz.Question RoleLead()
        {
            return Q(
                "本次身份是初期应对负责人。可以尝试初期灭火的条件是？",
                "Your role is initial response lead. When may you attempt an initial attack?",
                "이번 역할은 초기 대응 담당자입니다. 초기 소화를 시도할 수 있는 조건은?",
                new[] { "只要看见火苗就可以扑上去", "火势初期 + 器材合适 + 视野清楚 + 退路安全", "等火势变大、烟变浓以后再判断" },
                new[] { "Charge at any flame you see", "Incipient fire, suitable extinguisher, clear view and a safe escape route behind — all four met", "Wait until the fire grows and smoke thickens, then decide" },
                new[] { "불꽃이 보이면 바로 달려든다", "초기 화재, 적합한 소화기, 시야 확보, 뒤쪽 도주로 안전 — 네 가지 모두 충족", "불세가 커지고 연기가 짙어질 때까지 기다렸다 판단한다" },
                1,
                "初期应对负责人也只在四项条件全部满足时尝试；危险增加（烟气累积 / 超时 / 火势不再减弱）或灭火失败，立即停止并疏散；灭火成功后也要转移到安全地点。",
                "Even the lead attempts only when all four conditions hold. If danger rises (smoke build-up, time-out, no progress) or the attack fails, stop and evacuate; after a successful attack, still move to safety.",
                "초기 대응 담당자도 네 조건이 모두 충족될 때만 시도합니다. 위험 증가(연기 누적·시간 초과·진전 없음) 또는 실패 시 즉시 중단·대피하며, 성공 후에도 안전한 곳으로 이동합니다.");
        }

        // ---------------- 公共 7 题 ----------------

        static IEnumerable<PreTrainingQuiz.Question> Common()
        {
            yield return Q(
                "普通办公室人员在火灾中的首要职责是什么？",
                "What is the first duty of an ordinary office worker in a fire?",
                "일반 사무직 직원의 화재 시 첫 번째 임무는?",
                new[] { "必须先独自把火扑灭", "先报警、提醒周围人，然后按现场消防计划优先疏散", "先回工位保存文件" },
                new[] { "Put out the fire alone first", "Raise the alarm, warn others, then evacuate first as the site plan requires", "Go back to your desk and save files" },
                new[] { "혼자서 먼저 불을 끈다", "경보·주변 알림 후 현장 소방 계획에 따라 대피를 우선한다", "자리로 돌아가 파일을 저장한다" },
                1,
                "职责分工：普通人员以报警、提示他人、按计划疏散为主，不承担灭火任务；只有受过训练、被指定为初期应对负责人、且条件全部满足时才尝试初期灭火。",
                "Ordinary staff alarm, warn others and evacuate; only trained designated responders attempt initial attack, and only when every condition is met.",
                "일반 인원은 경보·알림·대피가 임무이며, 교육받은 지정 초기 대응 책임자만 모든 조건 충족 시 초기 소화를 시도합니다.");

            yield return Q(
                "手动火灾报警按钮的作用是什么？按下之后还要做什么？",
                "What does the manual call point do, and what comes next?",
                "수동 화재 발신기의 역할과 그 다음 할 일은?",
                new[] { "只是装饰，按下也没用", "拉响全楼警报提醒所有人撤离，同时拨打 119 报告火情", "按下后整栋楼会自动灭火" },
                new[] { "Decoration only, it does nothing", "It sounds the building alarm so everyone evacuates, and you also dial 119", "The building extinguishes the fire automatically" },
                new[] { "장식일 뿐 아무 효과 없다", "건물 전체 경보를 울려 모두 대피하게 하고 동시에 119에 신고한다", "누르면 건물이 자동으로 소화한다" },
                1,
                "报警装置：按下手动报警按钮 → 全楼声光警报启动；同时用手机/内线拨 119，报地址、起火物、火势、被困人员。报警不延误疏散，边走边报也可以。",
                "Press the call point to sound the building alarm, and dial 119 with address, material, size and trapped people. Reporting must never delay evacuation.",
                "발신기를 눌러 전관 경보를 울리고 119에 주소·연소물·규모·갇힌 인원을 신고합니다. 신고 때문에 대피가 늦어지면 안 됩니다.");

            yield return Q(
                "演练里的「安全出口」和「集合地点」由什么决定？",
                "What determines the safe exit and the assembly point?",
                "안전 출구와 집결지는 무엇으로 정해지는가?",
                new[] { "自己临时挑一个最近的门", "由现场消防计划（疏散图）事先指定，按绿色出口标志走", "跟着人最多的地方走" },
                new[] { "Pick whichever door looks closest", "They are designated in advance by the site fire plan and evacuation map, follow the green exit signs", "Follow the biggest crowd" },
                new[] { "가장 가까운 문을 즉석에서 고른다", "현장 소방 계획(대피도)에 사전 지정된 곳이며 녹색 비상구 표지를 따른다", "사람이 가장 많은 쪽을 따른다" },
                1,
                "出口与集合点：每栋建筑都有疏散图和指定的集合地点，平时要记住最近的两个安全出口；撤离时走绿色出口标志、走楼梯不乘电梯。实际集合流程依现场计划执行。",
                "Every building has an evacuation map and a designated assembly point. Know your two nearest exits; follow green signs, use stairs, never lifts. The actual assembly procedure follows the site plan.",
                "건물마다 대피도와 지정 집결지가 있습니다. 가장 가까운 비상구 두 곳을 평소에 익히고, 녹색 표지를 따라 계단으로 이동합니다(엘리베이터 금지).");

            yield return Q(
                "干粉灭火器的标准操作四步是？喷射时对准哪里？",
                "What are the four steps of a dry-powder extinguisher, and where do you aim?",
                "분말 소화기 4단계와 분사 조준 위치는?",
                new[] { "提 · 拔 · 握 · 压；对准火焰根部左右扫射", "摇 · 晃 · 倒 · 喷；对准火苗顶端", "拔 · 扔 · 跑 · 喊；对准烟雾" },
                new[] { "Lift · Pull · Aim · Squeeze; sweep at the base of the flames", "Shake · Rock · Tip · Spray; aim at the flame tips", "Pull · Throw · Run · Shout; aim at the smoke" },
                new[] { "들기 · 핀 뽑기 · 겨냥 · 압착, 불꽃根部를 좌우로 훑는다", "흔들기 · 기울기 · 분사, 불꽃 끝을 겨냥한다", "뽑기 · 던지기 · 도주, 연기를 겨냥한다" },
                0,
                "灭火器用法：提（提到上风口、离火 2~3 米）→ 拔（拔保险销）→ 握（握喷管对准火焰根部）→ 压（压下压把左右扫射）。站上风口、留好背后退路，火灭后观察 1 分钟防复燃。",
                "Lift (carry upwind, 2-3 m away), Pull the pin, Aim at the base, Squeeze and sweep. Stand upwind, keep an escape route behind you, and watch for re-ignition for a minute.",
                "들기(상풍, 2~3 m) → 핀 뽑기 → 根部 겨냥 → 압착·좌우 훑기. 상풍에 서고 뒤쪽 도주로를 확보하며, 진화 후 1분간 재점화를 확인합니다.");

            yield return Q(
                "什么条件下才可以尝试初期灭火？拿不准的时候怎么办？",
                "When may you attempt an initial attack, and what if you are unsure?",
                "초기 소화를 시도할 수 있는 조건과 판단이 어려울 때의 행동은?",
                new[] { "火势处于初期、有对路灭火器、视野清楚且身后有安全撤离路线，三者同时满足；拿不准就疏散", "只要手边有灭火器就上", "火越大越要冲进去灭" },
                new[] { "Incipient fire, the right extinguisher, clear visibility and a safe escape route — all at once; if unsure, evacuate", "Any extinguisher in hand means go", "The bigger the fire the more you should charge in" },
                new[] { "초기 화재·맞는 소화기·시야 확보·안전한 도주로가 모두 충족될 때만, 판단이 어려우면 대피", "소화기만 있으면 무조건 뛰어든다", "불이 클수록 뛰어들어 끈다" },
                0,
                "初期处置四条件：小规模初期火灾、适当器材、充分视野、安全撤离路线。任一不满足或无法判断 → 立即疏散。不灭火、直接沿安全路线撤离同样是正确应对（本演练记为成功）。",
                "Four conditions: incipient fire, right equipment, clear visibility, safe escape route. If any is missing or unclear, evacuate. Skipping the attack and evacuating safely counts as a correct response (success in this drill).",
                "네 조건: 초기 규모·적합한 장비·충분한 시야·안전한 도주로. 하나라도 미충족이거나 판단이 어려우면 즉시 대피합니다. 소화하지 않고 안전하게 대피하는 것도 올바른 대응(성공)입니다.");

            yield return Q(
                "浓烟中逃生，身体姿态和移动方式应该是？",
                "How should you move through heavy smoke?",
                "짙은 연기 속 이동 자세와 방법은?",
                new[] { "直立快跑，越快越好", "低姿弯腰、贴地移动，用湿毛巾捂口鼻，走楼梯不乘电梯", "乘电梯快速下楼" },
                new[] { "Run upright, faster is better", "Stay low and close to the floor, cover nose and mouth with a wet cloth, use the stairs not the lift", "Take the lift down quickly" },
                new[] { "똑바로 빨리 달린다", "낮게 몸을 숙여 바닥에 가깝게 이동하고 젖은 수건으로 코·입을 막으며 계단을 이용한다", "엘리베이터로 빠르게 내려간다" },
                1,
                "烟气向上聚集，贴地 30~60 cm 空气相对清洁；湿毛巾可过滤部分颗粒；火灾时电梯可能断电或成为烟囱，必须走楼梯。",
                "Smoke rises, so the air is cleaner within 30-60 cm of the floor. A wet cloth filters particles, and lifts may fail or act as chimneys — always use the stairs.",
                "연기는 위로 모이므로 바닥 30~60 cm가 상대적으로 깨끗합니다. 젖은 수건으로 여과하고, 엘리베이터는 정전·연통 위험이 있어 계단을 이용합니다.");

            yield return Q(
                "到达集合地点之后应该做什么？能不能回去拿东西？",
                "What do you do at the assembly point, and may you go back inside?",
                "집결지 도착 후 할 일과 건물로 돌아가도 되는지는?",
                new[] { "各自回家", "清点本部门人数、向现场负责人报告未撤离人员，留在安全区；绝不擅自返楼", "回楼里取回手机和钱包" },
                new[] { "Go home individually", "Count your team, report anyone unaccounted for to the person in charge, stay clear; never re-enter", "Go back for your phone and wallet" },
                new[] { "각자 집으로 간다", "부서 인원을 점검하고 미대피자를 책임자에게 보고하며 안전 구역에서 대기, 절대 재입장 금지", "휴대폰과 지갑을 가지러 들어간다" },
                1,
                "集合点三件事：清点人数、报告失联人员（姓名/位置/最后见到的地方）、等待消防到场。如尚未报警，在安全地点拨打 119；不擅自返回建筑。本演练的结束状态就是「到达集合点并报告」。",
                "At the assembly point: count people, report the missing (name, location, where last seen), wait for the fire service. If no one has called yet, dial 119 from safety. Never re-enter. Reaching and reporting at the assembly point is the drill's end state.",
                "집결지에서 인원 점검, 미확인자 보고(이름·위치·마지막 목격 지점), 소방대 대기. 아직 신고 전이라면 안전한 곳에서 119 신고. 재입장 금지. 본 훈련의 종료 상태는 '집결지 도착·보고'입니다.");
        }

        // ---------------- 情景一：电气 ----------------

        static IEnumerable<PreTrainingQuiz.Question> Electrical()
        {
            yield return Q(
                "配电柜/电脑起火，电源还没切断时能不能用水或水基灭火器？",
                "The cabinet or PC is burning and the power is still on — may you use water or a water-based extinguisher?",
                "배전반·PC 화재, 전원이 살아 있을 때 물이나 수계 소화기를 쓸 수 있는가?",
                new[] { "可以，水量不大没关系", "不可以，导电会触电，必须先断电", "可以，戴绝缘手套就行" },
                new[] { "Yes, a small amount of water is fine", "No — it conducts and can shock you, cut the power first", "Yes, if you wear insulating gloves" },
                new[] { "된다, 물이 조금이면 괜찮다", "안 된다, 감전 위험이 있으니 반드시 전원을 먼저 차단한다", "된다, 절연 장갑만 끼면 된다" },
                1,
                "电气火灾第一步是断电（拉总闸/拔插头/关配电柜开关），未断电前严禁用水或水基灭火器。断电后再用 CO₂ 或 ABC 干粉，喷嘴与带电体保持 0.5 米以上。",
                "Cut the power first (main breaker / unplug / cabinet switch). Never use water before de-energizing. Then use CO₂ or ABC powder, keeping 0.5 m clearance.",
                "전기 화재는 전원 차단이 먼저(차단기·플러그·배전반 스위치). 차단 전 물·수계 소화기는 금지. 이후 CO₂나 ABC 분말을 0.5 m 이상 거리에서 사용합니다.");

            yield return Q(
                "电气设备着火，应该选哪种灭火器？",
                "Which extinguisher suits an electrical fire?",
                "전기 설비 화재에 맞는 소화기는?",
                new[] { "水基（A 类）灭火器", "二氧化碳（CO₂）或 ABC 干粉灭火器", "泡沫灭火器" },
                new[] { "Water-based (Class A)", "CO₂ or ABC dry powder", "Foam" },
                new[] { "수계(A급) 소화기", "이산화탄소(CO₂) 또는 ABC 분말 소화기", "포말 소화기" },
                1,
                "电气火选不导电的 CO₂ 或 ABC 干粉；CO₂ 不留残渣、适合精密设备，干粉性价比高但会污染设备。泡沫和水基都导电，禁用。",
                "Use non-conductive CO₂ or ABC powder. CO₂ leaves no residue and suits electronics; powder is cheaper but messy. Foam and water-based media conduct — do not use.",
                "전기는 불전도성 CO₂ 또는 ABC 분말을 사용합니다. CO₂는 잔여물이 없어 정밀 기기에 적합하고, 분말은 가성비가 좋지만 오염이 남습니다. 포말·수계는 사용 금지.");

            yield return Q(
                "电气火灾扑灭后，最需要注意什么？",
                "After the electrical fire is out, what matters most?",
                "전기 화재 진화 후 가장 주의할 점은?",
                new[] { "立刻恢复送电继续办公", "保持断电、观察防止复燃，确认设备冷却后再由电工检查送电", "直接用手摸设备确认温度" },
                new[] { "Restore power immediately and keep working", "Keep the power off, watch for re-ignition, and only an electrician should re-energize after inspection", "Touch the equipment to check its temperature" },
                new[] { "즉시 전원을 복구하고 업무를 계속한다", "전원을 유지한 채 재점화를 감시하고, 냉각 후 전기 기사 점검 뒤에만 송전한다", "손으로 장비를 만져 온도를 확인한다" },
                1,
                "电气火扑灭后仍可能阴燃或复燃：保持断电、观察至少 1 分钟，不要徒手触摸发热设备；恢复送电必须由电工检查后进行。",
                "Electrical fires can smoulder or re-ignite: keep power off, watch for at least a minute, never touch hot equipment bare-handed, and let an electrician re-energize.",
                "전기 화재는 잔불·재점화 위험이 있습니다. 전원을 유지한 채 1분 이상 관찰하고, 뜨거운 장비를 맨손으로 만지지 않으며, 송전은 전기 기사 점검 후에 합니다.");
        }

        // ---------------- 情景二：纸张 ----------------

        static IEnumerable<PreTrainingQuiz.Question> Paper()
        {
            yield return Q(
                "纸张、文件、纸箱着火属于哪一类火灾？",
                "Which fire class covers paper, documents and cartons?",
                "종이·서류·종이 상자는 어느 급 화재인가?",
                new[] { "A 类（固体可燃物）", "B 类（易燃液体）", "D 类（金属）" },
                new[] { "Class A (solid combustibles)", "Class B (flammable liquids)", "Class D (metals)" },
                new[] { "A급(고체 가연물)", "B급(가연성 액체)", "D급(금속)" },
                0,
                "纸张属于 A 类固体可燃物火灾，适用水基灭火器或 ABC 干粉；B 类是油类液体，D 类是金属，用药完全不同。",
                "Paper is a Class A solid fire: use water-based or ABC powder. Class B is flammable liquids, Class D metals — different agents entirely.",
                "종이는 A급 고체 가연물로 수계 또는 ABC 분말이 적합합니다. B급은 유류, D급은 금속으로 소화약제가 다릅니다.");

            yield return Q(
                "桌上纸张起火，火苗已经引燃旁边一摞文件，先做什么？",
                "Papers on the desk are alight and the flame reached a stack of files — what first?",
                "책상 종이가 타고 옆 서류 더미로 옮겨붙었을 때 먼저 할 일은?",
                new[] { "先泼一杯水试试", "先报警并提醒同事，再判断能否初期处置；能就用灭火器对准火焰根部", "先把文件抱到走廊" },
                new[] { "Try a cup of water first", "Alarm and warn colleagues, then judge whether an initial attack is possible; if so aim at the base of the flames", "Carry the files out to the corridor" },
                new[] { "먼저 물 한 컵을 부어 본다", "경보·동료 알림 후 초기 대응 가능 여부를 판단하고, 가능하면 소화기로 불꽃根部를 겨냥한다", "서류를 안고 복도로 나른다" },
                1,
                "纸张火蔓延快但初期可控：先报警、再判断（火势是否仍是一小堆、灭火器是否在手边、身后是否有退路），满足才喷射；抱着燃烧的文件移动会把火带到别处。",
                "Paper fires spread fast but stay controllable early: alarm first, then judge (still a small pile, extinguisher at hand, escape route behind). Carrying burning files spreads the fire.",
                "종이 화재는 초기에 통제 가능하지만 번짐이 빠릅니다. 먼저 경보 후 (작은 더미·소화기 근접·도주로) 판단하여 분사하고, 타는 서류를 안고 이동하면 불이 옮겨붙습니다.");

            yield return Q(
                "纸张堆火看似灭了，为什么还要把纸堆扒开检查？",
                "The paper stack looks out — why spread it and check again?",
                "종이 더미가 꺼진 것처럼 보여도 왜 헤쳐서 확인해야 하는가?",
                new[] { "为了收拾干净", "纸堆内部会阴燃，不扒开检查容易复燃", "为了让纸快点干" },
                new[] { "To tidy up", "Paper stacks smoulder inside; without spreading it the fire re-ignites", "To dry the paper faster" },
                new[] { "정리하려고", "더미 내부에서 잔불이 은밀히 타므로 헤쳐 확인하지 않으면 재점화한다", "종이를 빨리 말리려고" },
                1,
                "纸张是典型阴燃材料：表面火焰扑灭后内部仍可能 400 ℃ 以上无焰燃烧。喷完后用工具扒开纸堆、浇水降温，观察 1 分钟无烟无火再离开。",
                "Paper smoulders: after the visible flame is out the interior can still burn above 400 °C without flame. Spread it, wet it, and watch a full minute before leaving.",
                "종이는 대표적 잔불 재료로 표면 진화 후에도 내부가 400 °C 이상 무염 연소할 수 있습니다. 헤쳐 물을 뿌려 식히고 1분간 연기·불꽃이 없음을 확인합니다.");
        }

        // ---------------- 情景三：茶水间 ----------------

        static IEnumerable<PreTrainingQuiz.Question> Pantry()
        {
            yield return Q(
                "茶水间油锅起火属于哪一类火灾？该用什么灭火剂？",
                "Which fire class is a burning oil pot, and which agent?",
                "탕비실 기름솥 화재의 급수와 소화약제는?",
                new[] { "A 类，用水基灭火器", "F 类（食用油），用湿化学灭火器或盖锅盖窒息", "B 类，用泡沫灭火器" },
                new[] { "Class A, water-based", "Class F (cooking oil), wet-chemical extinguisher or smother with a lid", "Class B, foam" },
                new[] { "A급, 수계 소화기", "F급(식용유), 습식 화학 소화기 또는 뚜껑으로 질식", "B급, 포말 소화기" },
                1,
                "食用油火灾属 F 类（部分标准并入 K 类）：必须用湿化学（皂化）灭火器，或关火后用锅盖/灭火毯窒息。水、泡沫、普通干粉都可能造成油火飞溅或复燃。",
                "Cooking-oil fires are Class F (K in some standards): use a wet-chemical extinguisher, or cut the heat and smother with a lid or fire blanket. Water, foam and ordinary powder can splash or allow re-ignition.",
                "식용유 화재는 F급(K급)으로 습식 화학 소화기 또는 불을 끄고 뚜껑·소화포로 질식 소화합니다. 물·포말·일반 분말은 튐·재점화 위험이 있습니다.");

            yield return Q(
                "油锅已经起火，第一步做什么？",
                "The oil pot is already alight — first step?",
                "기름솥에 이미 불이 붙었을 때 첫 단계는?",
                new[] { "先泼水降温", "先关掉燃气/电炉热源，再盖锅盖或灭火毯窒息", "端起锅冲到室外" },
                new[] { "Pour water to cool it", "Turn off the gas/heater first, then smother with a lid or fire blanket", "Carry the pot outside" },
                new[] { "먼저 물을 부어 식힌다", "가스·전기 열원을 먼저 끄고 뚜껑이나 소화포로 덮어 질식시킨다", "솥을 들고 밖으로 뛴다" },
                1,
                "油火三步：关热源（断根）→ 覆盖窒息（锅盖/灭火毯，沿锅边推盖）→ 冷却观察。端锅移动会让热油泼出、引燃沿途可燃物并烧伤自己。",
                "Three steps: cut the heat, smother (push the lid on from the side), then cool and watch. Carrying the pot splashes hot oil, ignites the path and burns you.",
                "세 단계: 열원 차단 → 뚜껑·소화포로 측면에서 덮어 질식 → 냉각·관찰. 솥을 들면 뜨거운 기름이 튀어 주변에 옮겨붙고 화상을 입습니다.");

            yield return Q(
                "茶水间的小家电（电水壶/微波炉）起火，和油锅起火处置有什么不同？",
                "How does a pantry appliance fire (kettle/microwave) differ from an oil-pot fire?",
                "탕비실 소형 가전(전기포트·전자레인지) 화재는 기름솥 화재와 어떻게 다른가?",
                new[] { "完全一样，都泼水", "小家电先拔插头/断电，再用 CO₂ 或干粉；油锅关火源后盖盖窒息", "小家电直接盖湿毛巾" },
                new[] { "Identical, pour water on both", "Unplug/de-energize the appliance then use CO₂ or powder; for oil cut the heat and smother", "Cover the appliance with a wet towel" },
                new[] { "완전히 같다, 둘 다 물을 붓는다", "소형 가전은 플러그를 뽑아 전원을 차단한 뒤 CO₂·분말, 기름솥은 열원을 끄고 덮어 질식", "소형 가전에 젖은 수건을 덮는다" },
                1,
                "两类火要分开判断：小家电属电气火（断电 + CO₂/干粉），油锅属 F 类（关火 + 窒息/湿化学）。湿毛巾盖带电设备既不能断氧又有触电风险。",
                "Separate judgements: appliances are electrical (de-energize + CO₂/powder), oil pots are Class F (cut heat + smother/wet chemical). A wet towel on live equipment neither cuts oxygen nor is safe.",
                "구분해서 판단합니다: 소형 가전은 전기 화재(전원 차단 + CO₂/분말), 기름솥은 F급(열원 차단 + 질식/습식 화학). 젖은 수건은 산소 차단도 감전 방지도 되지 않습니다.");
        }
    }
}
