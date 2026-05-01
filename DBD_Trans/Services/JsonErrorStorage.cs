using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using DBD_Trans.Models;

namespace DBD_Trans.Services
{
    public class JsonErrorStorage : IErrorStorage
    {
        private readonly IFileService _fileService;
        private readonly string _filePath;
        private Dictionary<string, List<ErrorItem>> _errors;

        public JsonErrorStorage(IFileService fileService, string baseDirectory)
        {
            _fileService = fileService;
            _filePath = Path.Combine(baseDirectory, "Errors.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _errors = JsonConvert.DeserializeObject<Dictionary<string, List<ErrorItem>>>(json)
                          ?? new Dictionary<string, List<ErrorItem>>();
            }
            else
            {
                _errors = new Dictionary<string, List<ErrorItem>>();
            }
        }

        public List<ErrorItem> GetErrors(string key)
        {
            return _errors.TryGetValue(key, out var list) ? list : new List<ErrorItem>();
        }

        public void UpdateErrors(string key, List<ErrorItem> errors)
        {
            if (errors == null || errors.Count == 0)
            {
                if (_errors.ContainsKey(key))
                    _errors.Remove(key);
            }
            else
            {
                _errors[key] = errors;
            }
            Save();
        }

        public void Save()
        {
            _fileService.SaveJson(_filePath, _errors);
        }
    }
}