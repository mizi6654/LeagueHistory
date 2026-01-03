using League.Managers;
using League.Models;
using Newtonsoft.Json.Linq;
using System.Diagnostics;
using static League.FormMain;

namespace League.UIHelpers
{
    /// <summary>
    /// 表格视图管理器
    /// </summary>
    public class TableViewManager
    {
        private ToolTip _toolTip;

        public TableViewManager(ToolTip toolTip)
        {
            _toolTip = toolTip;
        }

        /// <summary>
        /// 初始化表格视图
        /// </summary>
        public void InitializeTableView(TabPage tabPage, MatchInfo matchInfo)
        {
            var dataGridView = new CustomDataGridView()
            {
                Dock = DockStyle.Fill,
                ReadOnly = true,
                AutoGenerateColumns = false,
                AllowUserToAddRows = false,
                BackgroundColor = Color.White,
                Font = new Font("微软雅黑", 9),
                RowTemplate = { Height = 30 }
            };

            // 添加列
            AddTableViewColumns(dataGridView);

            // 异步加载数据
            LoadTableViewDataAsync(dataGridView, matchInfo);

            // 添加事件
            dataGridView.CellMouseMove += DataGridView_CellMouseMove;
            dataGridView.CellFormatting += DataGridView_CellFormatting;

            tabPage.Controls.Add(dataGridView);
        }

        /// <summary>
        /// 添加表格列
        /// </summary>
        private void AddTableViewColumns(DataGridView dataGridView)
        {
            DataGridViewColumn[] columns = {
            new DataGridViewTextBoxColumn { HeaderText = "队伍", DataPropertyName = "Team", Width = 50 },
            new DataGridViewImageColumn { HeaderText = "英雄", DataPropertyName = "ChampionIcon", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "玩家名称", DataPropertyName = "PlayerName", Width = 180 },
            new DataGridViewTextBoxColumn { HeaderText = "K/D/A", DataPropertyName = "KDA", Width = 70 },
            new DataGridViewTextBoxColumn { HeaderText = "伤害", DataPropertyName = "Damage", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "经济", DataPropertyName = "Gold", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "补刀", DataPropertyName = "CS", Width = 60 },
            new DataGridViewTextBoxColumn { HeaderText = "参团率", DataPropertyName = "KP", Width = 100 },
            new DataGridViewTextBoxColumn { HeaderText = "视野分", DataPropertyName = "Vision", Width = 60 },
            new DataGridViewImageColumn { HeaderText = "召唤师技能", DataPropertyName = "SpellsImage", Width = 60 },
            new DataGridViewImageColumn { HeaderText = "装备", DataPropertyName = "ItemsImage", Width = 180 },
            new DataGridViewImageColumn { HeaderText = "符文", DataPropertyName = "RunesImage", Width = 180 }
        };

            dataGridView.Columns.AddRange(columns);
        }

        /// <summary>
        /// 异步加载表格数据
        /// </summary>
        private async void LoadTableViewDataAsync(DataGridView dataGridView, MatchInfo matchInfo)
        {
            try
            {
                var playerDetails = await Task.Run(() => ParseMatchDetailsForTableView(matchInfo));

                // 检查控件是否已经创建了窗口句柄
                if (dataGridView.IsHandleCreated)
                {
                    dataGridView.Invoke(new Action(() => dataGridView.DataSource = playerDetails));
                }
                else
                {
                    // 如果句柄还没创建，等待一下再尝试
                    await Task.Delay(100);
                    if (dataGridView.IsHandleCreated)
                    {
                        dataGridView.Invoke(new Action(() => dataGridView.DataSource = playerDetails));
                    }
                    else
                    {
                        // 如果还是不行，直接设置（在UI线程中）
                        dataGridView.DataSource = playerDetails;
                    }
                }
            }
            catch (Exception ex)
            {
                HandleError("加载表格数据失败", ex);
            }
        }

        /// <summary>
        /// 为表格视图解析数据
        /// </summary>
        private async Task<List<TableViewPlayerDetail>> ParseMatchDetailsForTableView(MatchInfo matchInfo)
        {
            var details = new List<TableViewPlayerDetail>();

            if (matchInfo?.AllParticipants == null) return details;

            // 获取所有玩家的排名数据
            var allDamage = matchInfo.AllParticipants.Select(p => p["totalDamageDealtToChampions"]?.Value<int>() ?? 0).ToList();
            var allGold = matchInfo.AllParticipants.Select(p => p["goldEarned"]?.Value<int>() ?? 0).ToList();
            var allKP = matchInfo.AllParticipants.Select(p => p["challenges"]?["killParticipation"]?.Value<double>() ?? 0).ToList();

            foreach (var participant in matchInfo.AllParticipants)
            {
                string playerName = "";
                bool isSelf = false;
                string summonerId = participant["summonerId"]?.ToString();
                string riotName = participant["riotIdGameName"].ToString();
                string ritoTag = participant["riotIdTagline"].ToString();

                //排位名称
                string ritoRankName = participant["summonerName"].ToString();

                if (!string.IsNullOrEmpty(riotName) && !string.IsNullOrEmpty(ritoTag)) 
                {
                    isSelf = riotName == matchInfo.SelfPlayer?.FullName;
                    playerName = $"{riotName}#{ritoTag}";
                }

                var summonerData = await Globals.lcuClient.GetGameNameBySummonerId(summonerId);
                if (summonerData !=null)
                {
                    isSelf = summonerData["gameName"].ToString() == matchInfo.SelfPlayer?.FullName;
                    playerName = $"{summonerData["gameName"].ToString()}#{summonerData["tagLine"].ToString()}";
                }
                
                int championId = participant["championId"]?.Value<int>() ?? 0;
                int damage = participant["totalDamageDealtToChampions"]?.Value<int>() ?? 0;
                int gold = participant["goldEarned"]?.Value<int>() ?? 0;
                double kp = participant["challenges"]?["killParticipation"]?.Value<double>() ?? 0;

                // 计算排名
                int damageRank = GetRank(allDamage, damage);
                int goldRank = GetRank(allGold, gold);
                int kpRank = GetRank(allKP, kp);

                // 获取英雄信息 - 使用表格视图专用方法（只给图片加背景）
                var championInfoResult = Globals.resLoading.GetChampionInfoAsync(championId).Result;
                var championIcon = ImageProcessor.ResizeImageForTableView(championInfoResult.Item1, 25, 25);

                // 生成图片和ToolTip - 使用修复后的表格视图专用方法（单元格透明背景）
                var spellsResult = GenerateSpellsImageWithTooltipForTableView(participant);
                var itemsResult = GenerateItemsImageWithTooltipForTableView(participant);
                var runesResult = GenerateRunesImageWithTooltipForTableView(participant);

                var detail = new TableViewPlayerDetail
                {
                    Team = participant["teamId"]?.Value<int>() == 100 ? "蓝队" : "红队",
                    ChampionIcon = championIcon,
                    PlayerName = playerName,
                    KDA = $"{participant["kills"]}/{participant["deaths"]}/{participant["assists"]}",
                    Damage = $"{damage} (#{damageRank})",
                    Gold = $"{gold} (#{goldRank})",
                    CS = participant["totalMinionsKilled"]?.Value<int>() ?? 0,
                    KP = $"{kp:P1} (#{kpRank})",
                    Vision = participant["visionScore"]?.Value<int>() ?? 0,
                    SpellsImage = spellsResult.image,
                    ItemsImage = itemsResult.image,
                    RunesImage = runesResult.image,
                    IsSelf = isSelf,
                    //IsSelf = playerName == matchInfo.SelfPlayer?.FullName,

                    // 设置ToolTip
                    ChampionTooltip = $"英雄: {championInfoResult.Item2}\n\n{StripHtmlTags(championInfoResult.Item3)}",
                    SpellsTooltip = spellsResult.tooltip,
                    ItemsTooltip = itemsResult.tooltip,
                    RunesTooltip = runesResult.tooltip
                };

                details.Add(detail);
            }

            return details;
        }

        /// <summary>
        /// 为表格视图生成召唤师技能图片（只给图片加背景，不给单元格加背景）
        /// </summary>
        private (Image image, string tooltip) GenerateSpellsImageWithTooltipForTableView(JObject participant)
        {
            try
            {
                var bitmap = new Bitmap(60, 30);
                var spellNames = new List<string>();

                using (var g = Graphics.FromImage(bitmap))
                {
                    // 设置透明背景，不在整个单元格加黑色背景
                    g.Clear(Color.Transparent);

                    int x = 2; // 从左边距开始
                    for (int i = 1; i <= 2; i++)
                    {
                        int spellId = participant[$"spell{i}Id"]?.Value<int>() ?? 0;
                        if (spellId > 0)
                        {
                            try
                            {
                                var spellInfo = Globals.resLoading.GetSpellInfoAsync(spellId).Result;
                                if (spellInfo.Item1 != null)
                                {
                                    // 只给单个技能图片加黑色背景
                                    var spellIcon = ImageProcessor.ResizeImageForTableView(spellInfo.Item1, 20, 20);
                                    if (spellIcon != null)
                                    {
                                        g.DrawImage(spellIcon, x, 5, 20, 20);
                                        x += 25;
                                        spellNames.Add(spellInfo.Item2 ?? $"技能{spellId}");
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"召唤师技能 {spellId} 加载异常: {ex.Message}");
                            }
                        }
                        if (x == 2) x += 25;
                        else if (i == 2 && x == 27) x += 25;
                    }
                }

                string tooltip = spellNames.Count > 0
                    ? $"召唤师技能:\n{string.Join(" + ", spellNames)}"
                    : "无召唤师技能";

                return (bitmap, tooltip);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"生成召唤师技能图片异常: {ex.Message}");
                // 返回透明背景的默认图片
                return (ImageProcessor.CreateDefaultImage(60, 30, Color.Transparent), "召唤师技能加载失败");
            }
        }

        /// <summary>
        /// 为表格视图生成装备图片（只给图片加背景，不给单元格加背景）
        /// </summary>
        private (Image image, string tooltip) GenerateItemsImageWithTooltipForTableView(JObject participant)
        {
            Bitmap bitmap = null;

            try
            {
                bitmap = new Bitmap(180, 30);
                var itemNames = new List<string>();

                using (var g = Graphics.FromImage(bitmap))
                {
                    // 设置透明背景，不在整个单元格加黑色背景
                    g.Clear(Color.Transparent);

                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;

                    int x = 2; // 从左边距开始

                    for (int i = 0; i < 6; i++)
                    {
                        int itemId = participant[$"item{i}"]?.Value<int>() ?? 0;

                        if (itemId <= 0)
                        {
                            // 绘制空装备槽（透明背景）
                            var emptySlot = ImageProcessor.CreateEmptySlotImage(20, 20);
                            g.DrawImage(emptySlot, x, 5, 20, 20);
                            x += 25;
                            continue;
                        }

                        try
                        {
                            var task = Globals.resLoading.GetItemInfoAsync(itemId);
                            if (task.Wait(500))
                            {
                                var itemInfo = task.Result;
                                if (itemInfo.Item1 != null)
                                {
                                    // 只给单个装备图片加黑色背景
                                    var itemIcon = ImageProcessor.ResizeImageForTableView(itemInfo.Item1, 20, 20);
                                    if (itemIcon != null)
                                    {
                                        g.DrawImage(itemIcon, x, 5, 20, 20);
                                        itemNames.Add(itemInfo.Item2 ?? $"物品{itemId}");
                                    }
                                    else
                                    {
                                        var emptySlot = ImageProcessor.CreateEmptySlotImage(20, 20);
                                        g.DrawImage(emptySlot, x, 5, 20, 20);
                                    }
                                }
                                else
                                {
                                    var emptySlot = ImageProcessor.CreateEmptySlotImage(20, 20);
                                    g.DrawImage(emptySlot, x, 5, 20, 20);
                                }
                            }
                            else
                            {
                                var emptySlot = ImageProcessor.CreateEmptySlotImage(20, 20);
                                g.DrawImage(emptySlot, x, 5, 20, 20);
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"装备 {itemId} 加载异常: {ex.Message}");
                            var emptySlot = ImageProcessor.CreateEmptySlotImage(20, 20);
                            g.DrawImage(emptySlot, x, 5, 20, 20);
                        }

                        x += 25;
                    }
                }

                string tooltip = itemNames.Count > 0
                    ? $"装备:\n{string.Join(" | ", itemNames)}"
                    : "无装备";

                return (bitmap, tooltip);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"生成装备图片异常: {ex.Message}");
                bitmap?.Dispose();
                // 返回透明背景的默认图片
                return (ImageProcessor.CreateDefaultImage(180, 30, Color.Transparent), "装备数据加载失败");
            }
        }

        /// <summary>
        /// 为表格视图生成符文图片（只给图片加背景，不给单元格加背景）
        /// </summary>
        private (Image image, string tooltip) GenerateRunesImageWithTooltipForTableView(JObject participant)
        {
            try
            {
                var bitmap = new Bitmap(165, 30);
                var runeNames = new List<string>();

                using (var g = Graphics.FromImage(bitmap))
                {
                    // 设置透明背景，不在整个单元格加黑色背景
                    g.Clear(Color.Transparent);

                    // 高质量绘制
                    g.InterpolationMode = System.Drawing.Drawing2D.InterpolationMode.HighQualityBicubic;
                    g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.HighQuality;

                    var runes = LoadRunesForParticipant(participant);
                    int x = 2; // 从左边距开始

                    // 主系符文（4个）
                    if (runes.Primary != null)
                    {
                        for (int i = 0; i < Math.Min(4, runes.Primary.Length); i++)
                        {
                            if (runes.Primary[i] != null && runes.Primary[i].Icon != null)
                            {
                                // 只给单个符文图片加黑色背景
                                var runeIcon = ImageProcessor.ResizeImageForTableView(runes.Primary[i].Icon, 18, 18);
                                if (runeIcon != null)
                                {
                                    g.DrawImage(runeIcon, x, 6, 18, 18); // 垂直居中
                                    runeNames.Add(runes.Primary[i].Name ?? $"符文{runes.Primary[i].RuneId}");
                                }
                            }
                            x += 22; // 符文间距
                        }
                    }
                    else
                    {
                        x += 88; // 4个符文的位置
                    }

                    // 副系符文（2个）
                    if (runes.Secondary != null && runes.Secondary.Length >= 2)
                    {
                        x += 8; // 主副符文间距
                        for (int i = 0; i < 2; i++)
                        {
                            if (runes.Secondary[i] != null && runes.Secondary[i].Icon != null)
                            {
                                // 只给单个符文图片加黑色背景
                                var runeIcon = ImageProcessor.ResizeImageForTableView(runes.Secondary[i].Icon, 18, 18);
                                if (runeIcon != null)
                                {
                                    g.DrawImage(runeIcon, x, 6, 18, 18); // 垂直居中
                                    runeNames.Add(runes.Secondary[i].Name ?? $"符文{runes.Secondary[i].RuneId}");
                                }
                            }
                            x += 22; // 符文间距
                        }
                    }

                    // 如果没有符文数据，绘制提示文字
                    if (runeNames.Count == 0)
                    {
                        using (var font = new Font("微软雅黑", 8))
                        using (var brush = new SolidBrush(Color.Gray))
                        {
                            g.DrawString("无符文", font, brush, new PointF(5, 8));
                        }
                    }
                }

                string tooltip = runeNames.Count > 0
                    ? $"符文:\n{string.Join(" + ", runeNames)}"
                    : "无符文数据";

                return (bitmap, tooltip);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"生成符文图片异常: {ex.Message}");
                // 返回透明背景的默认图片
                return (ImageProcessor.CreateDefaultImage(165, 30, Color.Transparent), "符文加载失败");
            }
        }

        /// <summary>
        /// 计算排名
        /// </summary>
        private int GetRank<T>(List<T> allValues, T currentValue) where T : IComparable
        {
            var sorted = allValues.OrderByDescending(v => v).ToList();
            for (int i = 0; i < sorted.Count; i++)
            {
                if (currentValue.CompareTo(sorted[i]) == 0)
                    return i + 1;
            }
            return sorted.Count;
        }

        /// <summary>
        /// 加载玩家符文数据
        /// </summary>
        private (RuneInfo[] Primary, RuneInfo[] Secondary) LoadRunesForParticipant(JObject participant)
        {
            var perks = participant["perks"];
            if (perks == null) return (null, null);

            var styles = perks["styles"] as JArray;
            if (styles == null) return (null, null);

            // 获取主系符文
            var primaryStyle = styles.FirstOrDefault(s => s["description"]?.ToString() == "primaryStyle");
            var primaryRunes = primaryStyle?["selections"]?.Cast<JObject>().Take(4).Select(selection =>
            {
                var runeId = selection["perk"]?.Value<int>() ?? 0;
                if (runeId <= 0) return null;

                var runeInfo = Globals.resLoading.GetRuneInfoAsync(runeId).Result;
                return new RuneInfo
                {
                    RuneId = runeId,
                    Icon = runeInfo.Item1,
                    Name = runeInfo.Item2,
                    Description = runeInfo.Item3
                };
            }).Where(r => r != null).ToArray();

            // 获取副系符文
            var secondaryStyle = styles.FirstOrDefault(s => s["description"]?.ToString() == "subStyle");
            var secondaryRunes = secondaryStyle?["selections"]?.Cast<JObject>().Take(2).Select(selection =>
            {
                var runeId = selection["perk"]?.Value<int>() ?? 0;
                if (runeId <= 0) return null;

                var runeInfo = Globals.resLoading.GetRuneInfoAsync(runeId).Result;
                return new RuneInfo
                {
                    RuneId = runeId,
                    Icon = runeInfo.Item1,
                    Name = runeInfo.Item2,
                    Description = runeInfo.Item3
                };
            }).Where(r => r != null).ToArray();

            return (primaryRunes, secondaryRunes);
        }

        /// <summary>
        /// 表格单元格鼠标移动事件
        /// </summary>
        private void DataGridView_CellMouseMove(object sender, DataGridViewCellMouseEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex < 0) return;

            var dataGridView = (DataGridView)sender;
            var playerDetail = dataGridView.Rows[e.RowIndex].DataBoundItem as TableViewPlayerDetail;
            if (playerDetail == null) return;

            string tooltipText = GetColumnTooltip(playerDetail, e.ColumnIndex);
            _toolTip.SetToolTip(dataGridView, tooltipText);
        }

        /// <summary>
        /// 获取列ToolTip
        /// </summary>
        private string GetColumnTooltip(TableViewPlayerDetail playerDetail, int columnIndex)
        {
            return columnIndex switch
            {
                1 => playerDetail.ChampionTooltip,      // 英雄列
                9 => playerDetail.SpellsTooltip,        // 召唤师技能列
                10 => playerDetail.ItemsTooltip,        // 装备列
                11 => playerDetail.RunesTooltip,        // 符文列
                _ => null
            };
        }

        /// <summary>
        /// 表格单元格格式化事件
        /// </summary>
        private void DataGridView_CellFormatting(object sender, DataGridViewCellFormattingEventArgs e)
        {
            if (e.RowIndex < 0 || e.ColumnIndex != 2) return; // 只处理玩家名称列

            var dataGridView = (DataGridView)sender;
            var row = dataGridView.Rows[e.RowIndex];
            var playerDetail = row.DataBoundItem as TableViewPlayerDetail;

            if (playerDetail?.IsSelf == true)
            {
                row.DefaultCellStyle.BackColor = Color.LightYellow;
                row.DefaultCellStyle.Font = new Font(dataGridView.Font, FontStyle.Bold);
            }
        }

        /// <summary>
        /// 错误处理
        /// </summary>
        private void HandleError(string message, Exception ex)
        {
            Debug.WriteLine($"{message}: {ex.Message}\n{ex.StackTrace}");
            MessageBox.Show($"{message}: {ex.Message}");
        }

        /// <summary>
        /// 清理HTML标签
        /// </summary>
        private string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input)) return string.Empty;
            input = input.Replace("<br>", "\n").Replace("<br/>", "\n").Replace("<br />", "\n");
            return System.Text.RegularExpressions.Regex.Replace(input, "<.*?>", "").Trim();
        }
    }

    /// <summary>
    /// 自定义DataGridView以启用双缓冲
    /// </summary>
    public class CustomDataGridView : DataGridView
    {
        public CustomDataGridView()
        {
            DoubleBuffered = true;
        }
    }
}
