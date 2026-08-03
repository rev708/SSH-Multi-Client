using System.Drawing;

namespace SshTabClient.Terminal
{
    public struct TermCell
    {
        public char Ch;
        public Color Fore;
        public Color Back;
        public bool Bold;
        public bool IsWide; // 한글/한자 등 2칸을 차지하는 문자의 첫 칸이면 true

        public static TermCell Default => new TermCell
        {
            Ch = ' ',
            Fore = TerminalColors.DefaultFore,
            Back = TerminalColors.DefaultBack,
            Bold = false,
            IsWide = false
        };
    }

    /// <summary>
    /// 한글/한자 등 터미널에서 2칸 폭으로 취급되는 문자인지, 그리고
    /// Consolas가 지원하지 않아 한글 폰트로 그려야 하는 문자인지 판별합니다.
    /// </summary>
    public static class WideCharUtil
    {
        public static bool IsWide(char ch)
        {
            int c = ch;
            return (c >= 0x1100 && c <= 0x115F)   // 한글 자모
                || (c >= 0x2E80 && c <= 0xA4CF)    // CJK 부수~이 문자 등
                || (c >= 0xAC00 && c <= 0xD7A3)    // 한글 완성형 음절
                || (c >= 0xF900 && c <= 0xFAFF)    // CJK 호환 한자
                || (c >= 0xFF00 && c <= 0xFF60)    // 전각 문자
                || (c >= 0xFFE0 && c <= 0xFFE6);   // 전각 기호
        }

        public static bool NeedsKoreanFont(char ch) => ch > 0x7F && IsWide(ch);
    }

    public static class TerminalColors
    {
        public static readonly Color DefaultFore = Color.Gainsboro;
        public static readonly Color DefaultBack = Color.Black;

        // 표준 ANSI 0~7(일반) / 8~15(밝은색)
        public static readonly Color[] Palette =
        {
            Color.Black,
            Color.FromArgb(205, 49, 49),
            Color.FromArgb(13, 188, 121),
            Color.FromArgb(229, 229, 16),
            Color.FromArgb(36, 114, 200),
            Color.FromArgb(188, 63, 188),
            Color.FromArgb(17, 168, 205),
            Color.Gainsboro,
            Color.FromArgb(102, 102, 102),
            Color.FromArgb(241, 76, 76),
            Color.FromArgb(35, 209, 139),
            Color.FromArgb(245, 245, 67),
            Color.FromArgb(59, 142, 234),
            Color.FromArgb(214, 112, 214),
            Color.FromArgb(41, 184, 219),
            Color.White
        };
    }
}
