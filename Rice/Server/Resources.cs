using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using Rice.Server.Structures.Resources;

namespace Rice.Server
{
    public static class Resources
    {
        public static void Initialize(Config config)
        {
            // Resolved against the exe's own folder, not the process working
            // directory, so a launcher that starts Rice.exe from elsewhere still finds these.
            string resDir = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "res");
            string Res(string fileName) => Path.Combine(resDir, fileName);

            QuestTable.Load(Res("QuestClient.json"));
            ItemTable.Load(Res("ItemClient.json"), Res("UseItemClient.json"));
            VehicleTable.Load(Res("VehicleUpgrade.json"), Res("VehicleList.json"));
            AssistTable.Load(Res("AssistClient.json"));
            VShopTable.Load(Res("VShopItems.xml"));

            Log.WriteLine("Loaded resources.");
        }
    }
}
