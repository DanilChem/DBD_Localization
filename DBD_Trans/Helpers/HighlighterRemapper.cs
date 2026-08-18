using DBD_Trans.Models;
using System.Collections.Generic;

namespace DBD_Trans.Helpers
{
    /// <summary>
    /// Переносит сохранённые диапазоны подсветки (маркеров/замечаний) со старой версии
    /// текста строки на новую версию, когда содержимое Dbd-En.json / Dbd-Ru.json меняется.
    ///
    /// Логика: строим word-level диф (WordDiffer, уже используется в ChangesViewModel)
    /// между старым и новым текстом и по нему — карту "символ старого текста -> символ
    /// нового текста". Если помеченный переводчиком фрагмент целиком лежит в неизменной
    /// части текста — его позиция аккуратно сдвигается. Если фрагмент реально задет
    /// правкой (попадает в удалённый/изменённый кусок) — надёжно перенести его нельзя,
    /// и такой диапазон подсветки отбрасывается (но сам ErrorItem с текстом замечания
    /// НЕ удаляется — просто перестаёт быть привязан к конкретному месту в тексте).
    ///
    /// ВАЖНО: oldText/newText должны быть в тех же координатах, что и StartIndex/Length
    /// у TextRangeInfo — то есть уже прогнаны через HtmlStripper.StripHtmlTags, а не
    /// сырые значения из JSON (см. AnalysisViewModel.CleanEnglishText/CleanRussianText).
    /// </summary>
    public static class HighlightRemapper
    {
        public static List<TextRangeInfo> Remap(List<TextRangeInfo> oldHighlights, string oldText, string newText)
        {
            if (oldHighlights == null || oldHighlights.Count == 0)
                return new List<TextRangeInfo>();

            oldText = oldText ?? "";
            newText = newText ?? "";

            if (oldText == newText)
                return oldHighlights; // этот язык не менялся — переносить нечего

            var diff = WordDiffer.Diff(oldText, newText);

            // map[i] = позиция символа i старого текста в новом тексте,
            // либо -1, если символ реально пропал/изменился (надёжного соответствия нет).
            var map = new int[oldText.Length + 1];
            int oldPos = 0, newPos = 0;

            foreach (var seg in diff)
            {
                int len = seg.Text?.Length ?? 0;
                switch (seg.Type)
                {
                    case DiffSegmentType.Equal:
                        for (int i = 0; i < len; i++) map[oldPos + i] = newPos + i;
                        oldPos += len;
                        newPos += len;
                        break;

                    case DiffSegmentType.Removed:
                        for (int i = 0; i < len; i++) map[oldPos + i] = -1;
                        oldPos += len;
                        break;

                    case DiffSegmentType.Added:
                        newPos += len;
                        break;
                }
            }
            map[oldText.Length] = newPos; // "конец текста" тоже нужно уметь отобразить

            var result = new List<TextRangeInfo>();
            foreach (var h in oldHighlights)
            {
                int start = h.StartIndex;
                int end = h.StartIndex + h.Length; // exclusive

                // Защита от уже битых/некорректных данных (например, старый баг уже что-то испортил)
                if (start < 0 || end > oldText.Length || start >= end)
                    continue;

                bool damaged = false;
                for (int i = start; i < end; i++)
                {
                    if (map[i] == -1) { damaged = true; break; }
                }
                if (damaged) continue; // сам помеченный текст изменился — старую позицию не сохраняем

                int newStart = map[start];
                int newEnd = map[end];
                if (newEnd > newStart)
                    result.Add(new TextRangeInfo { StartIndex = newStart, Length = newEnd - newStart });
            }
            return result;
        }
    }
}