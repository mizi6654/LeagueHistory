using System.Diagnostics;

namespace League.PrimaryElection
{
    // Champion.cs - 英雄基本信息模型
    public class PrimaryChampion
    {
        public int Id { get; set; }
        public string Alias { get; set; } = string.Empty;   // 英文名，如 Vayne
        public string Name { get; set; } = string.Empty;     // 中文名，如 暗夜猎手
        public string Title { get; set; } = string.Empty;    // 标题
        public string Description { get; set; } = string.Empty;
        public List<string> Tags { get; set; } = new List<string>();
        public bool IsActive { get; set; } = true;

        // 唯一获取位置的方法：通过映射表
        public List<ChampionPosition> GetPositions()
        {
            return ChampionPositionMapper.GetPositionsByChampionName(Name);
        }

        // 从原有Champion转换的构造函数
        public PrimaryChampion() { }
    }

    public enum ChampionPosition
    {
        Top = 1,      // 上单
        Mid = 2,      // 中单
        Jungle = 3,   // 打野
        ADC = 4,      // 射手
        Support = 5,  // 辅助
        All = 99      // 全部
    }
}
