using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Renci.SshNet;
using SshTabClient.Models;
using SshTabClient.Services;

namespace SshTabClient.Terminal
{
    public class TerminalControl : Control
    {
        private const float MinFontSize = 7f;
        private const float MaxFontSize = 28f;

        private TerminalEmulator _emulator;
        private Font _font;
        private Font _koreanFont;
        private SizeF _charSize;
        private float _fontSize = 11f;

        private readonly VScrollBar _scrollBar;
        private bool _pinnedToBottom = true;

        private SshClient? _sshClient;
        private ShellStream? _shell;
        private Thread? _readThread;
        private volatile bool _running;

        // 마우스 드래그 텍스트 선택 (row, col) - 현재 화면(뷰포트) 기준 좌표
        private (int row, int col)? _selStart;
        private (int row, int col)? _selEnd;
        private bool _selecting;
        private Point _mouseDownPixel;

        public event Action<string>? StatusChanged;
        public event Action? Disconnected;

        public TerminalControl()
        {
            SetStyle(ControlStyles.AllPaintingInWmPaint | ControlStyles.UserPaint |
                      ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw |
                      ControlStyles.Selectable, true);
            TabStop = true;
            BackColor = Color.Black;

            _font = new Font("Consolas", _fontSize, FontStyle.Regular, GraphicsUnit.Point);
            _koreanFont = CreateKoreanFont(_fontSize);
            using (var g = CreateGraphics())
            {
                _charSize = g.MeasureString("W", _font, int.MaxValue, StringFormat.GenericTypographic);
            }

            _emulator = new TerminalEmulator(80, 24);
            HookEmulator();
            BuildContextMenu();

            _scrollBar = new VScrollBar { Dock = DockStyle.Right, Width = 17, Minimum = 0, Maximum = 0 };
            _scrollBar.Scroll += (s, e) =>
            {
                _pinnedToBottom = _scrollBar.Value >= _emulator.Scrollback.Count;
                Invalidate();
            };
            Controls.Add(_scrollBar);
        }

        public bool IsConnected => _sshClient?.IsConnected == true;

        public float FontSize => _fontSize;

        /// <summary>
        /// "맑은 고딕" 등 한글 폰트 이름은 Windows 로캘에 따라 다르게 인식될 수 있어,
        /// 여러 후보 이름을 순서대로 시도해서 실제로 설치된 폰트를 찾습니다.
        /// new Font(이름, ...)는 이름을 못 찾아도 예외 없이 다른 폰트로 조용히
        /// 대체되기 때문에(한글이 계속 안 보이는 원인이 될 수 있음), FontFamily로
        /// 먼저 존재 여부를 확인합니다.
        /// </summary>
        private static Font CreateKoreanFont(float size)
        {
            string[] candidates = { "Malgun Gothic", "맑은 고딕", "굴림", "Gulim", "바탕", "Batang", "Dotum" };
            foreach (var name in candidates)
            {
                try
                {
                    using var fam = new FontFamily(name);
                    return new Font(fam, size, FontStyle.Regular, GraphicsUnit.Point);
                }
                catch
                {
                    // 이 이름의 폰트가 없음 - 다음 후보 시도
                }
            }
            // 마지막 수단: 시스템 기본 UI 폰트 (이 경우도 한글이 없다면 시스템 자체에
            // 한글 폰트가 전혀 설치되어 있지 않은 것입니다)
            return new Font(SystemFonts.DefaultFont.FontFamily, size, FontStyle.Regular, GraphicsUnit.Point);
        }

        private void HookEmulator()
        {
            _emulator.Updated += () =>
            {
                if (IsHandleCreated) BeginInvoke(new Action(UpdateScrollBar));
            };
        }

        private void BuildContextMenu()
        {
            var menu = new ContextMenuStrip();
            var copyItem = new ToolStripMenuItem("복사 (Ctrl+Shift+C)");
            copyItem.Click += (s, e) => CopySelectionToClipboard();
            var pasteItem = new ToolStripMenuItem("붙여넣기 (Ctrl+Shift+V)");
            pasteItem.Click += (s, e) => PasteFromClipboard();
            menu.Items.Add(copyItem);
            menu.Items.Add(pasteItem);
            ContextMenuStrip = menu;
        }

        public void SetFontSize(float size)
        {
            size = Math.Max(MinFontSize, Math.Min(MaxFontSize, size));
            if (Math.Abs(size - _fontSize) < 0.01f) return;

            _fontSize = size;
            var old = _font;
            var oldKr = _koreanFont;
            _font = new Font("Consolas", _fontSize, FontStyle.Regular, GraphicsUnit.Point);
            _koreanFont = CreateKoreanFont(_fontSize);
            old.Dispose();
            oldKr.Dispose();

            using (var g = CreateGraphics())
            {
                _charSize = g.MeasureString("W", _font, int.MaxValue, StringFormat.GenericTypographic);
            }

            if (_shell != null)
            {
                var (cols, rows) = ComputeGridSize();
                _emulator.Resize(cols, rows);
            }

            Invalidate();
        }

        public void Connect(ServerProfile profile)
        {
            Disconnect();

            var authMethods = new List<AuthenticationMethod>();

            if (profile.AuthType == AuthType.Password)
            {
                var pwd = CredentialProtector.Unprotect(profile.EncryptedPassword) ?? "";
                authMethods.Add(new PasswordAuthenticationMethod(profile.Username, pwd));
            }
            else
            {
                var passphrase = CredentialProtector.Unprotect(profile.EncryptedPassphrase) ?? "";
                var keyFile = string.IsNullOrEmpty(passphrase)
                    ? new PrivateKeyFile(profile.KeyFilePath)
                    : new PrivateKeyFile(profile.KeyFilePath, passphrase);
                authMethods.Add(new PrivateKeyAuthenticationMethod(profile.Username, keyFile));
            }

            var connInfo = new ConnectionInfo(profile.Host, profile.Port, profile.Username, authMethods.ToArray())
            {
                Timeout = TimeSpan.FromSeconds(15)
            };

            _sshClient = new SshClient(connInfo);
            _sshClient.Connect();

            var (cols, rows) = ComputeGridSize();
            _emulator = new TerminalEmulator(cols, rows);
            HookEmulator();
            _pinnedToBottom = true;

            _shell = _sshClient.CreateShellStream("xterm-256color", (uint)cols, (uint)rows, 800, 600, 4096);

            _running = true;
            _readThread = new Thread(ReadLoop) { IsBackground = true };
            _readThread.Start();

            StatusChanged?.Invoke($"{profile.DisplayName} 연결됨");
            UpdateScrollBar();
            Invalidate();
        }

        public void Disconnect()
        {
            _running = false;
            try { _shell?.Dispose(); } catch { /* 무시 */ }
            try { _sshClient?.Disconnect(); } catch { /* 무시 */ }
            try { _sshClient?.Dispose(); } catch { /* 무시 */ }
            _shell = null;
            _sshClient = null;
        }

        private void ReadLoop()
        {
            var buf = new byte[4096];
            try
            {
                while (_running && _shell != null)
                {
                    int n = _shell.Read(buf, 0, buf.Length);
                    if (n <= 0)
                    {
                        if (!_shell.CanRead) break;
                        Thread.Sleep(10);
                        continue;
                    }
                    _emulator.Feed(buf, n);
                }
            }
            catch
            {
                // 연결 종료 등 - 아래 finally 에서 상태 정리
            }
            finally
            {
                _running = false;
                if (IsHandleCreated)
                {
                    BeginInvoke(new Action(() =>
                    {
                        StatusChanged?.Invoke("연결 종료됨");
                        Disconnected?.Invoke();
                    }));
                }
            }
        }

        private (int cols, int rows) ComputeGridSize()
        {
            int availWidth = Math.Max(0, ClientSize.Width - _scrollBar.Width);
            int cols = Math.Max(20, (int)(availWidth / _charSize.Width));
            int rows = Math.Max(5, (int)(ClientSize.Height / _charSize.Height));
            return (cols, rows);
        }

        protected override void OnResize(EventArgs e)
        {
            base.OnResize(e);
            if (_shell == null) { Invalidate(); return; }

            var (cols, rows) = ComputeGridSize();
            if (cols != _emulator.Cols || rows != _emulator.Rows)
            {
                // 참고: 설치된 SSH.NET 버전에 따라 ShellStream이 실시간 창 크기
                // 변경 요청을 지원하지 않을 수 있어 로컬 화면 크기만 다시
                // 맞춥니다. 원격 PTY 크기는 접속 시점 크기로 유지됩니다.
                _emulator.Resize(cols, rows);
            }
            UpdateScrollBar();
            Invalidate();
        }

        // ── 스크롤백 / 스크롤바 ──

        private void UpdateScrollBar()
        {
            int scrollbackCount = _emulator.Scrollback.Count;
            int visible = Math.Max(1, _emulator.Rows);

            _scrollBar.Minimum = 0;
            _scrollBar.LargeChange = visible;
            _scrollBar.SmallChange = 1;
            _scrollBar.Maximum = scrollbackCount + visible - 1;

            if (_pinnedToBottom)
            {
                _scrollBar.Value = scrollbackCount;
            }
            else
            {
                int maxReachable = Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
                _scrollBar.Value = Math.Min(_scrollBar.Value, maxReachable);
            }

            Invalidate();
        }

        private int CurrentScrollBackLines() => Math.Max(0, _emulator.Scrollback.Count - _scrollBar.Value);

        private TermCell GetCell(int viewportRow, int col, int scrollBackLines)
        {
            var em = _emulator;
            int combinedIndex = (em.Scrollback.Count - scrollBackLines) + viewportRow;

            if (combinedIndex >= 0 && combinedIndex < em.Scrollback.Count)
            {
                var line = em.Scrollback[combinedIndex];
                return col < line.Length ? line[col] : TermCell.Default;
            }

            int bufRow = combinedIndex - em.Scrollback.Count;
            if (bufRow < 0 || bufRow >= em.Rows) return TermCell.Default;
            return em.Buffer[bufRow, col];
        }

        private void ScrollLines(int linesTowardOlder)
        {
            int maxReachable = Math.Max(0, _scrollBar.Maximum - _scrollBar.LargeChange + 1);
            int newValue = Math.Max(_scrollBar.Minimum, Math.Min(maxReachable, _scrollBar.Value - linesTowardOlder));
            if (newValue == _scrollBar.Value) return;

            _scrollBar.Value = newValue;
            _pinnedToBottom = newValue >= _emulator.Scrollback.Count;
            Invalidate();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            int notches = e.Delta / 120;
            if (notches == 0) return;
            ScrollLines(notches * 3);
        }

        // ── 그리기 ──

        protected override void OnPaint(PaintEventArgs e)
        {
            var g = e.Graphics;
            g.TextRenderingHint = System.Drawing.Text.TextRenderingHint.SingleBitPerPixelGridFit;
            g.Clear(TerminalColors.DefaultBack);

            var em = _emulator;
            int scrollBackLines = CurrentScrollBackLines();
            bool viewingLive = scrollBackLines == 0;
            bool hasSel = TryGetNormalizedSelection(out var selStart, out var selEnd);

            for (int r = 0; r < em.Rows; r++)
            {
                for (int c = 0; c < em.Cols; c++)
                {
                    var cell = GetCell(r, c, scrollBackLines);
                    float x = c * _charSize.Width;
                    float y = r * _charSize.Height;

                    bool isCursor = viewingLive && em.CursorVisible && r == em.CursorRow && c == em.CursorCol && Focused;
                    bool isSelected = hasSel && IsCellSelected(r, c, selStart, selEnd);

                    Color back;
                    Color fore;
                    if (isCursor)
                    {
                        back = cell.Fore;
                        fore = cell.Back;
                    }
                    else if (isSelected)
                    {
                        back = Color.FromArgb(80, 130, 200);
                        fore = Color.White;
                    }
                    else
                    {
                        back = cell.Back;
                        fore = cell.Fore;
                    }

                    if (back != TerminalColors.DefaultBack || isCursor || isSelected)
                    {
                        using var b = new SolidBrush(back);
                        g.FillRectangle(b, x, y, _charSize.Width + 1, _charSize.Height + 1);
                    }

                    if (cell.Ch != ' ' && cell.Ch != '\0')
                    {
                        bool isKorean = WideCharUtil.NeedsKoreanFont(cell.Ch);
                        Font baseFont = isKorean ? _koreanFont : _font;
                        Font font = cell.Bold ? new Font(baseFont, FontStyle.Bold) : baseFont;
                        using var b = new SolidBrush(fore);

                        // 한글 등 2칸 문자는 다음 칸이 비어있으므로 자연스럽게 그 폭까지 채워짐 -
                        // 사각형+가운데정렬 방식은 일부 환경에서 글자가 아예 그려지지 않는
                        // 문제가 있어 일반 좌표 기반으로 단순하게 그립니다.
                        g.DrawString(cell.Ch.ToString(), font, b, x, y, StringFormat.GenericTypographic);

                        if (cell.Bold) font.Dispose();
                    }
                }
            }
        }

        protected override bool IsInputKey(Keys keyData) => true;

        protected override void OnKeyDown(KeyEventArgs e)
        {
            base.OnKeyDown(e);
            if (_shell == null) return;

            if (e.Control && e.Shift && e.KeyCode == Keys.C)
            {
                CopySelectionToClipboard();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if ((e.Control && e.Shift && e.KeyCode == Keys.V) || (e.Shift && e.KeyCode == Keys.Insert))
            {
                PasteFromClipboard();
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Shift && e.KeyCode == Keys.PageUp)
            {
                ScrollLines(Math.Max(1, _emulator.Rows - 1));
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }
            if (e.Shift && e.KeyCode == Keys.PageDown)
            {
                ScrollLines(-Math.Max(1, _emulator.Rows - 1));
                e.Handled = true;
                e.SuppressKeyPress = true;
                return;
            }

            byte[]? seq = e.KeyCode switch
            {
                Keys.Up => Bytes("\u001b[A"),
                Keys.Down => Bytes("\u001b[B"),
                Keys.Right => Bytes("\u001b[C"),
                Keys.Left => Bytes("\u001b[D"),
                Keys.Home => Bytes("\u001b[H"),
                Keys.End => Bytes("\u001b[F"),
                Keys.Delete => Bytes("\u001b[3~"),
                Keys.PageUp => Bytes("\u001b[5~"),
                Keys.PageDown => Bytes("\u001b[6~"),
                Keys.Tab => Bytes("\t"),
                Keys.Back => Bytes("\u007f"),
                Keys.Escape => Bytes("\u001b"),
                Keys.Enter => Bytes("\r"),
                _ => null
            };

            if (e.Control && !e.Alt && !e.Shift && e.KeyCode >= Keys.A && e.KeyCode <= Keys.Z)
            {
                int code = (int)e.KeyCode - (int)Keys.A + 1;
                seq = new[] { (byte)code };
            }

            if (seq != null)
            {
                Send(seq);
                e.Handled = true;
                e.SuppressKeyPress = true;
            }
        }

        protected override void OnKeyPress(KeyPressEventArgs e)
        {
            base.OnKeyPress(e);
            if (_shell == null) return;
            if (e.KeyChar >= ' ')
            {
                Send(Bytes(e.KeyChar.ToString()));
                e.Handled = true;
            }
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            Focus();
            if (e.Button == MouseButtons.Left)
            {
                _selecting = true;
                _mouseDownPixel = e.Location;
                _selStart = PixelToCell(e.Location);
                _selEnd = _selStart;
                Invalidate();
            }
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (_selecting)
            {
                _selEnd = PixelToCell(e.Location);
                Invalidate();
            }
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (e.Button != MouseButtons.Left) return;
            _selecting = false;

            int dx = Math.Abs(e.Location.X - _mouseDownPixel.X);
            int dy = Math.Abs(e.Location.Y - _mouseDownPixel.Y);
            if (dx < 3 && dy < 3)
            {
                // 드래그 없이 그냥 클릭한 경우 - 선택 해제
                _selStart = null;
                _selEnd = null;
                Invalidate();
            }
        }

        private (int row, int col) PixelToCell(Point p)
        {
            int col = (int)(p.X / _charSize.Width);
            int row = (int)(p.Y / _charSize.Height);
            col = Math.Max(0, Math.Min(_emulator.Cols - 1, col));
            row = Math.Max(0, Math.Min(_emulator.Rows - 1, row));
            return (row, col);
        }

        private bool TryGetNormalizedSelection(out (int row, int col) start, out (int row, int col) end)
        {
            start = default;
            end = default;
            if (_selStart == null || _selEnd == null) return false;

            var a = _selStart.Value;
            var b = _selEnd.Value;
            if (a.row > b.row || (a.row == b.row && a.col > b.col))
                (a, b) = (b, a);

            start = a;
            end = b;
            return true;
        }

        private static bool IsCellSelected(int row, int col, (int row, int col) start, (int row, int col) end)
        {
            if (row < start.row || row > end.row) return false;
            if (row == start.row && col < start.col) return false;
            if (row == end.row && col > end.col) return false;
            return true;
        }

        public void CopySelectionToClipboard()
        {
            if (!TryGetNormalizedSelection(out var start, out var end)) return;

            int scrollBackLines = CurrentScrollBackLines();
            var sb = new StringBuilder();
            for (int r = start.row; r <= end.row; r++)
            {
                int colFrom = r == start.row ? start.col : 0;
                int colTo = r == end.row ? end.col : _emulator.Cols - 1;

                var line = new StringBuilder();
                for (int c = colFrom; c <= colTo && c < _emulator.Cols; c++)
                {
                    char ch = GetCell(r, c, scrollBackLines).Ch;
                    if (ch != '\0') line.Append(ch);
                }

                sb.Append(line.ToString().TrimEnd());
                if (r != end.row) sb.Append(Environment.NewLine);
            }

            var text = sb.ToString();
            if (string.IsNullOrEmpty(text)) return;
            try { Clipboard.SetText(text); } catch { /* 클립보드 접근 실패는 무시 */ }
        }

        public void PasteFromClipboard()
        {
            if (_shell == null) return;
            string text;
            try { text = Clipboard.GetText(); } catch { return; }
            if (string.IsNullOrEmpty(text)) return;

            // 셸에서는 엔터를 캐리지리턴(\r)으로 받는 경우가 대부분이라 개행을 통일합니다.
            text = text.Replace("\r\n", "\r").Replace("\n", "\r");
            Send(Bytes(text));
        }

        private static byte[] Bytes(string s) => Encoding.UTF8.GetBytes(s);

        private void Send(byte[] data)
        {
            if (_selStart != null || _selEnd != null)
            {
                _selStart = null;
                _selEnd = null;
            }
            if (!_pinnedToBottom)
            {
                // 스크롤을 올려서 과거 기록을 보던 중 타이핑을 하면 최신 화면으로 돌아옴
                _pinnedToBottom = true;
                UpdateScrollBar();
            }
            Invalidate();

            try { _shell?.Write(data, 0, data.Length); _shell?.Flush(); }
            catch { /* 무시 */ }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                Disconnect();
                _font.Dispose();
                _koreanFont.Dispose();
            }
            base.Dispose(disposing);
        }
    }
}
