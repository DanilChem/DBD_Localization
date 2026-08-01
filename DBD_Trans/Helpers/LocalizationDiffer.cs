using DBD_Trans.Models;
using System;
using System.Collections.Generic;

namespace DBD_Trans.Helpers
{
    /// <summary>
    /// Чистая логика сравнения: берёт текущее содержимое Dbd-En.json / Dbd-Ru.json
    /// (уже развёрнутое в словари "ключ -> значение") и снимок, сохранённый при
    /// предыдущей проверке, и возвращает список того, что добавилось, изменилось
    /// или пропало. Никакого чтения/записи файлов — только сравнение.
    /// </summary>
    public static class LocalizationDiffer
    {
        public static List<ChangeItem> Diff(
            Dictionary<string, string> currentEnglish,
            Dictionary<string, string> currentRussian,
            Dictionary<string, SnapshotEntry> previousSnapshot)
        {
            var changes = new List<ChangeItem>();

            var allKeys = new HashSet<string>(currentEnglish.Keys);
            allKeys.UnionWith(currentRussian.Keys);
            allKeys.UnionWith(previousSnapshot.Keys);

            foreach (var key in allKeys)
            {
                currentEnglish.TryGetValue(key, out var newEn);
                currentRussian.TryGetValue(key, out var newRu);
                bool existsNow = currentEnglish.ContainsKey(key) || currentRussian.ContainsKey(key);

                previousSnapshot.TryGetValue(key, out var old);
                bool existedBefore = old != null;

                if (existsNow && !existedBefore)
                {
                    changes.Add(new ChangeItem
                    {
                        Key = key,
                        Type = ChangeType.Added,
                        NewEnglish = newEn,
                        NewRussian = newRu,
                        EnglishChanged = !string.IsNullOrEmpty(newEn),
                        RussianChanged = !string.IsNullOrEmpty(newRu)
                    });
                }
                else if (!existsNow && existedBefore)
                {
                    changes.Add(new ChangeItem
                    {
                        Key = key,
                        Type = ChangeType.Removed,
                        OldEnglish = old.English,
                        OldRussian = old.Russian,
                        EnglishChanged = !string.IsNullOrEmpty(old.English),
                        RussianChanged = !string.IsNullOrEmpty(old.Russian)
                    });
                }
                else if (existsNow && existedBefore)
                {
                    bool enChanged = !string.Equals(old.English ?? "", newEn ?? "", StringComparison.Ordinal);
                    bool ruChanged = !string.Equals(old.Russian ?? "", newRu ?? "", StringComparison.Ordinal);

                    if (enChanged || ruChanged)
                    {
                        changes.Add(new ChangeItem
                        {
                            Key = key,
                            Type = ChangeType.Updated,
                            OldEnglish = old.English,
                            NewEnglish = newEn,
                            EnglishChanged = enChanged,
                            OldRussian = old.Russian,
                            NewRussian = newRu,
                            RussianChanged = ruChanged
                        });
                    }
                }
                // existsNow == false && existedBefore == false — ключ, который упомянут в
                // allKeys из-за UnionWith, но на деле нигде не встречается, пропускаем.
            }

            changes.Sort((a, b) => string.Compare(a.Key, b.Key, StringComparison.OrdinalIgnoreCase));
            return changes;
        }
    }
}
