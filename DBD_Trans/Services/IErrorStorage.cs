using System.Collections.Generic;
using DBD_Trans.Models;

namespace DBD_Trans.Services
{
    public interface IErrorStorage
    {
        List<ErrorItem> GetErrors(string key);
        void UpdateErrors(string key, List<ErrorItem> errors);
        void Save();
    }
}