using League.Models;
using League.PrimaryElection;
using Newtonsoft.Json;
using System.Diagnostics;
using static League.FormMain;

namespace League
{
    public partial class Preliminary : Form
    {
        // 配置与路径
        //private const string ConfigFilePath = "LeagueConfig.json";
        //private LeagueConfig _appConfig = new LeagueConfig();

        private readonly LeagueConfig _config;  // 统一用一个变量，语义清晰

        // 英雄管理
        private ChampionManager _championManager;
        private bool _isDataLoaded = false;

        // UI 相关
        private ImageList _championImageList;

        // 搜索专用：保存当前选中的位置下所有英雄（用于实时过滤）
        private List<PrimaryChampion> _currentPositionChampions = new List<PrimaryChampion>();

        // 搜索专用：所有英雄（全局，用于全局搜索）
        private List<PrimaryChampion> _allChampions = new List<PrimaryChampion>();

        private LeagueConfig _preConfig;

        public Preliminary(LeagueConfig config)
        {
            InitializeComponent();

            _config = config ?? throw new ArgumentNullException(nameof(config));  // 使用传入的实例

            // 1. 初始化控件与状态
            InitializeComponents();
            InitializeContextMenus();

            // 2. 绑定事件
            BindButtonEvents();

            // 3. 获取英雄管理器单例并订阅事件（防止重复订阅）
            _championManager = Globals.resLoading.ChampionManager;
            _championManager.ChampionsLoaded -= OnChampionsLoaded;
            _championManager.LoadError -= OnChampionsLoadError;
            _championManager.ChampionsLoaded += OnChampionsLoaded;
            _championManager.LoadError += OnChampionsLoadError;

            // 4. 异步初始化英雄数据
            InitializeAsync();

            // 5. 窗体关闭时保存
            this.FormClosing += Preliminary_FormClosing;

            // ===== 新增：启用右边 ListView 的拖拽排序 =====
            rightListView.AllowDrop = true;                    // 必须
            rightListView.ItemDrag += RightListView_ItemDrag;
            rightListView.DragEnter += RightListView_DragEnter;
            rightListView.DragOver += RightListView_DragOver;
            rightListView.DragDrop += RightListView_DragDrop;
            rightListView.DragLeave += RightListView_DragLeave;
        }

        #region 预选列表拖拽排序
        private void RightListView_ItemDrag(object sender, ItemDragEventArgs e)
        {
            // 只允许左键拖拽，且有选中项时才开始拖拽
            if (e.Button == MouseButtons.Left && rightListView.SelectedItems.Count > 0)
            {
                rightListView.DoDragDrop(rightListView.SelectedItems, DragDropEffects.Move);
            }
        }

        private void RightListView_DragEnter(object sender, DragEventArgs e)
        {
            e.Effect = DragDropEffects.Move;
        }

        private void RightListView_DragOver(object sender, DragEventArgs e)
        {
            Point cp = rightListView.PointToClient(new Point(e.X, e.Y));
            ListViewItem hoverItem = rightListView.GetItemAt(cp.X, cp.Y);

            if (hoverItem != null)
            {
                Rectangle bounds = hoverItem.Bounds;
                int margin = bounds.Height / 3; // 上1/3 插前面，下2/3 插后面，更灵敏
                bool appearsAfter = (cp.Y - bounds.Top) > margin;

                rightListView.InsertionMark.AppearsAfterItem = appearsAfter;
                rightListView.InsertionMark.Index = hoverItem.Index;
            }
            else
            {
                // 拖到空白区域，放在最后
                rightListView.InsertionMark.Index = rightListView.Items.Count;
                rightListView.InsertionMark.AppearsAfterItem = false;
            }
        }

        private void RightListView_DragDrop(object sender, DragEventArgs e)
        {
            if (rightListView.SelectedItems.Count == 0) return;

            // 1. 先获取当前插入标记的准确目标位置
            int targetIndex = rightListView.InsertionMark.Index;
            bool dropAfter = rightListView.InsertionMark.AppearsAfterItem;

            // 如果标记在最后一项之后，targetIndex 会是 Items.Count，需要特殊处理
            if (targetIndex == rightListView.Items.Count)
                dropAfter = true;

            // 计算最终插入位置（在移除前计算）
            int insertAt = dropAfter ? targetIndex + 1 : targetIndex;

            // 2. 收集所有被拖拽的项（支持多选）
            var draggedItems = rightListView.SelectedItems
                .Cast<ListViewItem>()
                .OrderBy(item => item.Index)  // 按原顺序收集，后面插入时保持相对顺序
                .ToList();

            if (draggedItems.Count == 0) return;

            // 3. 计算偏移：被拖走的项中，有多少原本在目标位置之前
            int removedBeforeTarget = draggedItems.Count(item => item.Index < targetIndex);

            // 最终插入位置 = 原始目标位置 - 被移除的“前面项”数量
            int finalInsertIndex = insertAt - removedBeforeTarget;

            // 边界保护
            if (finalInsertIndex < 0) finalInsertIndex = 0;
            if (finalInsertIndex > rightListView.Items.Count) finalInsertIndex = rightListView.Items.Count;

            // 4. 先移除所有拖拽项（从后往前移除，避免索引错乱）
            for (int i = draggedItems.Count - 1; i >= 0; i--)
            {
                rightListView.Items.Remove(draggedItems[i]);
            }

            // 5. 在正确位置重新插入（保持原相对顺序）
            foreach (var item in draggedItems)
            {
                rightListView.Items.Insert(finalInsertIndex++, item);
            }

            // 6. 保持选中状态
            foreach (var item in draggedItems)
                item.Selected = true;
            rightListView.Focus();

            // 7. 更新优先级和显示序号
            UpdatePreSelectedPriorities();

            // 8. 清除插入标记
            rightListView.InsertionMark.Index = -1;
        }

        private void RightListView_DragLeave(object sender, EventArgs e)
        {
            rightListView.InsertionMark.Index = -1;
        }

        // 辅助方法：精确计算插入位置（处理多选时索引偏移）
        private int CalculateInsertIndex(List<ListViewItem> draggedItems, int originalTarget, bool appearsAfter)
        {
            int offset = 0;
            foreach (var item in draggedItems)
            {
                if (item.Index < originalTarget)
                    offset++;
            }
            return appearsAfter ? originalTarget - offset + draggedItems.Count : originalTarget - offset;
        }
        #endregion

        #region 事件订阅与清理

        private void Preliminary_FormClosing(object sender, FormClosingEventArgs e)
        {
            if (_championManager != null)
            {
                _championManager.ChampionsLoaded -= OnChampionsLoaded;
                _championManager.LoadError -= OnChampionsLoadError;
            }

            SavePreSelectedToConfig();

            // 让主窗体负责最终写入文件
            if (this.Owner is FormMain mainForm)
            {
                mainForm.SaveAppConfig();
            }
        }

        #endregion

        #region 按钮与事件绑定

        private void BindButtonEvents()
        {
            btnTop.Click += (s, e) => OnPositionButtonClick(ChampionPosition.Top, btnTop);
            btnMid.Click += (s, e) => OnPositionButtonClick(ChampionPosition.Mid, btnMid);
            btnJungle.Click += (s, e) => OnPositionButtonClick(ChampionPosition.Jungle, btnJungle);
            btnADC.Click += (s, e) => OnPositionButtonClick(ChampionPosition.ADC, btnADC);
            btnSupport.Click += (s, e) => OnPositionButtonClick(ChampionPosition.Support, btnSupport);
            btnClear.Click += BtnClear_Click;

            // 搜索框实时监听
            txtSearch.TextChanged += TxtSearch_TextChanged;
            txtSearch.PlaceholderText = "搜索英雄（中文/称号/英文）";
        }

        #endregion

        #region 右键菜单

        private void InitializeContextMenus()
        {
            // 左边：添加到预选
            var leftMenu = new ContextMenuStrip();
            leftMenu.Items.Add("添加到预选列表", null, (s, e) => AddToPreSelected());
            leftListView.ContextMenuStrip = leftMenu;

            // 右边：操作预选列表
            var rightMenu = new ContextMenuStrip();
            rightMenu.Items.Add("移除", null, (s, e) => RemoveFromPreSelected());
            rightMenu.Items.Add("上移", null, (s, e) => MoveUpPreSelected());
            rightMenu.Items.Add("下移", null, (s, e) => MoveDownPreSelected());
            rightMenu.Items.Add(new ToolStripSeparator());
            rightMenu.Items.Add("清空全部", null, (s, e) => ClearAllPreSelected());
            rightListView.ContextMenuStrip = rightMenu;
        }

        #endregion

        #region 预选列表操作（添加/移除/移动/清空）

        private void AddToPreSelected()
        {
            if (leftListView.SelectedItems.Count == 0) return;

            int addedCount = 0;
            foreach (ListViewItem item in leftListView.SelectedItems)
            {
                var champion = item.Tag as PrimaryChampion;
                if (champion == null) continue;

                // 防重
                bool exists = rightListView.Items.Cast<ListViewItem>()
                    .Any(x => (x.Tag as PreliminaryHero)?.ChampionId == champion.Id);
                if (exists) continue;

                var preHero = new PreliminaryHero
                {
                    ChampionId = champion.Id,
                    ChampionName = champion.Name,
                    Position = champion.GetPositions().FirstOrDefault().ToString(),
                    AddedTime = DateTime.Now,
                    Priority = rightListView.Items.Count + 1 + addedCount
                };

                var newItem = new ListViewItem(preHero.Priority.ToString());
                newItem.SubItems.Add(champion.Name);           // 英雄名称
                newItem.SubItems.Add(champion.Title);          // 称号
                newItem.SubItems.Add(champion.Alias);          // 英文名
                newItem.SubItems.Add(GetPositionDisplayName(champion.GetPositions())); // 位置

                newItem.Tag = preHero;
                rightListView.Items.Add(newItem);
                addedCount++;
            }

            if (addedCount > 0)
                UpdatePreSelectedPriorities();
            else if (leftListView.SelectedItems.Count == 1)
                MessageBox.Show("该英雄已在预选列表中", "提示", MessageBoxButtons.OK, MessageBoxIcon.Information);
        }

        private void RemoveFromPreSelected()
        {
            if (rightListView.SelectedItems.Count == 0) return;
            foreach (ListViewItem item in rightListView.SelectedItems)
                rightListView.Items.Remove(item);
            UpdatePreSelectedPriorities();
        }

        private void MoveUpPreSelected()
        {
            if (rightListView.SelectedItems.Count != 1) return;
            var item = rightListView.SelectedItems[0];
            int index = item.Index;
            if (index == 0) return;

            rightListView.Items.RemoveAt(index);
            rightListView.Items.Insert(index - 1, item);
            item.Selected = true;
            UpdatePreSelectedPriorities();
        }

        private void MoveDownPreSelected()
        {
            if (rightListView.SelectedItems.Count != 1) return;
            var item = rightListView.SelectedItems[0];
            int index = item.Index;
            if (index >= rightListView.Items.Count - 1) return;

            rightListView.Items.RemoveAt(index);
            rightListView.Items.Insert(index + 1, item);
            item.Selected = true;
            UpdatePreSelectedPriorities();
        }

        private void ClearAllPreSelected()
        {
            if (rightListView.Items.Count == 0) return;
            if (MessageBox.Show("确定要清空所有预选英雄吗？", "确认清空",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes)
            {
                rightListView.Items.Clear();
            }
        }

        private void BtnClear_Click(object sender, EventArgs e) => ClearAllPreSelected();

        private void UpdatePreSelectedPriorities()
        {
            for (int i = 0; i < rightListView.Items.Count; i++)
            {
                var item = rightListView.Items[i];
                if (item.Tag is PreliminaryHero preHero)
                {
                    preHero.Priority = i + 1;
                    item.Text = (i + 1).ToString();                    // 序
                    item.SubItems[2].Text = (i + 1).ToString();        // 优先级列
                }
            }
        }

        #endregion

        #region 位置按钮与高亮

        private void OnPositionButtonClick(ChampionPosition position, Button button)
        {
            if (!_isDataLoaded) return;
            LoadChampionsByPosition(position);
            HighlightActiveButton(button);
        }

        private void HighlightActiveButton(Button activeButton)
        {
            var buttons = new[] { btnTop, btnMid, btnJungle, btnADC, btnSupport };
            foreach (var btn in buttons)
            {
                btn.BackColor = SystemColors.Control;
                btn.ForeColor = SystemColors.ControlText;
                btn.Font = new Font(btn.Font, FontStyle.Regular);
            }
            activeButton.BackColor = Color.FromArgb(0, 120, 215);
            activeButton.ForeColor = Color.White;
            activeButton.Font = new Font(activeButton.Font, FontStyle.Bold);
        }

        #endregion

        #region 英雄数据加载与初始化

        private void InitializeComponents()
        {
            _championImageList = new ImageList
            {
                ImageSize = new Size(32, 32),
                ColorDepth = ColorDepth.Depth32Bit
            };
            SetButtonsEnabled(false);
        }

        private void SetButtonsEnabled(bool enabled)
        {
            btnTop.Enabled = btnMid.Enabled = btnJungle.Enabled =
            btnADC.Enabled = btnSupport.Enabled = btnClear.Enabled = enabled;
        }

        private async void InitializeAsync()
        {
            try
            {
                Debug.WriteLine("[Preliminary] 正在初始化英雄管理器...");
                await _championManager.InitializeAsync(forceRefresh: false);

                // 缓存命中时不会触发事件，手动调用
                if (_championManager.IsInitialized && _championManager.AllChampions.Count > 0)
                {
                    OnChampionsLoaded(this, EventArgs.Empty);
                }
            }
            catch (Exception ex)
            {
                MessageBox.Show($"初始化失败: {ex.Message}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
                this.Close();
            }
        }

        
        private void OnChampionsLoaded(object sender, EventArgs e)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnChampionsLoaded(sender, e)));
                return;
            }

            if (_isDataLoaded) return;  // 防止重复执行

            _isDataLoaded = true;
            SetButtonsEnabled(true);

            // 【新增】保存所有英雄用于全局搜索
            _allChampions = _championManager.AllChampions.ToList();

            Debug.WriteLine($"[Preliminary] 英雄数据加载完成，共 {_allChampions.Count} 个英雄");

            LoadChampionsByPosition(ChampionPosition.Top);
            HighlightActiveButton(btnTop);

            RestorePreSelectedFromConfig(); // 恢复预选列表
        }

        private void OnChampionsLoadError(object sender, string errorMessage)
        {
            if (this.InvokeRequired)
            {
                this.Invoke(new Action(() => OnChampionsLoadError(sender, errorMessage)));
                return;
            }
            MessageBox.Show($"英雄数据加载失败: {errorMessage}", "错误", MessageBoxButtons.OK, MessageBoxIcon.Error);
            this.Close();
        }

        #endregion

        #region 左边英雄列表加载与搜索

        private void LoadChampionsByPosition(ChampionPosition position)
        {
            if (!_isDataLoaded || _championManager == null) return;

            // 如果当前有搜索文字，不受位置按钮影响（保持当前搜索结果）
            if (!string.IsNullOrEmpty(txtSearch.Text.Trim()))
                return;

            leftListView.BeginUpdate();
            leftListView.Items.Clear();

            var champions = _championManager.GetChampionsByPosition(position);

            // 保存当前位置英雄（仅用于搜索为空时恢复）
            _currentPositionChampions = champions.ToList();

            int index = 1;
            foreach (var champ in champions)
            {
                AddChampionToList(champ, index++);
            }

            leftListView.EndUpdate();

            this.Text = $"英雄预选 - 上单 ({champions.Count} 个英雄)";
        }

        private void AddChampionToList(PrimaryChampion champion, int index)
        {
            var item = new ListViewItem(index.ToString());
            item.SubItems.Add(champion.Name);      // 英雄名称
            item.SubItems.Add(champion.Title);     // 称号
            item.SubItems.Add(champion.Alias);     // 英文名
            item.SubItems.Add(GetPositionTags(champion)); // 位置

            item.Tag = champion;
            leftListView.Items.Add(item);
        }

        private void TxtSearch_TextChanged(object sender, EventArgs e)
        {
            ApplySearchFilter();
        }

        private void ApplySearchFilter()
        {
            string keyword = txtSearch.Text.Trim();
            bool hasKeyword = !string.IsNullOrEmpty(keyword);

            IEnumerable<PrimaryChampion> source;

            if (hasKeyword)
            {
                // 全局搜索：所有英雄中匹配
                source = _allChampions.Where(c =>
                    c.Name.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    c.Title.Contains(keyword, StringComparison.OrdinalIgnoreCase) ||
                    c.Alias.Contains(keyword, StringComparison.OrdinalIgnoreCase));
            }
            else
            {
                // 无搜索：显示当前选中的位置英雄
                source = _currentPositionChampions;
            }

            leftListView.BeginUpdate();
            leftListView.Items.Clear();

            int index = 1;
            foreach (var champ in source)
            {
                AddChampionToList(champ, index++);
            }

            leftListView.EndUpdate();

            this.Text = hasKeyword
                ? $"英雄预选 - 搜索 \"{keyword}\" 找到 {source.Count()} 个英雄"
                : $"英雄预选 - {GetPositionName(GetCurrentPosition())} ({source.Count()} 个英雄)";
        }

        // 辅助方法：获取当前选中位置（可选，用于标题显示）
        private ChampionPosition GetCurrentPosition()
        {
            if (btnTop.BackColor == Color.FromArgb(0, 120, 215)) return ChampionPosition.Top;
            if (btnMid.BackColor == Color.FromArgb(0, 120, 215)) return ChampionPosition.Mid;
            if (btnJungle.BackColor == Color.FromArgb(0, 120, 215)) return ChampionPosition.Jungle;
            if (btnADC.BackColor == Color.FromArgb(0, 120, 215)) return ChampionPosition.ADC;
            if (btnSupport.BackColor == Color.FromArgb(0, 120, 215)) return ChampionPosition.Support;
            return ChampionPosition.Top;
        }

        private string GetPositionTags(PrimaryChampion champion)
        {
            var positions = champion.GetPositions();
            if (positions.Count == 0) return "?";

            return string.Join("/", positions.Select(p => p switch
            {
                ChampionPosition.Top => "上",
                ChampionPosition.Mid => "中",
                ChampionPosition.Jungle => "野",
                ChampionPosition.ADC => "AD",
                ChampionPosition.Support => "辅",
                _ => "?"
            }));
        }

        private string GetPositionName(ChampionPosition position)
        {
            return position switch
            {
                ChampionPosition.Top => "上单",
                ChampionPosition.Mid => "中单",
                ChampionPosition.Jungle => "打野",
                ChampionPosition.ADC => "射手",
                ChampionPosition.Support => "辅助",
                _ => "全部"
            };
        }

        #endregion

        #region 预选列表持久化

        private void RestorePreSelectedFromConfig()
        {
            rightListView.Items.Clear();

            if (_config.Preliminary?.Heroes == null || _config.Preliminary.Heroes.Count == 0)
                return;

            var sorted = _config.Preliminary.Heroes.OrderBy(h => h.Priority).ToList();
            foreach (var preHero in sorted)
            {
                var champion = _championManager.GetChampionById(preHero.ChampionId);
                if (champion == null) continue;

                var item = new ListViewItem((rightListView.Items.Count + 1).ToString());
                item.SubItems.Add(champion.Name);
                item.SubItems.Add(champion.Title);
                item.SubItems.Add(champion.Alias);
                item.SubItems.Add(GetPositionDisplayName(champion.GetPositions()));
                item.Tag = preHero;
                rightListView.Items.Add(item);
            }
            UpdatePreSelectedPriorities();
        }

        private void SavePreSelectedToConfig()
        {
            try
            {
                _config.Preliminary ??= new PreliminaryConfig();
                _config.Preliminary.Heroes.Clear();

                foreach (ListViewItem item in rightListView.Items)
                {
                    if (item.Tag is PreliminaryHero preHero)
                    {
                        preHero.Priority = item.Index + 1;
                        _config.Preliminary.Heroes.Add(preHero);
                    }
                }

                Debug.WriteLine($"[Preliminary] 预选列表已更新到共享配置，共 {rightListView.Items.Count} 个英雄");
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"[Preliminary] 更新预选失败: {ex.Message}");
            }
        }

        private string GetPositionDisplayName(List<ChampionPosition> positions)
        {
            if (positions == null || positions.Count == 0) return "?";
            return string.Join("/", positions.Select(p => p switch
            {
                ChampionPosition.Top => "上",
                ChampionPosition.Mid => "中",
                ChampionPosition.Jungle => "野",
                ChampionPosition.ADC => "AD",
                ChampionPosition.Support => "辅",
                _ => "?"
            }));
        }

        #endregion
    }
}