using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBD_Trans.Services
{
    public class JsonFileService : IFileService
    {
        public JObject LoadJson(string filePath)
        {
            if (!File.Exists(filePath))
                return null;

            var json = File.ReadAllText(filePath);
            return JObject.Parse(json);
        }

        public void SaveJson(string filePath, object data)
        {
            var json = JsonConvert.SerializeObject(data, Formatting.Indented);
            File.WriteAllText(filePath, json);
        }
    }
}
