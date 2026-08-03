using System;
using System.Collections.Generic;
using System.Drawing;
using System.Linq;
using System.Text;

namespace SshTabClient.Terminal
{
    /// <summary>
    /// 단순화된 VT100/ANSI 터미널 에뮬레이터.
    /// 커서 이동, 화면/줄 지우기, SGR(색상) 정도를 지원하여
    /// vim/nano 같은 화면 그리기 프로그램이 기본적으로 동작하게 합니다.
    /// 완전한 xterm 호환은 아닙니다.
    /// </summary>
    public class TerminalEmulator
    {
        public int Cols { get; private set; }
        public int Rows { get; private set; }
        public TermCell[,] Buffer { get; private set; }
        public int CursorRow { get; private set; }
        public int CursorCol { get; private set; }
        public bool CursorVisible { get; private set; } = true;

        /// <summary>화면 위로 밀려 올라간 과거 줄들 (스크롤바로 거슬러 볼 수 있음)</summary>
        public List<TermCell[]> Scrollback { get; } = new();
        private const int MaxScrollbackLines = 3000;

        public event Action? Updated;

        private Color _curFore = TerminalColors.DefaultFore;
        private Color _curBack = TerminalColors.DefaultBack;
        private bool _curBold;

        private enum ParseState { Normal, Escape, Csi, OscOrOther }
        private ParseState _state = ParseState.Normal;
        private readonly StringBuilder _paramBuf = new();

        private int _savedRow, _savedCol;

        public TerminalEmulator(int cols, int rows)
        {
            Cols = Math.Max(1, cols);
            Rows = Math.Max(1, rows);
            Buffer = new TermCell[Rows, Cols];
            Clear();
        }

        public void Resize(int cols, int rows)
        {
            cols = Math.Max(1, cols);
            rows = Math.Max(1, rows);
            var newBuf = new TermCell[rows, cols];
            for (int r = 0; r < rows; r++)
                for (int c = 0; c < cols; c++)
                    newBuf[r, c] = TermCell.Default;

            int copyRows = Math.Min(rows, Rows);
            int copyCols = Math.Min(cols, Cols);
            for (int r = 0; r < copyRows; r++)
                for (int c = 0; c < copyCols; c++)
                    newBuf[r, c] = Buffer[r, c];

            Buffer = newBuf;
            Cols = cols;
            Rows = rows;
            CursorRow = Math.Min(CursorRow, Rows - 1);
            CursorCol = Math.Min(CursorCol, Cols - 1);
            Scrollback.Clear(); // 폭이 바뀌면 이전 줄들을 그대로 재사용할 수 없어 비웁니다
            Updated?.Invoke();
        }

        public void Clear()
        {
            for (int r = 0; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    Buffer[r, c] = TermCell.Default;
            CursorRow = 0;
            CursorCol = 0;
            Updated?.Invoke();
        }

        private readonly Decoder _utf8Decoder = Encoding.UTF8.GetDecoder();

        public void Feed(byte[] data, int count)
        {
            // SSH 스트림은 UTF-8 바이트로 들어오는데, 한글은 한 글자가 3바이트라
            // 네트워크 수신 단위 경계에서 문자 중간이 잘릴 수 있습니다.
            // Decoder는 그 상태를 기억했다가 다음 Feed 호출에서 이어붙여 줍니다.
            int charCount = _utf8Decoder.GetCharCount(data, 0, count);
            if (charCount == 0) return;
            var chars = new char[charCount];
            int actual = _utf8Decoder.GetChars(data, 0, count, chars, 0);
            for (int i = 0; i < actual; i++)
                ProcessByte(chars[i]);
            Updated?.Invoke();
        }

        private void ProcessByte(char ch)
        {
            switch (_state)
            {
                case ParseState.Normal: HandleNormal(ch); break;
                case ParseState.Escape: HandleEscape(ch); break;
                case ParseState.Csi: HandleCsi(ch); break;
                case ParseState.OscOrOther:
                    if (ch == '\u0007' || ch == '\u001b') _state = ParseState.Normal;
                    break;
            }
        }

        private void HandleNormal(char ch)
        {
            switch (ch)
            {
                case '\u001b':
                    _state = ParseState.Escape;
                    _paramBuf.Clear();
                    break;
                case '\r':
                    CursorCol = 0;
                    break;
                case '\n':
                    LineFeed();
                    break;
                case '\b':
                    if (CursorCol > 0) CursorCol--;
                    break;
                case '\t':
                    int next = ((CursorCol / 8) + 1) * 8;
                    CursorCol = Math.Min(next, Cols - 1);
                    break;
                case '\u0007':
                    break;
                default:
                    if (ch >= ' ') PutChar(ch);
                    break;
            }
        }

        private void PutChar(char ch)
        {
            if (CursorCol >= Cols)
            {
                CursorCol = 0;
                LineFeed();
            }

            if (WideCharUtil.IsWide(ch))
            {
                if (CursorCol >= Cols - 1)
                {
                    // 줄 끝에 딱 걸리면 다음 줄로 넘겨서 글자가 반토막 나지 않게 함
                    CursorCol = 0;
                    LineFeed();
                }
                Buffer[CursorRow, CursorCol] = new TermCell { Ch = ch, Fore = _curFore, Back = _curBack, Bold = _curBold, IsWide = true };
                CursorCol++;
                // 다음 칸은 이 문자의 나머지 폭을 차지하는 자리이므로 빈 값으로 채움
                Buffer[CursorRow, CursorCol] = new TermCell { Ch = '\0', Fore = _curFore, Back = _curBack, Bold = _curBold };
                CursorCol++;
            }
            else
            {
                Buffer[CursorRow, CursorCol] = new TermCell { Ch = ch, Fore = _curFore, Back = _curBack, Bold = _curBold };
                CursorCol++;
            }
        }

        private void LineFeed()
        {
            if (CursorRow == Rows - 1) ScrollUp();
            else CursorRow++;
        }

        private void ScrollUp()
        {
            var savedLine = new TermCell[Cols];
            for (int c = 0; c < Cols; c++) savedLine[c] = Buffer[0, c];
            Scrollback.Add(savedLine);
            if (Scrollback.Count > MaxScrollbackLines) Scrollback.RemoveAt(0);

            for (int r = 1; r < Rows; r++)
                for (int c = 0; c < Cols; c++)
                    Buffer[r - 1, c] = Buffer[r, c];
            for (int c = 0; c < Cols; c++)
                Buffer[Rows - 1, c] = TermCell.Default;
        }

        private void ScrollDown()
        {
            for (int r = Rows - 1; r > 0; r--)
                for (int c = 0; c < Cols; c++)
                    Buffer[r, c] = Buffer[r - 1, c];
            for (int c = 0; c < Cols; c++)
                Buffer[0, c] = TermCell.Default;
        }

        private void HandleEscape(char ch)
        {
            if (ch == '[')
            {
                _state = ParseState.Csi;
                _paramBuf.Clear();
                return;
            }
            if (ch == ']')
            {
                _state = ParseState.OscOrOther; // 창 제목 설정(OSC) 등은 무시
                return;
            }
            switch (ch)
            {
                case '7': _savedRow = CursorRow; _savedCol = CursorCol; break;
                case '8': CursorRow = _savedRow; CursorCol = _savedCol; break;
                case 'M': if (CursorRow == 0) ScrollDown(); else CursorRow--; break;
                default: break; // 알 수 없는 시퀀스는 무시
            }
            _state = ParseState.Normal;
        }

        private void HandleCsi(char ch)
        {
            if (ch == ';' || ch == '?' || (ch >= '0' && ch <= '9'))
            {
                _paramBuf.Append(ch);
                return;
            }

            var raw = _paramBuf.ToString();
            bool isPrivate = raw.StartsWith("?");
            var numStr = isPrivate ? raw.Substring(1) : raw;
            var parts = numStr.Length == 0
                ? Array.Empty<int>()
                : numStr.Split(';').Select(s => s.Length == 0 ? 0 : (int.TryParse(s, out var v) ? v : 0)).ToArray();

            int P(int idx, int def = 0) => idx < parts.Length ? (parts[idx] == 0 ? def : parts[idx]) : def;

            switch (ch)
            {
                case 'A': CursorRow = Math.Max(0, CursorRow - P(0, 1)); break;
                case 'B': CursorRow = Math.Min(Rows - 1, CursorRow + P(0, 1)); break;
                case 'C': CursorCol = Math.Min(Cols - 1, CursorCol + P(0, 1)); break;
                case 'D': CursorCol = Math.Max(0, CursorCol - P(0, 1)); break;
                case 'H':
                case 'f':
                {
                    int row = parts.Length > 0 ? parts[0] : 1;
                    int col = parts.Length > 1 ? parts[1] : 1;
                    if (row == 0) row = 1;
                    if (col == 0) col = 1;
                    CursorRow = Math.Min(Rows - 1, Math.Max(0, row - 1));
                    CursorCol = Math.Min(Cols - 1, Math.Max(0, col - 1));
                    break;
                }
                case 'J': EraseInDisplay(parts.Length > 0 ? parts[0] : 0); break;
                case 'K': EraseInLine(parts.Length > 0 ? parts[0] : 0); break;
                case 'm': ApplySgr(parts); break;
                case 'h':
                case 'l':
                    if (isPrivate && parts.Length > 0 && parts[0] == 25)
                        CursorVisible = ch == 'h';
                    break;
                default: break; // 나머지 CSI 시퀀스는 무시
            }

            _state = ParseState.Normal;
        }

        private void EraseInDisplay(int mode)
        {
            switch (mode)
            {
                case 0:
                    EraseInLine(0);
                    for (int r = CursorRow + 1; r < Rows; r++)
                        for (int c = 0; c < Cols; c++)
                            Buffer[r, c] = TermCell.Default;
                    break;
                case 1:
                    EraseInLine(1);
                    for (int r = 0; r < CursorRow; r++)
                        for (int c = 0; c < Cols; c++)
                            Buffer[r, c] = TermCell.Default;
                    break;
                case 2:
                case 3:
                    for (int r = 0; r < Rows; r++)
                        for (int c = 0; c < Cols; c++)
                            Buffer[r, c] = TermCell.Default;
                    break;
            }
        }

        private void EraseInLine(int mode)
        {
            switch (mode)
            {
                case 0:
                    for (int c = CursorCol; c < Cols; c++) Buffer[CursorRow, c] = TermCell.Default;
                    break;
                case 1:
                    for (int c = 0; c <= CursorCol && c < Cols; c++) Buffer[CursorRow, c] = TermCell.Default;
                    break;
                case 2:
                    for (int c = 0; c < Cols; c++) Buffer[CursorRow, c] = TermCell.Default;
                    break;
            }
        }

        private void ApplySgr(int[] parts)
        {
            if (parts.Length == 0)
            {
                _curFore = TerminalColors.DefaultFore;
                _curBack = TerminalColors.DefaultBack;
                _curBold = false;
                return;
            }

            foreach (var p in parts)
            {
                if (p == 0) { _curFore = TerminalColors.DefaultFore; _curBack = TerminalColors.DefaultBack; _curBold = false; }
                else if (p == 1) _curBold = true;
                else if (p == 22) _curBold = false;
                else if (p >= 30 && p <= 37) _curFore = TerminalColors.Palette[p - 30 + (_curBold ? 8 : 0)];
                else if (p == 39) _curFore = TerminalColors.DefaultFore;
                else if (p >= 40 && p <= 47) _curBack = TerminalColors.Palette[p - 40];
                else if (p == 49) _curBack = TerminalColors.DefaultBack;
                else if (p >= 90 && p <= 97) _curFore = TerminalColors.Palette[p - 90 + 8];
                else if (p >= 100 && p <= 107) _curBack = TerminalColors.Palette[p - 100 + 8];
                // 38/48(256색·RGB)은 미지원 - 필요시 확장 가능
            }
        }
    }
}
