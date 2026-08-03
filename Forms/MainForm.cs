using System;
using System.Drawing;
using System.Windows.Forms;
using SshTabClient.Models;
using SshTabClient.Services;
using SshTabClient.Terminal;

namespace SshTabClient.Forms
{
    public class MainForm : Form
    {
        private const int CloseButtonSize = 20;

        private readonly TabControl _tabControl;
        private readonly StatusStrip _statusStrip;
        private readonly ToolStripStatusLabel _statusLabel;
        private readonly ProfileStore _store = new();
        private readonly TabPage _addTabPage;
        private int _consoleCounter;
        private float _currentFontSize = 11f;
        private bool _uiReady;
        private int _hoverCloseIndex = -1;

        public MainForm()
        {
            Text = "SSH Client / by : rev708";
            Width = 1000;
            Height = 650;
            StartPosition = FormStartPosition.CenterScreen;
            Font = new Font("맑은 고딕", 9f);

            var toolStrip = new ToolStrip();
            var manageBtn = new ToolStripButton("서버 관리") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            manageBtn.Click += (s, e) => OpenServerManager();
            var closeBtn = new ToolStripButton("현재 탭 닫기") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            closeBtn.Click += (s, e) => CloseCurrentTab();
            var fontMinusBtn = new ToolStripButton("글자 작게") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            fontMinusBtn.Click += (s, e) => ChangeFontSize(-1f);
            var fontPlusBtn = new ToolStripButton("글자 크게") { DisplayStyle = ToolStripItemDisplayStyle.Text };
            fontPlusBtn.Click += (s, e) => ChangeFontSize(1f);
            toolStrip.Items.Add(manageBtn);
            toolStrip.Items.Add(closeBtn);
            toolStrip.Items.Add(new ToolStripSeparator());
            toolStrip.Items.Add(fontMinusBtn);
            toolStrip.Items.Add(fontPlusBtn);

            _statusStrip = new StatusStrip();
            _statusLabel = new ToolStripStatusLabel("준비됨");
            _statusStrip.Items.Add(_statusLabel);

            _tabControl = new TabControl
            {
                Dock = DockStyle.Fill,
                DrawMode = TabDrawMode.OwnerDrawFixed,
                Padding = new Point(8, 6)
            };
            _tabControl.DrawItem += TabControl_DrawItem;
            _tabControl.MouseUp += TabControl_MouseUp;
            _tabControl.MouseMove += TabControl_MouseMove;
            _tabControl.MouseLeave += (s, e) =>
            {
                if (_hoverCloseIndex != -1) { _hoverCloseIndex = -1; _tabControl.Invalidate(); }
            };
            _tabControl.SelectedIndexChanged += (s, e) =>
            {
                if (_uiReady && _tabControl.SelectedTab == _addTabPage) AddConsoleTab();
            };

            _addTabPage = new TabPage("+");
            _tabControl.TabPages.Add(_addTabPage);

            Controls.Add(_tabControl);
            Controls.Add(toolStrip);
            Controls.Add(_statusStrip);

            Load += (s, e) =>
            {
                AddConsoleTab();
                _uiReady = true;
            };
        }

        // ── 탭 제목 관리 (닫기 버튼 자리 확보를 위해 실제 표시 Text에는 여백을 붙여둠) ──

        private static void SetTabTitle(TabPage page, string title)
        {
            page.Tag = title;
            page.Text = title + "      ";
        }

        private static string GetTabTitle(TabPage page) => page.Tag as string ?? page.Text.TrimEnd();

        // ── 탭 직접 그리기: "+" 탭은 가운데 정렬, 일반 탭은 오른쪽에 x 닫기 버튼 ──

        private void TabControl_DrawItem(object? sender, DrawItemEventArgs e)
        {
            var tab = _tabControl.TabPages[e.Index];
            var rect = e.Bounds;
            bool isAddTab = tab == _addTabPage;
            bool selected = e.Index == _tabControl.SelectedIndex;

            using (var backBrush = new SolidBrush(selected ? SystemColors.Window : SystemColors.Control))
                e.Graphics.FillRectangle(backBrush, rect);

            if (isAddTab)
            {
                using var boldFont = new Font(_tabControl.Font, FontStyle.Bold);
                TextRenderer.DrawText(e.Graphics, "+", boldFont, rect, SystemColors.ControlText,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
            else
            {
                string title = GetTabTitle(tab);
                var closeRect = GetCloseButtonRect(rect);
                var textRect = new Rectangle(rect.Left + 8, rect.Top, Math.Max(0, closeRect.Left - rect.Left - 8), rect.Height);

                TextRenderer.DrawText(e.Graphics, title, _tabControl.Font, textRect, SystemColors.ControlText,
                    TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine | TextFormatFlags.EndEllipsis);

                bool hoverClose = e.Index == _hoverCloseIndex;
                if (hoverClose)
                {
                    using var hoverBrush = new SolidBrush(Color.FromArgb(232, 17, 35));
                    e.Graphics.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
                    e.Graphics.FillEllipse(hoverBrush, closeRect);
                }

                using var xFont = new Font(_tabControl.Font.FontFamily, 10f, FontStyle.Bold);
                var xColor = hoverClose ? Color.White : Color.DimGray;
                TextRenderer.DrawText(e.Graphics, "×", xFont, closeRect, xColor,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine);
            }
        }

        private static Rectangle GetCloseButtonRect(Rectangle tabRect)
        {
            int top = tabRect.Top + (tabRect.Height - CloseButtonSize) / 2;
            int left = tabRect.Right - CloseButtonSize - 6;
            return new Rectangle(left, top, CloseButtonSize, CloseButtonSize);
        }

        private void TabControl_MouseMove(object? sender, MouseEventArgs e)
        {
            int newHover = -1;
            for (int i = 0; i < _tabControl.TabPages.Count; i++)
            {
                var tab = _tabControl.TabPages[i];
                if (tab == _addTabPage) continue;

                var rect = _tabControl.GetTabRect(i);
                var closeRect = GetCloseButtonRect(rect);
                if (closeRect.Contains(e.Location))
                {
                    newHover = i;
                    break;
                }
            }
            if (newHover != _hoverCloseIndex)
            {
                _hoverCloseIndex = newHover;
                _tabControl.Invalidate();
            }
        }

        private void TabControl_MouseUp(object? sender, MouseEventArgs e)
        {
            if (e.Button != MouseButtons.Left) return;
            for (int i = 0; i < _tabControl.TabPages.Count; i++)
            {
                var tab = _tabControl.TabPages[i];
                if (tab == _addTabPage) continue;

                var rect = _tabControl.GetTabRect(i);
                var closeRect = GetCloseButtonRect(rect);
                if (closeRect.Contains(e.Location))
                {
                    CloseTab(tab);
                    break;
                }
            }
        }

        private void AddConsoleTab()
        {
            _consoleCounter++;
            var page = new TabPage();
            SetTabTitle(page, $"콘솔 {_consoleCounter}");

            var connectPanel = BuildConnectPanel(page);
            page.Controls.Add(connectPanel);

            var menu = new ContextMenuStrip();
            var closeItem = new ToolStripMenuItem("닫기");
            closeItem.Click += (s, e) => CloseTab(page);
            menu.Items.Add(closeItem);
            page.ContextMenuStrip = menu;

            int insertIndex = _tabControl.TabPages.IndexOf(_addTabPage);
            _tabControl.TabPages.Insert(insertIndex, page);
            _tabControl.SelectedTab = page;
        }

        private Control BuildConnectPanel(TabPage page)
        {
            var panel = new Panel { Dock = DockStyle.Fill, BackColor = Color.WhiteSmoke };

            var label = new Label { Text = "접속할 서버를 선택하세요", AutoSize = true, Left = 24, Top = 20 };
            var combo = new ComboBox { Left = 24, Top = 46, Width = 320, DropDownStyle = ComboBoxStyle.DropDownList };
            RefreshProfileCombo(combo);

            var connectBtn = new Button { Text = "연결", Left = 354, Top = 43, Width = 80, Height = 32 };
            connectBtn.Click += (s, e) =>
            {
                if (combo.SelectedItem is ServerProfile profile)
                    ConnectTab(page, panel, profile);
                else
                    MessageBox.Show(this, "먼저 서버를 선택하세요.", "알림", MessageBoxButtons.OK, MessageBoxIcon.Information);
            };

            var manageBtn = new Button { Text = "서버 관리", Left = 444, Top = 43, Width = 100, Height = 32 };
            manageBtn.Click += (s, e) =>
            {
                OpenServerManager();
                RefreshProfileCombo(combo);
            };

            panel.Controls.Add(label);
            panel.Controls.Add(combo);
            panel.Controls.Add(connectBtn);
            panel.Controls.Add(manageBtn);

            return panel;
        }

        private void RefreshProfileCombo(ComboBox combo)
        {
            var profiles = _store.Load();
            combo.DataSource = null;
            combo.DisplayMember = "DisplayName";
            combo.DataSource = profiles;
        }

        private void ConnectTab(TabPage page, Control connectPanel, ServerProfile profile)
        {
            page.Controls.Remove(connectPanel);
            connectPanel.Dispose();

            var terminal = new TerminalControl { Dock = DockStyle.Fill };
            terminal.SetFontSize(_currentFontSize);
            terminal.StatusChanged += msg => SetStatus($"[{GetTabTitle(page)}] {msg}");
            terminal.Disconnected += () => SetStatus($"[{GetTabTitle(page)}] 연결이 종료되었습니다.");

            page.Controls.Add(terminal);
            SetTabTitle(page, $"{GetTabTitle(page)} - {profile.Name}");

            try
            {
                terminal.Connect(profile);
                terminal.Focus();
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, $"연결 실패: {ex.Message}", "오류", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
        }

        private void ChangeFontSize(float delta)
        {
            _currentFontSize = Math.Max(7f, Math.Min(28f, _currentFontSize + delta));
            foreach (TabPage page in _tabControl.TabPages)
            {
                foreach (Control c in page.Controls)
                {
                    if (c is TerminalControl term) term.SetFontSize(_currentFontSize);
                }
            }
            SetStatus($"글자 크기: {_currentFontSize:0}pt");
        }

        private void CloseCurrentTab()
        {
            if (_tabControl.SelectedTab is TabPage page && page != _addTabPage) CloseTab(page);
        }

        private void CloseTab(TabPage page)
        {
            foreach (Control c in page.Controls)
            {
                if (c is TerminalControl term) term.Disconnect();
            }
            _tabControl.TabPages.Remove(page);
            page.Dispose();
        }

        private void OpenServerManager()
        {
            using var form = new ServerManageForm(_store);
            form.ShowDialog(this);
        }

        private void SetStatus(string text)
        {
            if (InvokeRequired) { BeginInvoke(new Action(() => SetStatus(text))); return; }
            _statusLabel.Text = text;
        }
    }
}
