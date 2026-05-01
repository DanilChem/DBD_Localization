using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;

namespace DBD_Trans.Helpers
{
    public static class JsonFlattener
    {
        public static List<KeyValuePair<string, string>> FlattenToOrderedList(JObject root)
        {
            var result = new List<KeyValuePair<string, string>>();
            Traverse(root, "", (key, value) => result.Add(new KeyValuePair<string, string>(key, value)));
            return result;
        }

        public static Dictionary<string, string> FlattenToDictionary(JObject root)
        {
            var dict = new Dictionary<string, string>();
            Traverse(root, "", (key, value) => dict[key] = value);
            return dict;
        }

        private static void Traverse(JToken token, string currentPath, Action<string, string> onStringValue)
        {
            switch (token)
            {
                case JObject obj:
                    foreach (var prop in obj.Properties())
                    {
                        string newPath = string.IsNullOrEmpty(currentPath)
                            ? prop.Name
                            : $"{currentPath}.{prop.Name}";
                        Traverse(prop.Value, newPath, onStringValue);
                    }
                    break;
                case JArray arr:
                    for (int i = 0; i < arr.Count; i++)
                    {
                        string arrayPath = $"{currentPath}[{i}]";
                        Traverse(arr[i], arrayPath, onStringValue);
                    }
                    break;
                case JValue val when val.Type == JTokenType.String:
                    onStringValue(currentPath, val.Value<string>());
                    break;
                    // Игнорируем остальные типы (числа, булевы и т.д.)
            }
        }
    }
}