using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBD_Trans.Models
{
    public interface IAppSettings
    {
        double AnalysisFontSize { get; set; }
        void Save();
    }
}
