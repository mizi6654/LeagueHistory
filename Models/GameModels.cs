namespace League.Models
{
    internal class GameModels
    {
    }

    //英雄
    public class Champion
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
        public string Alias { get; set; }
    }

    //装备
    public class Item
    {
        public int Id { get; set; }
        public string Name { get; set; }
        public string IconFileName { get; set; }
        public string Description { get; set; }
        public Image Icon { get; set; }
    }

    //召唤师技能
    public class SummonerSpell
    {
        public long Id { get; set; }
        public string Name { get; set; }
        public string IconFileName { get; set; }
        public string Description { get; set; }
        public string IconPath { get; set; }
    }

    //玩家头像
    public class ProfileIcon
    {
        public int Id { get; set; }
        public string Name { get; set; }         // 可选：例如显示名“默认头像123”
        public string Description { get; set; }  // 可选：你可以填一些说明
    }

    //符文
    public class RuneInfo
    {
        public int id { get; set; }
        public string name { get; set; }
        public string longDesc { get; set; }
        public string shortDesc { get; set; }
        public string iconPath { get; set; }

        public int RuneId { get; set; }
        public Image Icon { get; set; }
        public string Name { get; set; }
        public string Description { get; set; }
    }

}
