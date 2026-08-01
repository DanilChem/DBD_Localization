using DBD_Trans.Helpers;
using DBD_Trans.Models;
using Newtonsoft.Json;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace DBD_Trans.Services
{
    /// <summary>
    /// Хранит два файла рядом с Errors.json / Statuses.json:
    ///  - ChangeSnapshot.json — последнее известное значение каждой строки (для сравнения);
    ///  - ChangeHistory.json  — журнал всех обнаруженных наборов изменений (для окна истории).
    /// </summary>
    public class JsonChangeHistoryStorage : IChangeHistoryStorage
    {
        // Сколько последних патчей храним в истории, чтобы файл не рос бесконечно.
        private const int MaxHistoryEntries = 100;

        private readonly IFileService _fileService;
        private readonly string _snapshotPath;
        private readonly string _historyPath;
        private readonly bool _snapshotExistedOnStart;

        private Dictionary<string, SnapshotEntry> _snapshot;
        private List<ChangeSet> _history;

        public JsonChangeHistoryStorage(IFileService fileService, string baseDirectory)
        {
            _fileService = fileService;
            _snapshotPath = Path.Combine(baseDirectory, "ChangeSnapshot.json");
            _historyPath = Path.Combine(baseDirectory, "ChangeHistory.json");

            _snapshotExistedOnStart = File.Exists(_snapshotPath);

            Load();
        }

        private void Load()
        {
            if (File.Exists(_snapshotPath))
            {
                var json = File.ReadAllText(_snapshotPath);
                _snapshot = JsonConvert.DeserializeObject<Dictionary<string, SnapshotEntry>>(json)
                            ?? new Dictionary<string, SnapshotEntry>();
            }
            else
            {
                _snapshot = new Dictionary<string, SnapshotEntry>();
            }

            if (File.Exists(_historyPath))
            {
                var json = File.ReadAllText(_historyPath);
                _history = JsonConvert.DeserializeObject<List<ChangeSet>>(json)
                           ?? new List<ChangeSet>();
            }
            else
            {
                _history = new List<ChangeSet>();
            }
        }

        public ChangeSet DetectAndRecordChanges(Dictionary<string, string> currentEnglish, Dictionary<string, string> currentRussian)
        {
            currentEnglish = currentEnglish ?? new Dictionary<string, string>();
            currentRussian = currentRussian ?? new Dictionary<string, string>();

            // Первый запуск с этой функцией — снимка ещё не существует. Не считаем всё
            // текущее содержимое "добавленным", а просто фиксируем точку отсчёта.
            if (!_snapshotExistedOnStart)
            {
                RebuildSnapshot(currentEnglish, currentRussian);
                Save();
                return null;
            }

            var changes = LocalizationDiffer.Diff(currentEnglish, currentRussian, _snapshot);

            RebuildSnapshot(currentEnglish, currentRussian);

            if (changes.Count == 0)
            {
                Save();
                return null;
            }

            var changeSet = new ChangeSet { Changes = changes };
            _history.Insert(0, changeSet);

            if (_history.Count > MaxHistoryEntries)
                _history.RemoveRange(MaxHistoryEntries, _history.Count - MaxHistoryEntries);

            Save();
            return changeSet;
        }

        private void RebuildSnapshot(Dictionary<string, string> currentEnglish, Dictionary<string, string> currentRussian)
        {
            var newSnapshot = new Dictionary<string, SnapshotEntry>();
            var allKeys = new HashSet<string>(currentEnglish.Keys);
            allKeys.UnionWith(currentRussian.Keys);

            foreach (var key in allKeys)
            {
                currentEnglish.TryGetValue(key, out var en);
                currentRussian.TryGetValue(key, out var ru);
                newSnapshot[key] = new SnapshotEntry { English = en, Russian = ru };
            }

            _snapshot = newSnapshot;
        }

        public List<ChangeSet> GetHistory()
        {
            return _history.OrderByDescending(c => c.DetectedAt).ToList();
        }

        public int GetUnviewedChangeItemCount()
        {
            return _history.Where(c => !c.IsViewed).Sum(c => c.Changes?.Count ?? 0);
        }

        public void MarkAllAsViewed()
        {
            foreach (var set in _history)
                set.IsViewed = true;
            Save();
        }

        public void ClearHistory()
        {
            _history.Clear();
            Save();
        }

        public void Save()
        {
            _fileService.SaveJson(_snapshotPath, _snapshot);
            _fileService.SaveJson(_historyPath, _history);
        }
    }
}
