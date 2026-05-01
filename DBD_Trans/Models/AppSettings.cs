using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace DBD_Trans.Models
{
    public class AppSettings : IAppSettings
    {
        public double AnalysisFontSize
        {
            get => Properties.Settings.Default.AnalysisFontSize;
            set => Properties.Settings.Default.AnalysisFontSize = value;
        }

        public void Save()
        {
            Properties.Settings.Default.Save();
        }
    }
}
