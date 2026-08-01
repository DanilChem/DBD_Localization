using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace DBD_Trans.Helpers
{
    public enum DiffSegmentType
    {
        Equal,
        Removed,
        Added
    }

    /// <summary>Кусок текста с пометкой, что с ним произошло относительно старой версии.</summary>
    public class DiffSegment
    {
        public string Text { get; set; }
        public DiffSegmentType Type { get; set; }
    }

    /// <summary>
    /// Простой word-level diff: разбивает текст на слова (сохраняя пробелы как отдельные
    /// токены) и находит наибольшую общую подпоследовательность (LCS), чтобы понять, какие
    /// слова совпадают, а какие реально убраны/добавлены. Это позволяет в истории изменений
    /// подсвечивать не весь текст целиком, а только то, что действительно поменялось —
    /// как в режиме "показать правки" в текстовых редакторах.
    /// </summary>
    public static class WordDiffer
    {
        private static readonly Regex TokenRegex = new Regex(@"\s+|\S+", RegexOptions.Compiled);

        public static List<DiffSegment> Diff(string oldText, string newText)
        {
            var oldTokens = Tokenize(oldText ?? "");
            var newTokens = Tokenize(newText ?? "");
            return DiffTokens(oldTokens, newTokens);
        }

        private static List<string> Tokenize(string text)
        {
            var result = new List<string>();
            foreach (Match m in TokenRegex.Matches(text))
                result.Add(m.Value);
            return result;
        }

        private static List<DiffSegment> DiffTokens(List<string> oldTokens, List<string> newTokens)
        {
            int n = oldTokens.Count, m = newTokens.Count;

            // lcs[i, j] = длина LCS для oldTokens[i..] и newTokens[j..] (суффиксная таблица)
            var lcs = new int[n + 1, m + 1];
            for (int i = n - 1; i >= 0; i--)
            {
                for (int j = m - 1; j >= 0; j--)
                {
                    lcs[i, j] = oldTokens[i] == newTokens[j]
                        ? lcs[i + 1, j + 1] + 1
                        : Math.Max(lcs[i + 1, j], lcs[i, j + 1]);
                }
            }

            var segments = new List<DiffSegment>();

            void Append(DiffSegmentType type, string text)
            {
                if (segments.Count > 0 && segments[segments.Count - 1].Type == type)
                    segments[segments.Count - 1].Text += text; // склеиваем соседние токены одного типа
                else
                    segments.Add(new DiffSegment { Type = type, Text = text });
            }

            int a = 0, b = 0;
            while (a < n && b < m)
            {
                if (oldTokens[a] == newTokens[b])
                {
                    Append(DiffSegmentType.Equal, oldTokens[a]);
                    a++; b++;
                }
                else if (lcs[a + 1, b] >= lcs[a, b + 1])
                {
                    Append(DiffSegmentType.Removed, oldTokens[a]);
                    a++;
                }
                else
                {
                    Append(DiffSegmentType.Added, newTokens[b]);
                    b++;
                }
            }
            while (a < n) { Append(DiffSegmentType.Removed, oldTokens[a]); a++; }
            while (b < m) { Append(DiffSegmentType.Added, newTokens[b]); b++; }

            return segments;
        }
    }
}
