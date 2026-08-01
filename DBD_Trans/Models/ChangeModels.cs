using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;

namespace DBD_Trans.Models
{
    /// <summary>
    /// Тип изменения строки локализации между двумя проверками файлов.
    /// </summary>
    public enum ChangeType
    {
        Added,
        Updated,
        Removed
    }

    /// <summary>
    /// Одна конкретная строка, которая появилась, изменилась или исчезла
    /// в Dbd-En.json / Dbd-Ru.json по сравнению с предыдущим запуском.
    /// </summary>
    public class ChangeItem
    {
        public string Key { get; set; }
        public ChangeType Type { get; set; }

        public string OldEnglish { get; set; }
        public string NewEnglish { get; set; }
        public bool EnglishChanged { get; set; }

        public string OldRussian { get; set; }
        public string NewRussian { get; set; }
        public bool RussianChanged { get; set; }
    }

    /// <summary>
    /// Набор изменений, обнаруженный за одну проверку файлов (по сути — один патч).
    /// Хранится в ChangeHistory.json и отображается в окне истории изменений.
    /// </summary>
    public class ChangeSet
    {
        public Guid Id { get; set; } = Guid.NewGuid();
        public DateTime DetectedAt { get; set; } = DateTime.Now;
        public bool IsViewed { get; set; }
        public List<ChangeItem> Changes { get; set; } = new List<ChangeItem>();

        [JsonIgnore]
        public int AddedCount => Changes?.Count(c => c.Type == ChangeType.Added) ?? 0;

        [JsonIgnore]
        public int UpdatedCount => Changes?.Count(c => c.Type == ChangeType.Updated) ?? 0;

        [JsonIgnore]
        public int RemovedCount => Changes?.Count(c => c.Type == ChangeType.Removed) ?? 0;
    }

    /// <summary>
    /// Снимок значения одной строки на момент последней проверки. Используется
    /// только для сравнения с новым содержимым файлов и хранится в ChangeSnapshot.json.
    /// </summary>
    public class SnapshotEntry
    {
        public string English { get; set; }
        public string Russian { get; set; }
    }
}
