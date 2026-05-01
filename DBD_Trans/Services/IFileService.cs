using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBD_Trans.Services
{
    public interface IFileService
    {
        JObject LoadJson(string filePath);
        void SaveJson(string filePath, object data);
    }
}
