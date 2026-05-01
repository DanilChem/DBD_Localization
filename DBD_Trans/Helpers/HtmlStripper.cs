using System.Text.RegularExpressions;

namespace DBD_Trans.Helpers
{
    public static class HtmlStripper
    {
        public static string StripHtmlTags(string input)
        {
            if (string.IsNullOrEmpty(input))
                return input;

            // 1. <br> → \n
            string result = Regex.Replace(input, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);

            // 2. <li> → \n• 
            result = Regex.Replace(result, @"<li[^>]*>", "\n• ", RegexOptions.IgnoreCase);

            // 3. Remove </li>
            result = Regex.Replace(result, @"</li>", "", RegexOptions.IgnoreCase);

            // 4. <ul>/<ol> → \n
            result = Regex.Replace(result, @"<ul[^>]*>", "\n", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"<ol[^>]*>", "\n", RegexOptions.IgnoreCase);

            // 5. Remove </ul>, </ol>
            result = Regex.Replace(result, @"</ul>", "", RegexOptions.IgnoreCase);
            result = Regex.Replace(result, @"</ol>", "", RegexOptions.IgnoreCase);

            // 6. Remove any other tags
            result = Regex.Replace(result, "<.*?>", string.Empty);

            // 7. Normalize newlines
            result = Regex.Replace(result, @"\n{3,}", "\n\n");

            return result.Trim();
        }
    }
}