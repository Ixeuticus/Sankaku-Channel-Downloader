using System;
using System.IO;

namespace SankakuDownloader
{
    public static class Logger
    {
        private const string LogFile = "*_errorlog.txt";
        public static string CurrentLogFile => LogFile.Replace("*", DateTime.Now.ToString("dd-MM-yyyy"));

        public static void Log(string message)
        {
            var timestamp = $"[{DateTime.Now.ToString("dd.MM.yyyy HH:mm:ss")}] ";
            File.AppendAllText(CurrentLogFile, timestamp + message + "\n");
        }
        public static void Log(Exception ex, string extraMessage = null)
        {
            int d = 5, c = 0;
            string msg = (extraMessage ?? "") + constructMessage(ex);
            string constructMessage(Exception e)
            {
                c++;
                if (c > d) return "";
                var m = e.Message;

                if (e.InnerException != null && 
                    e.InnerException?.Message != null)
                    m += " -> " + constructMessage(e.InnerException);
                
                return m;
            }
            Log(msg);
        }
    }
}
