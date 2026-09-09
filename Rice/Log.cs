using System;
using System.IO;
using System.Text;

namespace Rice
{
    public static class Log
    {
        // The server is normally watched through its console window, which is lost the
        // moment anything redirects stdout (Program.Main calls Console.Clear). Mirroring
        // every line into a file makes crashes readable after the fact.
        private static readonly object fileLock = new object();
        private static readonly string logPath =
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "rice.log");
        private static bool fileDisabled;

        static Log()
        {
            try
            {
                File.WriteAllText(logPath,
                    string.Format("=== Rice log opened {0:yyyy-MM-dd HH:mm:ss} ==={1}",
                        DateTime.Now, Environment.NewLine),
                    Encoding.UTF8);
            }
            catch (Exception)
            {
                // A missing log is never a reason to take the server down.
                fileDisabled = true;
            }
        }

        private static void ToFile(string line)
        {
            if (fileDisabled)
                return;

            try
            {
                lock (fileLock)
                    File.AppendAllText(logPath, line + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception)
            {
                fileDisabled = true;
            }
        }

        public static void WriteLine(string format, params object[] args)
        {
            string message = string.Format(format, args);
            string line = string.Format("[{0}] {1}", DateTime.Now.ToLongTimeString(), message);

            Console.WriteLine(line);
            ToFile(line);
        }

        public static void WriteError(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.Red;
            WriteLine("ERROR: " + format, args);
            Console.ForegroundColor = ConsoleColor.Gray;
        }

        public static void WriteDebug(string format, params object[] args)
        {
            Console.ForegroundColor = ConsoleColor.DarkGray;
            WriteLine("DEBUG: " + format, args);
            Console.ForegroundColor = ConsoleColor.Gray;
        }
    }
}
