using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Rice
{
    public class Config
    {
        const string DefaultFileName = "server.json";

        public bool DebugMode = true;
        public string PublicIP = "127.0.0.1";

        public ushort AuthPort = 11005;
        public ushort LobbyPort = 11011;
        public ushort GamePort = 11021;
        public ushort AreaPort = 11031;
        public ushort RankingPort = 11078;

        // Resolved against the exe's own folder, not the process working
        // directory, so a launcher that starts Rice.exe from elsewhere still finds it.
        static string ResolvePath(string fileName) =>
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, fileName);

        public static Config Load(string fileName = DefaultFileName)
        {
            Config config;
            string path = ResolvePath(fileName);

            if (File.Exists(path))
            {
                Log.WriteLine("Config file exists, loading.");

                string json = File.ReadAllText(path);
                config = JsonConvert.DeserializeObject<Config>(json);
            }
            else
            {
                Log.WriteLine("Config file could not be found, using defaults.");
                config = new Config();
            }

            return config;
        }

        public void Save(string fileName = DefaultFileName)
        {
            string json = JsonConvert.SerializeObject(this, Formatting.Indented);
            File.WriteAllText(ResolvePath(fileName), json);
        }
    }
}
