using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
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
            Console.ReadLine();
        }
    }
}
