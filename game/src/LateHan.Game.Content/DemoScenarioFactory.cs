using LateHan.Game.Domain;

namespace LateHan.Game.Content;

public static class DemoScenarioFactory
{
    public static GameScenario Create()
    {
        var settlements = CreateSettlements();
        return new GameScenario(
            "scenario.189.central_plains",
            "中平六年·京畿动荡",
            "洛阳政局剧变，各地人物开始重新选择道路。历史提供初始约束，此后结果由世界中的行动共同形成。",
            new GameDate(189, 8, 18),
            "settlement.luoyang",
            "luoyang.inn",
            new WorldMap(settlements, CreateRoads()),
            CreateCharacters(),
            CreateBackgrounds(),
            CreateTopics());
    }

    private static IReadOnlyList<Settlement> CreateSettlements() =>
    [
        Settlement("luoyang", "洛阳", SettlementType.Capital, "河南尹", 48, 38,
            "天下都城，宫阙、官署与市井相互挤压。权力消息来得最快，门禁和身份也最森严。",
            ("government", "河南尹署", UrbanLocationType.GovernmentOffice, "处理京畿民政的官署，门前常有等候通报的人。"),
            ("inn", "宣阳里客舍", UrbanLocationType.Inn, "行旅聚集的客舍，消息驳杂，却是外来者最容易落脚之处。"),
            ("market", "金市", UrbanLocationType.Market, "商贾、工匠与游人汇聚，物价会随局势变化。"),
            ("school", "太学", UrbanLocationType.School, "诸生议论政事与经义，名望和师承在这里格外重要。"),
            ("residence", "北部里巷", UrbanLocationType.Residence, "士人和官吏的宅邸散落其间，拜访需要关系或名刺。")),
        Settlement("mengjin", "孟津", SettlementType.Ferry, "河阳县", 46, 19,
            "黄河重要渡口，舟船、军需和过客让此地比寻常县邑更敏感。",
            ("ferry", "孟津渡", UrbanLocationType.Market, "渡船与脚夫都受天气和军令影响。"),
            ("inn", "河畔客舍", UrbanLocationType.Inn, "南来北往之人在这里等候渡河。"),
            ("barracks", "渡口营", UrbanLocationType.Barracks, "守卒盘查往来文书与大宗货物。")),
        Settlement("henei", "河内", SettlementType.CommanderySeat, "河内郡", 37, 10,
            "控扼太行与黄河北岸，田地和人口使其有支撑军队的潜力。",
            ("government", "郡府", UrbanLocationType.GovernmentOffice, "郡中政务和征发由此发出。"),
            ("inn", "山阳客舍", UrbanLocationType.Inn, "商旅谈论北地道路和郡县局势。"),
            ("market", "河内市", UrbanLocationType.Market, "粮食、牲畜和山货在此交换。")),
        Settlement("hulao", "虎牢关", SettlementType.Pass, "河南尹", 63, 37,
            "洛阳东面的险要关隘。平时检查行旅，乱时则能决定军队能否进入京畿。",
            ("gate", "关门", UrbanLocationType.Barracks, "守卒、拒马和查验文书的案几占据道路。"),
            ("inn", "关下逆旅", UrbanLocationType.Inn, "赶不上开关时辰的行旅只能在此投宿。")),
        Settlement("chenggao", "成皋", SettlementType.CountySeat, "河南尹", 72, 38,
            "依山临河的县邑，连接京畿与东方诸郡，军情和商路在这里交错。",
            ("government", "县寺", UrbanLocationType.GovernmentOffice, "县中诉讼、税役与治安在此办理。"),
            ("inn", "汜水客舍", UrbanLocationType.Inn, "沿官道东行的人在此换马歇脚。"),
            ("market", "成皋市", UrbanLocationType.Market, "规模不大，但能补充东行所需。")),
        Settlement("xingyang", "荥阳", SettlementType.CommanderySeat, "河南尹东部", 84, 42,
            "东出洛阳后的交通节点，周边仓储与道路使它在动荡中格外重要。",
            ("government", "荥阳官寺", UrbanLocationType.GovernmentOffice, "地方官吏在此维持征收和驿传。"),
            ("inn", "广武客舍", UrbanLocationType.Inn, "来自陈留、颍川和洛阳的消息在此碰面。"),
            ("market", "荥阳市", UrbanLocationType.Market, "粮价和车马雇价最能显出局势变化。")),
        Settlement("chenliu", "陈留", SettlementType.CommanderySeat, "陈留郡", 103, 48,
            "人口与交通条件优越的东方大郡，地方豪强、官吏和游士都在观察京师变化。",
            ("government", "陈留郡府", UrbanLocationType.GovernmentOffice, "郡吏往来频繁，地方征发的风声从这里传出。"),
            ("inn", "浚仪客舍", UrbanLocationType.Inn, "东来西往的士人与商队在此停留。"),
            ("tavern", "城南酒肆", UrbanLocationType.Tavern, "游侠、役夫和不得志的士人愿意在酒后多说几句。")),
        Settlement("yingchuan", "颍川", SettlementType.CommanderySeat, "颍川郡", 87, 67,
            "士族与学术声望卓著的郡国，人物品评和地方关系网络影响深远。",
            ("government", "颍川郡府", UrbanLocationType.GovernmentOffice, "属吏处理郡中政务，也留意来自京师的诏令。"),
            ("school", "颍川书院", UrbanLocationType.School, "士人讲学清议，年轻人物在此积累声名。"),
            ("inn", "阳翟客舍", UrbanLocationType.Inn, "求学、求仕和避乱者在此交换消息。")),
    ];

    private static IReadOnlyList<Road> CreateRoads() =>
    [
        new("road.luoyang.mengjin", "settlement.luoyang", "settlement.mengjin", 2, "北上孟津的官道"),
        new("road.mengjin.henei", "settlement.mengjin", "settlement.henei", 3, "渡过黄河后通往河内的道路"),
        new("road.luoyang.hulao", "settlement.luoyang", "settlement.hulao", 3, "沿洛水东出的官道"),
        new("road.hulao.chenggao", "settlement.hulao", "settlement.chenggao", 2, "穿过关隘与汜水的山道"),
        new("road.chenggao.xingyang", "settlement.chenggao", "settlement.xingyang", 2, "沿黄河南岸东行的驿路"),
        new("road.xingyang.chenliu", "settlement.xingyang", "settlement.chenliu", 4, "通向陈留平原的官道"),
        new("road.xingyang.yingchuan", "settlement.xingyang", "settlement.yingchuan", 5, "折向东南、连接颍川的郡道"),
        new("road.luoyang.yingchuan", "settlement.luoyang", "settlement.yingchuan", 7, "经轘辕关南下颍川的长路"),
    ];

    private static IReadOnlyList<PlayerBackground> CreateBackgrounds() =>
    [
        new("background.scholar", "寒门士子", "有学问却缺少门第，需要靠游学、交往和时势打开仕途。", "白身士人", 600,
            new Abilities(28, 24, 58, 46, 42, 62), ["谨慎", "好学"]),
        new("background.clerk", "郡府小吏", "熟悉文书与地方运作，收入稳定，但深受官府层级约束。", "郡府属吏", 900,
            new Abilities(34, 28, 48, 62, 46, 50), ["务实", "守序"]),
        new("background.ranger", "游侠", "有武艺和地方人脉，行动自由，却难以直接进入高门与官署。", "游侠", 450,
            new Abilities(42, 64, 38, 24, 55, 30), ["果决", "重诺"]),
    ];

    private static IReadOnlyList<ConversationTopic> CreateTopics() =>
    [
        new("topic.court_upheaval", "京师变局", "洛阳朝局骤变，官员、军队和名门都在重新选择立场。"),
        new("topic.eastern_roads", "东方道路", "虎牢关以东的关津盘查和行旅风险正在发生变化。"),
        new("topic.grain_prices", "粮价波动", "京畿征发与商路不稳正在影响沿途城邑的粮价。"),
        new("topic.local_recruitment", "地方征辟", "部分郡府正在物色能处理文书、治安和军需的人才。"),
    ];

    private static IReadOnlyList<Character> CreateCharacters() =>
    [
        Person("cao_cao", "曹操", "孟德", Gender.Male, CharacterRole.Official | CharacterRole.General, "西园旧部", 72, 68, 82, 70, 76, 74, "luoyang", "residence", "汉廷", "果决", "多疑"),
        Person("yuan_shao", "袁绍", "本初", Gender.Male, CharacterRole.General | CharacterRole.LocalNotable, "名门士人", 78, 64, 70, 62, 88, 72, "luoyang", "residence", "汉廷", "矜持", "重名"),
        Person("wang_yun", "王允", "子师", Gender.Male, CharacterRole.Official, "朝廷官员", 42, 30, 76, 84, 78, 82, "luoyang", "government", "汉廷", "刚直", "深谋"),
        Person("cai_yong", "蔡邕", "伯喈", Gender.Male, CharacterRole.Scholar | CharacterRole.Official, "名士", 22, 18, 68, 64, 76, 96, "luoyang", "school", "汉廷", "博学", "念旧"),
        Person("cai_yan", "蔡琰", "文姬", Gender.Female, CharacterRole.Scholar, "士人", 18, 14, 61, 52, 66, 91, "luoyang", "school", null, "聪敏", "坚韧"),
        Person("lady_bian", "卞氏", "", Gender.Female, CharacterRole.LocalNotable, "曹氏家眷", 24, 18, 70, 72, 82, 64, "luoyang", "residence", "曹氏", "沉着", "节俭"),
        Person("lu_zhi", "卢植", "子干", Gender.Male, CharacterRole.Official | CharacterRole.General | CharacterRole.Scholar, "宿儒", 78, 61, 82, 80, 72, 90, "luoyang", "school", "汉廷", "刚正", "持重"),
        Person("chunyu_qiong", "淳于琼", "仲简", Gender.Male, CharacterRole.General, "军官", 64, 67, 48, 40, 56, 42, "mengjin", "barracks", "汉廷", "勇悍", "好胜"),
        Person("han_hao", "韩浩", "元嗣", Gender.Male, CharacterRole.General | CharacterRole.LocalNotable, "河内豪杰", 70, 65, 62, 68, 58, 54, "henei", "government", null, "严整", "务实"),
        Person("sima_fang", "司马防", "建公", Gender.Male, CharacterRole.Official | CharacterRole.LocalNotable, "郡中名士", 36, 28, 70, 80, 72, 76, "henei", "government", "汉廷", "严肃", "审慎"),
        Person("xu_rong", "徐荣", "", Gender.Male, CharacterRole.General, "边军将领", 78, 71, 68, 52, 44, 46, "hulao", "gate", "凉州军", "沉毅", "守职"),
        Person("li_su", "李肃", "", Gender.Male, CharacterRole.General | CharacterRole.Official, "军中使者", 58, 61, 64, 48, 67, 50, "hulao", "gate", "并州军", "机敏", "趋利"),
        Person("zhang_miao", "张邈", "孟卓", Gender.Male, CharacterRole.Official | CharacterRole.LocalNotable, "陈留名士", 54, 48, 66, 72, 84, 70, "chenliu", "government", "汉廷", "好客", "重义"),
        Person("wei_zi", "卫兹", "子许", Gender.Male, CharacterRole.LocalNotable | CharacterRole.Merchant, "地方豪右", 44, 42, 62, 70, 78, 60, "chenliu", "tavern", null, "慷慨", "有识"),
        Person("zang_hong", "臧洪", "子源", Gender.Male, CharacterRole.Official, "郡府属吏", 66, 58, 74, 73, 78, 69, "chenliu", "government", "汉廷", "慷慨", "刚烈"),
        Person("xun_yu", "荀彧", "文若", Gender.Male, CharacterRole.Scholar | CharacterRole.Official, "颍川士人", 44, 28, 91, 93, 89, 88, "yingchuan", "school", null, "雅正", "远见"),
        Person("xun_you", "荀攸", "公达", Gender.Male, CharacterRole.Scholar | CharacterRole.Official, "颍川士人", 52, 32, 94, 88, 80, 86, "yingchuan", "school", "汉廷", "缜密", "寡言"),
        Person("guo_jia", "郭嘉", "奉孝", Gender.Male, CharacterRole.Scholar, "青年士人", 35, 24, 93, 65, 76, 82, "yingchuan", "inn", null, "洞察", "不羁"),
        Person("zhong_yao", "钟繇", "元常", Gender.Male, CharacterRole.Official | CharacterRole.Scholar, "郡中士人", 38, 30, 83, 88, 82, 90, "yingchuan", "government", "汉廷", "沉着", "勤勉"),
        Person("xi_zhi_cai", "戏志才", "", Gender.Male, CharacterRole.Scholar, "颍川士人", 30, 22, 88, 64, 74, 78, "yingchuan", "inn", null, "敏锐", "体弱"),
        Person("cheng_yu", "程昱", "仲德", Gender.Male, CharacterRole.LocalNotable | CharacterRole.Scholar, "东郡名士", 68, 48, 87, 81, 66, 75, "xingyang", "inn", null, "刚戾", "有断"),
        Person("bao_xin", "鲍信", "允诚", Gender.Male, CharacterRole.Official | CharacterRole.General, "济北官员", 72, 64, 69, 61, 70, 60, "chenggao", "inn", "汉廷", "果决", "忠直"),
        Person("bian_rang", "边让", "文礼", Gender.Male, CharacterRole.Scholar, "名士", 24, 20, 67, 54, 82, 86, "chenliu", "inn", null, "辩捷", "自负"),
        Person("ren_jun", "任峻", "伯达", Gender.Male, CharacterRole.Official | CharacterRole.LocalNotable, "地方吏", 52, 44, 68, 84, 65, 58, "xingyang", "government", "汉廷", "务实", "有干"),
    ];

    private static Settlement Settlement(
        string id,
        string name,
        SettlementType type,
        string region,
        int x,
        int y,
        string description,
        params (string Id, string Name, UrbanLocationType Type, string Description)[] locations) =>
        new($"settlement.{id}", name, type, region, new MapCoordinate(x, y), description,
            locations.Select(item => new UrbanLocation($"{id}.{item.Id}", item.Name, item.Type, item.Description)).ToArray());

    private static Character Person(
        string id,
        string name,
        string courtesyName,
        Gender gender,
        CharacterRole roles,
        string identity,
        int command,
        int martial,
        int strategy,
        int administration,
        int diplomacy,
        int learning,
        string settlement,
        string location,
        string? affiliation,
        params string[] traits) =>
        new($"character.{id}", name, courtesyName, gender, roles, identity,
            new Abilities(command, martial, strategy, administration, diplomacy, learning),
            traits,
            ["在动荡中保全自身所珍视的人与事", "寻找能够施展能力的位置"],
            $"settlement.{settlement}", $"{settlement}.{location}", affiliation);
}
