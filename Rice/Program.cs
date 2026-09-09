using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Rice.Server;
using Rice.Server.Core;

namespace Rice
{
    class Program
    {
        static void Main(string[] args)
        {
            try
            {
                Console.Title = "Rice";
                Console.Clear();
            }
            catch (System.IO.IOException)
            {
                // Running with output redirected to a file or pipe: there is no
                // console buffer to clear, which is not a reason to fail startup.
            }

            Config config = Config.Load();

            Database.Initialize(config);
            Resources.Initialize(config);
            RiceServer.Initialize(config);
            Admin.Initialize(config);

            Database.Start();
            RiceServer.Start();

            // Console.ReadLine() used to sit here, but it needs a real stdin: started
            // hidden/detached (launcher, scheduled task) with nothing to read from, it
            // returns immediately and the process exits right after "started". Wait on
            // an event instead, set only by an actual shutdown signal.
            var shutdown = new ManualResetEventSlim(false);

            try
            {
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    shutdown.Set();
                };
            }
            catch (System.IO.IOException)
            {
                // No console attached to hook Ctrl+C on - ProcessExit below still covers shutdown.
            }

            AppDomain.CurrentDomain.ProcessExit += (s, e) => shutdown.Set();

            shutdown.Wait();
            Log.WriteLine("Shutting down.");
        }
    }
}
