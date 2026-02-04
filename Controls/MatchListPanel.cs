using System.Diagnostics;
using System.Text.RegularExpressions;
using League.Models;

namespace League.Controls
{
    public class MatchListPanel : Panel, IDisposable
    {
        private ToolTip _tooltip = new ToolTip();

        private string _lastTooltip = "";

        private MatchInfo _matchInfo;

        private new const int Padding = 10;

        private const int IconSize = 64;

        private const int SpellSize = 30;

        private const int SpellSpacing = 4;

        private const int ItemSize = 30;

        private const int ItemSpacing = 4;

        private const int ModeWidth = 120;

        private const int KdaX = 280;

        private const int SpellStartX = 395;

        private const int ItemsStartX = 260;

        private const int emptyItemX = 190;

        private const int ItemsYOffset = 30;

        private const int TeamIconSize = 30;

        private const int TeamIconSpacing = 2;

        private const int TeamRowSpacing = 8;

        private Rectangle _detailsRect;

        private Rectangle _replayRect;

        private bool _disposed;

        // 存储图片引用的列表
        private List<Image> _matchImages = new List<Image>();

        public MatchInfo MatchInfo
        {
            get
            {
                return _matchInfo;
            }
            set
            {
                _matchInfo = value;
                Invalidate();
            }
        }

        public event Action<MatchInfo> DetailsClicked;

        public event Action<MatchInfo> ReplayClicked;

        public event Action<string> PlayerIconClicked;

        public MatchListPanel(MatchInfo match)
        {
            _matchInfo = match;
            SetStyle(ControlStyles.UserPaint | ControlStyles.ResizeRedraw | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer, value: true);
            BorderStyle = BorderStyle.None;
            BackColor = Color.FloralWhite;
            DoubleBuffered = true;
            Size = new Size(950, 90);
            _tooltip = new ToolTip
            {
                InitialDelay = 100,
                ReshowDelay = 100,
                AutoPopDelay = 15000,
                ShowAlways = true
            };
        }

        // 添加图片到跟踪列表
        public void TrackImage(Image image)
        {
            if (image != null)
                _matchImages.Add(image);
        }

        // 清理所有图片资源
        public void DisposeImages()
        {
            foreach (var image in _matchImages)
            {
                try
                {
                    if (image != null)
                    {
                        // 直接尝试释放，不检查 IsDisposed
                        image.Dispose();
                    }
                }
                catch { }
            }
            _matchImages.Clear();
        }
        

        // 清理所有资源
        public void DisposeAllResources()
        {
            DisposeImages();

            // 清理控件内的图片
            var pictureBoxes = this.Controls.OfType<PictureBox>().ToList();
            foreach (var pb in pictureBoxes)
            {
                if (pb.Image != null)
                {
                    pb.Image.Dispose();
                    pb.Image = null;
                }
            }

            // 清理 MatchInfo
            if (MatchInfo != null)
            {
                MatchInfo.RawGameData = null;
                MatchInfo.AllParticipants = null;
                MatchInfo = null;
            }

            // 解绑事件
            this.DetailsClicked = null;
            this.ReplayClicked = null;
        }

        // 🔥 新增：深度清理方法
        public void DeepClean()
        {
            try
            {
                // 1. 清理图片资源
                DisposeImages();

                // 2. 清理MatchInfo中的资源
                if (_matchInfo != null)
                {
                    _matchInfo.Dispose();
                    _matchInfo = null;
                }

                // 3. 清理控件内的图片
                var pictureBoxes = this.Controls.OfType<PictureBox>().ToList();
                foreach (var pb in pictureBoxes)
                {
                    if (pb.Image != null)
                    {
                        try
                        {
                            pb.Image.Dispose();
                        }
                        catch { }
                        pb.Image = null;
                    }
                }

                // 4. 解绑事件
                this.DetailsClicked = null;
                this.ReplayClicked = null;
                this.PlayerIconClicked = null;

                // 5. 清理ToolTip
                if (_tooltip != null)
                {
                    _tooltip.Dispose();
                    _tooltip = null;
                }

                // 6. 清理子控件
                this.Controls.Clear();

                // 7. 清理引用
                _lastTooltip = null;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[MatchListPanel.DeepClean异常] {ex.Message}");
            }
        }

        // 🔥 增强 Dispose 方法
        public new void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;

                // 先深度清理
                DeepClean();

                // 清理事件
                DetailsClicked = null;
                ReplayClicked = null;
                PlayerIconClicked = null;

                // 清理基类资源
                base.Dispose();
                _tooltip?.Dispose();
                _matchInfo?.Dispose();

                GC.SuppressFinalize(this);
            }
        }


        protected override void OnPaintBackground(PaintEventArgs e)
        {
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (MatchInfo == null)
            {
                return;
            }
            Graphics g = e.Graphics;
            using (SolidBrush backgroundBrush = new SolidBrush(BackColor))
            {
                g.FillRectangle(backgroundBrush, ClientRectangle);
            }
            Font indexFont = new Font("微软雅黑", 8f);
            string indexText = MatchInfo.Index.ToString();
            SizeF textSize = g.MeasureString(indexText, indexFont);
            int borderPadding = 2;
            Rectangle borderRect = new Rectangle(0, Height - (int)textSize.Height - borderPadding - 2, (int)textSize.Width + borderPadding * 2, (int)textSize.Height + borderPadding * 2);
            using (SolidBrush bgBrush = new SolidBrush(Color.White))
            {
                g.FillRectangle(bgBrush, borderRect);
            }
            using (Pen borderPen = new Pen(Color.Purple, 1f))
            {
                g.DrawRectangle(borderPen, borderRect);
            }
            using (SolidBrush indexBrush = new SolidBrush(Color.Blue))
            {
                g.DrawString(indexText, indexFont, indexBrush, borderRect.X + borderPadding, borderRect.Y + borderPadding);
            }
            Font primaryFont = new Font("微软雅黑", 9f);
            Font boldFont = new Font("微软雅黑", 11f, FontStyle.Bold);
            int headerY = 14;
            int iconX = 70;
            using (SolidBrush resultBrush = new SolidBrush(MatchInfo.ResultColor))
            {
                g.DrawString(MatchInfo.ResultText, boldFont, resultBrush, new RectangleF(10f, headerY, 90f, boldFont.Height));
            }
            int durationY = headerY + boldFont.Height + 10;
            g.DrawString(MatchInfo.DurationText, primaryFont, Brushes.DimGray, 10f, durationY);
            g.DrawString(MatchInfo.GameTime, primaryFont, Brushes.DimGray, 140f, durationY);
            if (MatchInfo.HeroIcon != null)
            {
                g.DrawImage(MatchInfo.HeroIcon, iconX, 10, 64, 64);
            }
            int modeX = iconX + 64 + 10;
            g.DrawString(MatchInfo.Mode, primaryFont, Brushes.DarkSlateBlue, new RectangleF(modeX, headerY + 5, 120f, 20f));
            int spellY = headerY - 6;
            if (MatchInfo.SummonerSpells != null)
            {
                for (int i = 0; i < Math.Min(2, MatchInfo.SummonerSpells.Length); i++)
                {
                    if (MatchInfo.SummonerSpells[i] != null)
                    {
                        g.DrawImage(MatchInfo.SummonerSpells[i], 395 + i * 34, spellY, 30, 30);
                    }
                }
            }
            string kdaText = $"{MatchInfo.Kills} / {MatchInfo.Deaths} / {MatchInfo.Assists}";
            g.DrawString(kdaText, primaryFont, Brushes.DarkSlateGray, 280f, headerY - 2);
            int runeY = headerY + 30 - 34;
            

            //新增海克斯符文判断
            // 判断是否为海克斯大乱斗模式
            bool isHextechArena = MatchInfo.Mode?.Contains("海克斯") == true ||
                                 MatchInfo.QueueId == "q_2400";

            if (isHextechArena && MatchInfo.Augments != null && MatchInfo.Augments.Length > 0)
            {
                // 显示海克斯符文
                for (int j = 0; j < 6; j++)
                {
                    int x = 470 + j * 34;
                    Rectangle rect = new Rectangle(x, runeY, 30, 30);

                    // 绘制深色背景
                    using (SolidBrush bgBrush = new SolidBrush(Color.FromArgb(40, 40, 40))) // 深灰色背景
                    {
                        g.FillRectangle(bgBrush, rect);
                    }

                    using (Pen pen = new Pen(Color.Purple, 1f)) // 紫色边框表示海克斯符文
                    {
                        g.DrawRectangle(pen, rect);
                    }
                    using (SolidBrush bgBrush2 = new SolidBrush(Color.Black))
                    {
                        g.FillRectangle(bgBrush2, rect);
                    }

                    Image runeImage = null;
                    if (j < MatchInfo.Augments.Length)
                    {
                        RuneInfo augment = MatchInfo.Augments[j];
                        if (augment != null && augment.Icon != null)
                        {
                            runeImage = augment.Icon;
                        }
                    }

                    if (runeImage != null)
                    {
                        g.DrawImage(runeImage, rect);
                    }
                }

            }
            else
            {
                // 显示普通符文（原有逻辑）
                for (int j = 0; j < 6; j++)
                {
                    int x = 470 + j * 34;
                    Rectangle rect = new Rectangle(x, runeY, 30, 30);
                    using (Pen pen = new Pen(Color.Gray))
                    {
                        g.DrawRectangle(pen, rect);
                    }
                    using (SolidBrush bgBrush2 = new SolidBrush(Color.Black))
                    {
                        g.FillRectangle(bgBrush2, rect);
                    }

                    Image runeImage = null;
                    if (j < 4 && MatchInfo.PrimaryRunes != null && j < MatchInfo.PrimaryRunes.Length)
                    {
                        RuneInfo primaryRune = MatchInfo.PrimaryRunes[j];
                        if (primaryRune != null && primaryRune.Icon != null)
                        {
                            runeImage = primaryRune.Icon;
                        }
                    }
                    else if (j >= 4 && MatchInfo.SecondaryRunes != null && j - 4 < MatchInfo.SecondaryRunes.Length)
                    {
                        RuneInfo secondaryRune = MatchInfo.SecondaryRunes[j - 4];
                        if (secondaryRune != null && secondaryRune.Icon != null)
                        {
                            runeImage = secondaryRune.Icon;
                        }
                    }

                    if (runeImage == null)
                    {
                        continue;
                    }
                    g.DrawImage(runeImage, rect);
                    if (j == 0 && MatchInfo.PrimaryRunes != null && MatchInfo.PrimaryRunes.Length != 0)
                    {
                        using Pen pen2 = new Pen(Color.Goldenrod, 2f);
                        g.DrawRectangle(pen2, rect);
                    }
                }
            }
            //结束----

            Font linkFont = new Font("微软雅黑", 9f, FontStyle.Underline);
            string detailsText = "查看详情数据";
            string replayText = MatchInfo.IsReplayDownloaded ? "播放回放" : "下载回放";
            int detailsX = 570;
            int replayX = detailsX - 80;
            int linkY = runeY + 30 + 8;
            using (SolidBrush linkBrush = new SolidBrush(Color.DodgerBlue))
            {
                g.DrawString(replayText, linkFont, linkBrush, replayX, linkY);
                g.DrawString(detailsText, linkFont, linkBrush, detailsX, linkY);
            }
            SizeF detailsSize = g.MeasureString(detailsText, linkFont);
            SizeF replaySize = g.MeasureString(replayText, linkFont);
            _detailsRect = new Rectangle(detailsX, linkY, (int)detailsSize.Width, (int)detailsSize.Height);
            _replayRect = new Rectangle(replayX, linkY, (int)replaySize.Width, (int)replaySize.Height);
            int itemsY = headerY + 30;
            if (MatchInfo.Items != null)
            {
                for (int k = 0; k < Math.Min(6, MatchInfo.Items.Length); k++)
                {
                    Item item = MatchInfo.Items[k];
                    if (item != null && item.Icon != null)
                    {
                        g.DrawImage(item.Icon, 260 + k * 34, itemsY + 5, 30, 30);
                    }
                }
            }
            int statsX = Width + 190 - 10 - 450;
            g.DrawString("伤害：" + MatchInfo.DamageText, primaryFont, Brushes.OrangeRed, statsX, headerY);
            g.DrawString("经济：" + MatchInfo.GoldText, primaryFont, Brushes.Goldenrod, statsX, headerY + 20);
            g.DrawString("参团：" + MatchInfo.KPPercentage, primaryFont, Brushes.DodgerBlue, statsX, headerY + 40);
            DrawTeamMembers(g, statsX + 100, headerY - 2);
            using Pen borderPen2 = new Pen(MatchInfo.ResultColor, 1f);
            g.DrawRectangle(borderPen2, 0, 0, Width - 1, Height - 1);
        }

        private void DrawTeamMembers(Graphics g, int startX, int baseY)
        {
            if (MatchInfo?.RedTeam != null && MatchInfo?.BlueTeam != null)
            {
                for (int i = 0; i < Math.Min(5, MatchInfo.RedTeam.Count); i++)
                {
                    int x = startX + i * 32;
                    DrawPlayerIcon(g, MatchInfo.RedTeam[i], x, baseY - 4, 30);
                }
                int blueY = baseY + 30 + 8 + 4;
                for (int j = 0; j < Math.Min(5, MatchInfo.BlueTeam.Count); j++)
                {
                    int x2 = startX + j * 32;
                    DrawPlayerIcon(g, MatchInfo.BlueTeam[j], x2, blueY, 30);
                }
            }
        }

        private void DrawPlayerIcon(Graphics g, PlayerInfo player, int x, int y, int size)
        {
            if (player?.Avatar == null)
            {
                return;
            }
            g.DrawImage(player.Avatar, x, y, size, size);
            if (!player.IsSelf)
            {
                return;
            }
            using Pen pen = new Pen(Color.Lime, 3f);
            g.DrawRectangle(pen, x, y, size, size);
        }

        private IEnumerable<Tuple<Rectangle, PlayerInfo>> GetPlayerIconRegions()
        {
            if (MatchInfo == null)
            {
                yield break;
            }
            int statsX = Width - 10 - 250;
            int teamStartX = statsX + 100;
            int baseY = 10;
            if (MatchInfo.RedTeam != null)
            {
                for (int i = 0; i < MatchInfo.RedTeam.Count; i++)
                {
                    yield return Tuple.Create(new Rectangle(teamStartX + i * 32, baseY - 4, 30, 30), MatchInfo.RedTeam[i]);
                }
            }
            int blueY = baseY + 30 + 8 - 6;
            if (MatchInfo.BlueTeam != null)
            {
                for (int j = 0; j < MatchInfo.BlueTeam.Count; j++)
                {
                    yield return Tuple.Create(new Rectangle(teamStartX + j * 32, blueY, 30, 30), MatchInfo.BlueTeam[j]);
                }
            }
        }

        protected override void OnMouseClick(MouseEventArgs e)
        {
            base.OnMouseClick(e);
            if (_replayRect.Contains(e.Location))
            {
                ReplayClicked?.Invoke(MatchInfo);
            }
            else if (_detailsRect.Contains(e.Location))
            {
                DetailsClicked?.Invoke(MatchInfo);
            }
            else
            {
                if (MatchInfo == null)
                {
                    return;
                }
                foreach (Tuple<Rectangle, PlayerInfo> tuple in GetPlayerIconRegions())
                {
                    if (!tuple.Item1.Contains(e.Location) || tuple.Item2 == null)
                    {
                        continue;
                    }
                    try
                    {
                        if (tuple.Item2.IsSelf)
                        {
                            new ToolTip().Show("这是当前玩家", this, e.Location, 1500);
                        }
                        else
                        {
                            PlayerIconClicked?.Invoke(tuple.Item2.SummonerId.ToString());
                        }
                        break;
                    }
                    catch
                    {
                        break;
                    }
                }
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (MatchInfo == null)
            {
                return;
            }
            string tooltipText = null;
            if (_replayRect.Contains(e.Location))
            {
                tooltipText = "查看本场比赛的回放";
            }
            else if (_detailsRect.Contains(e.Location))
            {
                tooltipText = "查看本场比赛的详细数据";
            }
            if (new Rectangle(70, 10, 64, 64).Contains(e.Location) && tooltipText == null)
            {
                tooltipText = MatchInfo.ChampionName;
            }
            int spellY = 8;
            for (int i = 0; i < 2; i++)
            {
                if (new Rectangle(395 + i * 34, spellY, 30, 30).Contains(e.Location) && tooltipText == null)
                {
                    if (MatchInfo.SpellNames != null && MatchInfo.SpellDescriptions != null && i < MatchInfo.SpellNames.Length && i < MatchInfo.SpellDescriptions.Length)
                    {
                        tooltipText = MatchInfo.SpellNames[i] + "\n" + StripHtmlTags(MatchInfo.SpellDescriptions[i]);
                    }
                    break;
                }
            }
            int itemY = 44;
            if (MatchInfo.Items != null)
            {
                for (int j = 0; j < Math.Min(6, MatchInfo.Items.Length); j++)
                {
                    if (new Rectangle(260 + j * 34, itemY, 30, 30).Contains(e.Location) && tooltipText == null)
                    {
                        Item item = MatchInfo.Items[j];
                        if (item != null)
                        {
                            tooltipText = item.Name + "\n" + StripHtmlTags(item.Description);
                        }
                        break;
                    }
                }
            }
            int runeY = 10;

            //根据不同的符文判断显示提示描述
            bool isHextechArena = MatchInfo.Mode?.Contains("海克斯") == true ||
                         MatchInfo.QueueId == "q_2400";
            for (int k = 0; k < 6; k++)
            {
                int x = 470 + k * 34;
                if (new Rectangle(x, runeY, 30, 30).Contains(e.Location) && tooltipText == null)
                {
                    RuneInfo rune = null;

                    if (isHextechArena && MatchInfo.Augments != null && k < MatchInfo.Augments.Length)
                    {
                        // 海克斯符文提示
                        rune = MatchInfo.Augments[k];
                    }
                    else
                    {
                        // 普通符文提示
                        if (k < 4 && MatchInfo.PrimaryRunes != null && k < MatchInfo.PrimaryRunes.Length)
                        {
                            rune = MatchInfo.PrimaryRunes[k];
                        }
                        else if (k >= 4 && MatchInfo.SecondaryRunes != null && k - 4 < MatchInfo.SecondaryRunes.Length)
                        {
                            rune = MatchInfo.SecondaryRunes[k - 4];
                        }
                    }

                    if (rune != null)
                    {
                        tooltipText = rune.name + "\n" + StripHtmlTags(rune.longDesc ?? rune.Description ?? "");
                    }
                    break;
                }
            }


            foreach (Tuple<Rectangle, PlayerInfo> tuple in GetPlayerIconRegions())
            {
                if (tuple.Item1.Contains(e.Location) && tuple.Item2 != null && tooltipText == null)
                {
                    tooltipText = tuple.Item2.FullName;
                    break;
                }
            }
            if (tooltipText != null && tooltipText != _lastTooltip)
            {
                _tooltip.SetToolTip(this, tooltipText);
                _lastTooltip = tooltipText;
            }
            else if (tooltipText == null)
            {
                _tooltip.SetToolTip(this, null);
                _lastTooltip = null;
            }
        }

        public static string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
            {
                return string.Empty;
            }
            input = input.Replace("<br>", "\n").Replace("<br/>", "\n");
            return Regex.Replace(input, "<.*?>", "").Trim();
        }
    }
}
