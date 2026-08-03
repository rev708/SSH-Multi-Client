using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;
using SshTabClient.Models;

namespace SshTabClient.Services
{
    public class ProfileStore
    {
        private readonly string _filePath;

        public ProfileStore()
        {
            var dir = Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "SshTabClient");
            Directory.CreateDirectory(dir);
            _filePath = Path.Combine(dir, "servers.json");
        }

        public List<ServerProfile> Load()
        {
            if (!File.Exists(_filePath)) return new List<ServerProfile>();
            try
            {
                var json = File.ReadAllText(_filePath);
                var list = JsonSerializer.Deserialize<List<ServerProfile>>(json);
                return list ?? new List<ServerProfile>();
            }
            catch
            {
                return new List<ServerProfile>();
            }
        }

        public void Save(List<ServerProfile> profiles)
        {
            var json = JsonSerializer.Serialize(profiles, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(_filePath, json);
        }
    }
}
