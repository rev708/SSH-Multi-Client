using System;
using System.Text.Json.Serialization;

namespace SshTabClient.Models
{
    public enum AuthType
    {
        Password,
        KeyFile
    }

    public class ServerProfile
    {
        public string Id { get; set; } = Guid.NewGuid().ToString("N");
        public string Name { get; set; } = "";
        public string Host { get; set; } = "";
        public int Port { get; set; } = 22;
        public string Username { get; set; } = "";
        public AuthType AuthType { get; set; } = AuthType.Password;

        // 비밀번호/키 암호는 절대 평문으로 저장하지 않고 DPAPI로 암호화된 값만 저장합니다.
        public string? EncryptedPassword { get; set; }
        public string? KeyFilePath { get; set; }
        public string? EncryptedPassphrase { get; set; }

        [JsonIgnore]
        public string DisplayName => $"{Name} ({Username}@{Host}:{Port})";

        public override string ToString() => DisplayName;
    }
}
