
namespace League.PrimaryElection
{
    /// <summary>
    /// 英雄位置映射器：基于国服真实玩法（101.qq.com）硬编码位置表
    /// 完全替代原 Tags 判断，更准确反映主流位置
    /// </summary>
    public static class ChampionPositionMapper
    {
        private static readonly Dictionary<string, List<ChampionPosition>> _positionMap;

        static ChampionPositionMapper()
        {
            var map = new Dictionary<string, List<ChampionPosition>>(StringComparer.OrdinalIgnoreCase);

            // 上单英雄（国服主流上单）
            string[] topHeroes = { "腕豪", "熔岩巨兽", "不落魔锋", "无双剑姬", "暗夜猎手", "暗裔剑魔", "铁铠冥魂",
                "炼金术士", "解脱者", "猩红收割者", "虚空先知", "暴怒骑士", "诺克萨斯统领",
                "正义天使", "沙漠死神", "山隐之焰", "亡灵战神", "海洋之灾", "荒漠屠夫",
                "祖安狂人", "狂暴之心", "武器大师", "迅捷斥候", "铁血狼母", "虚空恐惧",
                "祖安怒兽", "不屈之枪", "圣锤之毅", "诺克萨斯之手", "德玛西亚之力",
                "复仇焰魂", "狂战士", "河流之王", "齐天大圣", "符文法师", "无畏战车",
                "牧魂人", "青钢影", "放逐之刃", "灵罗娃娃", "封魔剑魂", "未来守护者",
                "蛮族之王", "炽炎雏龙", "海兽祭司", "疾风剑豪", "机械公敌", "大发明家",
                "刀锋舞者", "惩戒之箭", "不灭狂雷", "纳祖芒荣耀", "德玛西亚之翼",
                "离群之刺", "迷失之牙", "暮光之眼", "酒桶", "龙血武姬", "上古领主", "兽灵行者"};

            // 打野英雄（国服主流打野，已修正上古领主等缺失英雄）
            string[] jungleHeroes = { "含羞蓓蕾", "时间刺客", "狂野女猎手", "蜘蛛女皇", "百裂冥犬", "狂厄蔷薇",
                "法外狂徒", "破败之王", "影流之镰", "无极剑圣", "皎月女神", "痛苦之拥",
                "武器大师", "解脱者", "永恒梦魇", "复仇焰魂", "远古恐惧", "虚空女皇",
                "虚空掠夺者", "死亡颂唱者", "未来守护者", "齐天大圣", "不落魔锋", "暗裔剑魔",
                "德玛西亚皇子", "铁血狼母", "灵罗娃娃", "虚空遁地兽", "熔岩巨兽", "岩雀",
                "荆棘之兴", "堕落天使", "祖安怒兽", "德邦总管", "披甲龙龟", "不屈之枪",
                "盲僧", "恶魔小丑", "刀锋之影", "祖安狂人", "影流之主", "雪原双子",
                "永猎双子", "不灭狂雷", "北地之怒", "战争之影", "殇之木乃伊", "元素女皇",
                "巨魔之王", "生化魔人", "潮汐海灵", "酒桶", "傲之追猎者", "皮城执法官",
                "放逐之刃", "翠神", "龙血武姬", "上古领主", "兽灵行者" };

            // 中单英雄
            string[] midHeroes = { "虚空先知", "远古巫灵", "百裂冥犬", "愁云使者", "铸星龙王", "皎月女神",
                "解脱者", "复仇焰魂", "不屈之枪", "奥术先驱", "异画师", "光辉女郎",
                "九尾妖狐", "猩红收割者", "潮汐海灵", "冰晶凤凰", "刀锋之影", "堕落天使",
                "正义巨像", "冰霜女巫", "诺克萨斯统领", "双界灵兔", "诡术妖姬", "疾风剑豪",
                "熔岩巨兽", "不祥之刃", "时间刺客", "影哨", "虚空行者", "暮光星灵",
                "影流之主", "流光镜影", "邪恶小法师", "卡牌大师", "离群之刺", "虚空恐惧",
                "炽炎雏龙", "封魔剑魂", "德玛西亚之力", "暗黑元首", "未来守护者",
                "黑暗之女", "沙漠死神", "虚空之眼", "亡灵战神", "蛮族之王", "元素女皇",
                "符文法师", "爆破鬼才", "刀锋舞者", "发条魔灵", "法外狂徒", "魔蛇之拥",
                "岩雀", "沙漠皇帝", "麦林炮手" };

            // ADC（射手）
            string[] adcHeroes = { "赏金猎人", "战争女神", "残月之肃", "炽炎雏龙", "不羁之悦", "暗夜猎手",
                "沙漠玫瑰", "戏命师", "逆羽", "瘟疫之源", "暴走萝莉", "虚空之女",
                "祖安花火", "圣枪游侠", "皮城女警", "深渊巨口", "荣耀行刑官", "探险家",
                "寒冰射手", "爆破鬼才", "麦林炮手", "不破之誓", "惩戒之箭", "复仇之矛",
                "英勇投弹手" };

            // 辅助
            string[] supportHeroes = { "复仇焰魂", "诺克萨斯统领", "远古巫灵", "堕落天使", "魂锁典狱长",
                "河流之王", "蜘蛛女皇", "明烛", "唤潮鲛姬", "荆棘之兴", "魔法猫咪",
                "众星之子", "迅捷斥候", "光辉女郎", "虚空之眼", "星籁歌姬", "恶魔小丑",
                "时光守护者", "不屈之枪", "弗雷尔卓德之心", "深海泰坦", "风暴之怒",
                "曙光女神", "蒸汽机器人", "异画师", "仙灵女巫", "德玛西亚皇子",
                "瓦洛兰之盾", "天启者", "镕铁少女", "解脱者", "诡术妖姬", "远古恐惧",
                "炼金男爵", "幻翎", "流光镜影", "万花通灵", "血港鬼影", "牛头酋长",
                "琴瑟仙女", "扭曲树精", "熔岩巨兽", "涤魂圣枪", "圣锤之毅", "腕豪",
                "星界游神", "盲僧", "邪恶小法师", "赏金猎人", "殇之木乃伊", "寒冰射手",
                "瘟疫之源" };

            // 批量添加位置
            AddPositions(topHeroes, ChampionPosition.Top);
            AddPositions(jungleHeroes, ChampionPosition.Jungle);
            AddPositions(midHeroes, ChampionPosition.Mid);
            AddPositions(adcHeroes, ChampionPosition.ADC);
            AddPositions(supportHeroes, ChampionPosition.Support);

            _positionMap = map;

            // 局部方法：向映射表添加位置
            void AddPositions(string[] heroes, ChampionPosition pos)
            {
                foreach (var hero in heroes)
                {
                    if (!map.ContainsKey(hero))
                        map[hero] = new List<ChampionPosition>();

                    if (!map[hero].Contains(pos))
                        map[hero].Add(pos);
                }
            }
        }

        /// <summary>
        /// 根据英雄中文名获取主流位置列表
        /// </summary>
        public static List<ChampionPosition> GetPositionsByChampionName(string championName)
        {
            if (_positionMap.TryGetValue(championName, out var positions))
            {
                return positions;
            }
            return new List<ChampionPosition>();
        }
    }
}