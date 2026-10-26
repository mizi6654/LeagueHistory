using System.Diagnostics;

namespace League.UIState
{
    /// <summary>
    /// 主窗体底部状态栏管理：左侧连接/大区，右侧临时操作提示。
    /// 线程安全，可在任意线程调用。
    /// </summary>
    public sealed class StatusBarManager : IDisposable
    {
        private readonly FormMain _form;
        private readonly ToolStripStatusLabel _lblConnection;
        private readonly ToolStripStatusLabel _lblTip;

        private System.Windows.Forms.Timer? _tipTimer;
        private string _lastStatus = "检测中...";
        private string? _lastRegion;

        public StatusBarManager(
            FormMain form,
            ToolStripStatusLabel connectionLabel,
            ToolStripStatusLabel tipLabel)
        {
            _form = form ?? throw new ArgumentNullException(nameof(form));
            _lblConnection = connectionLabel ?? throw new ArgumentNullException(nameof(connectionLabel));
            _lblTip = tipLabel ?? throw new ArgumentNullException(nameof(tipLabel));

            // 初始状态
            SafeSetConnection("检测中...", null);
            SafeSetTip("");
        }

        #region 连接状态（左侧）

        /// <summary>检测中</summary>
        public void SetDetecting()
            => SetConnectionStatus("检测中...");

        /// <summary>已连接（可带大区）</summary>
        public void SetConnected(string? region = null)
            => SetConnectionStatus("已连接", region);

        /// <summary>已断开</summary>
        public void SetDisconnected()
            => SetConnectionStatus("已断开", null);

        /// <summary>正在恢复连接 / 重连中</summary>
        public void SetReconnecting()
            => SetConnectionStatus("正在恢复连接...");

        /// <summary>游戏中</summary>
        public void SetInGame(string? region = null)
            => SetConnectionStatus("游戏中", region);

        /// <summary>查找匹配中</summary>
        public void SetMatching(string? region = null)
            => SetConnectionStatus("查找匹配中", region);

        /// <summary>选人中</summary>
        public void SetChampSelect(string? region = null)
            => SetConnectionStatus("选人中", region);

        /// <summary>游戏已结束</summary>
        public void SetGameEnded(string? region = null)
            => SetConnectionStatus("游戏已结束", region);

        /// <summary>空闲中</summary>
        public void SetIdle(string? region = null)
            => SetConnectionStatus("空闲中", region);

        /// <summary>
        /// 通用：更新左侧连接状态与大区。
        /// region 传 null 表示清空大区；传空字符串表示保持上次大区不变。
        /// </summary>
        public void SetConnectionStatus(string status, string? region = "")
        {
            if (string.IsNullOrWhiteSpace(status))
                status = "未知";

            // region == "" → 保持上次大区；region == null → 明确清空
            if (region == null)
                _lastRegion = null;
            else if (region.Length > 0)
                _lastRegion = region;

            _lastStatus = status;
            SafeSetConnection(_lastStatus, _lastRegion);
        }

        /// <summary>仅更新大区（状态文字不变）</summary>
        public void SetRegion(string? region)
        {
            _lastRegion = string.IsNullOrWhiteSpace(region) ? null : region.Trim();
            SafeSetConnection(_lastStatus, _lastRegion);
        }

        #endregion

        #region 临时提示（右侧）

        /// <summary>
        /// 显示临时提示。autoClearMs &lt;= 0 表示不自动清空。
        /// </summary>
        public void ShowTip(string message, int autoClearMs = 5000)
        {
            SafeSetTip(message ?? "");

            StopTipTimer();

            if (autoClearMs <= 0 || string.IsNullOrEmpty(message))
                return;

            SafeInvoke(() =>
            {
                _tipTimer = new System.Windows.Forms.Timer { Interval = autoClearMs };
                _tipTimer.Tick += (_, _) =>
                {
                    StopTipTimer();
                    SafeSetTip("");
                };
                _tipTimer.Start();
            });
        }

        public void ClearTip() => ShowTip("", 0);

        // 常用场景快捷方法（可按需要增减）
        public void TipReplayDownloading() => ShowTip("正在下载回放...");
        public void TipReplayDone() => ShowTip("回放下载完成");
        public void TipReplayFailed(string? reason = null)
            => ShowTip(string.IsNullOrEmpty(reason) ? "回放下载失败" : $"回放下载失败：{reason}");

        public void TipMatching() => ShowTip("已进入匹配队列");
        public void TipMatchFound() => ShowTip("已找到对局");
        public void TipConfigSaved() => ShowTip("配置已保存");
        public void TipConfigFailed(string? reason = null)
            => ShowTip(string.IsNullOrEmpty(reason) ? "配置保存失败" : $"配置保存失败：{reason}");

        #endregion

        #region 内部实现

        private void SafeSetConnection(string status, string? region)
        {
            SafeInvoke(() =>
            {
                _lblConnection.Text = string.IsNullOrEmpty(region)
                    ? status
                    : $"{status}  |  {region}";
            });
        }

        private void SafeSetTip(string text)
        {
            SafeInvoke(() => _lblTip.Text = text);
        }

        private void SafeInvoke(Action action)
        {
            if (_form.IsDisposed) return;

            try
            {
                if (!_form.IsHandleCreated)
                {
                    // 句柄未创建时尽量直接设（一般在构造/Load 早期）
                    action();
                    return;
                }

                if (_form.InvokeRequired)
                    _form.BeginInvoke(action);
                else
                    action();
            }
            catch (ObjectDisposedException) { }
            catch (InvalidOperationException) { }
            catch (Exception ex)
            {
                Debug.WriteLine($"[StatusBarManager] SafeInvoke: {ex.Message}");
            }
        }

        private void StopTipTimer()
        {
            try
            {
                if (_tipTimer == null) return;
                _tipTimer.Stop();
                _tipTimer.Dispose();
                _tipTimer = null;
            }
            catch { }
        }

        public void Dispose()
        {
            StopTipTimer();
        }

        #endregion
    }
}