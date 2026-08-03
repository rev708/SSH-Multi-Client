using System;
using System.Security.Cryptography;
using System.Text;

namespace SshTabClient.Services
{
    /// <summary>
    /// Windows DPAPI(현재 사용자 계정 범위)로 문자열을 암호화/복호화합니다.
    /// 비밀번호나 키 암호를 평문으로 파일에 저장하지 않기 위해 사용합니다.
    /// 주의: 다른 PC나 다른 Windows 계정으로 옮기면 복호화할 수 없습니다.
    /// </summary>
    public static class CredentialProtector
    {
        private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("SshTabClient.v1");

        public static string? Protect(string? plainText)
        {
            if (string.IsNullOrEmpty(plainText)) return null;
            var bytes = Encoding.UTF8.GetBytes(plainText);
            var protectedBytes = ProtectedData.Protect(bytes, Entropy, DataProtectionScope.CurrentUser);
            return Convert.ToBase64String(protectedBytes);
        }

        public static string? Unprotect(string? encrypted)
        {
            if (string.IsNullOrEmpty(encrypted)) return null;
            try
            {
                var protectedBytes = Convert.FromBase64String(encrypted);
                var bytes = ProtectedData.Unprotect(protectedBytes, Entropy, DataProtectionScope.CurrentUser);
                return Encoding.UTF8.GetString(bytes);
            }
            catch
            {
                return null;
            }
        }
    }
}
