using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Catflap
{
    public class Logger
    {
        public delegate void LogMessage(string message);
        public static event LogMessage OnLogMessage;
        
        public static void Info(String str)
        {
            string fmtstr = DateTime.Now.ToString("HH:mm:ss") + "> " + str + "\n";

            if (OnLogMessage != null)
                OnLogMessage(fmtstr);

            Console.WriteLine(fmtstr);
        }
    }
}
