using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;

namespace DBD_Trans.Services
{
    public class JsonMergeStorage : IMergeStorage
    {
        private readonly IFileService _fileService;
        private readonly string _filePath;
        private Dictionary<string, MergeData> _merges;

        public JsonMergeStorage(IFileService fileService, string baseDirectory)
        {
            _fileService = fileService;
            _filePath = Path.Combine(baseDirectory, "Merges.json");
            Load();
        }

        private void Load()
        {
            if (File.Exists(_filePath))
            {
                var json = File.ReadAllText(_filePath);
                _merges = JsonConvert.DeserializeObject<Dictionary<string, MergeData>>(json) ?? new Dictionary<string, MergeData>();
            }
            else
            {
                _merges = new Dictionary<string, MergeData>();
            }
        }

        public List<int> GetMerges(string key, bool isEnglish)
        {
            if (_merges.TryGetValue(key, out var data))
            {
                return isEnglish ? data.English : data.Russian;
            }
            return new List<int>();
        }

        public void SetMerges(string key, bool isEnglish, List<int> mergedStartIndices)
        {
            if (!_merges.ContainsKey(key))
                _merges[key] = new MergeData();

            if (isEnglish)
                _merges[key].English = mergedStartIndices;
            else
                _merges[key].Russian = mergedStartIndices;
        }

        public void Save()
        {
            _fileService.SaveJson(_filePath, _merges);
        }
    }

    public class MergeData
    {
        public List<int> English { get; set; } = new List<int>();
        public List<int> Russian { get; set; } = new List<int>();
    }
}