namespace League.Models
{
    /// <summary>
    /// 海克斯符文类
    /// </summary>
    public class AugmentInfo
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string IconPath { get; set; }

        public string Rarity { get; set; } // 新增稀有度属性
    }
}
