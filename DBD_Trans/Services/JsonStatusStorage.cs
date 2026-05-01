using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using DBD_Trans.Models;

namespace DBD_Trans.Services
{
    public class JsonStatusStorage : IStatusStorage
    {
        private readonly string _filePath;
        private Dictionary<string, ItemStatus> _statuses;

        public JsonStatusStorage(string baseDirectory)
        {
            _filePath = Path.Combine(baseDirectory, "Statuses.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _statuses = JsonConvert.DeserializeObject<Dictionary<string, ItemStatus>>(json)
                           ?? new Dictionary<string, ItemStatus>();
            }
            else
            {
                _statuses = new Dictionary<string, ItemStatus>();
            }
        }

        public ItemStatus GetStatus(string key)
        {
            return _statuses.TryGetValue(key, out var status) ? status : ItemStatus.InProgress;
        }

        public void SetStatus(string key, ItemStatus status)
        {
            _statuses[key] = status;
            Save();
        }

        public void Save()
        {
            var json = JsonConvert.SerializeObject(_statuses, Formatting.Indented);
            File.WriteAllText(_filePath, json);
        }
    }
}