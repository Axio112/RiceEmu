using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Xml;

namespace Rice.Server.Structures.Resources
{
    public static class VShopTable
    {
        public static Dictionary<uint, VShopEntry> Entries = new Dictionary<uint, VShopEntry>();

        public static void Load(string path)
        {
            Entries.Clear();

            if (!File.Exists(path))
            {
                Log.WriteError("Could not find VShopItems at " + path);
                return;
            }

            var doc = new XmlDocument();
            doc.Load(path);

            var nodes = doc.SelectNodes("//VShopItem");
            if (nodes == null)
            {
                Log.WriteError("VShopItems.xml contained no VShopItem nodes");
                return;
            }

            foreach (XmlNode node in nodes)
            {
                if (node.Attributes == null)
                    continue;

                var entry = new VShopEntry
                {
                    Support = AttrInt(node, "support"),
                    Id = AttrUInt(node, "id"),
                    ItemCode = AttrStr(node, "item"),
                    Category = AttrInt(node, "category"),
                    CategoryIdx = AttrInt(node, "categoryIdx"),
                    UseMito = AttrInt(node, "useMito") != 0,
                    UseHancoin = AttrInt(node, "useHancoin") != 0,
                    MitoPrice = AttrLong(node, "mitoPrice"),
                    MitoSell = AttrLong(node, "mitoSell"),
                    Mito7dPrice = AttrLong(node, "mito7dPrice"),
                    Mito30dPrice = AttrLong(node, "mito30dPrice"),
                    Mito90dPrice = AttrLong(node, "mito90dPrice"),
                    Mito365dPrice = AttrLong(node, "mito365dPrice"),
                    Mito0dPrice = AttrLong(node, "mito0dPrice"),
                    Hancoin7dPrice = AttrLong(node, "hancoin7dPrice"),
                    Hancoin30dPrice = AttrLong(node, "hancoin30dPrice"),
                    Hancoin90dPrice = AttrLong(node, "hancoin90dPrice"),
                    Hancoin365dPrice = AttrLong(node, "hancoin3650dPrice"),
                    Hancoin0dPrice = AttrLong(node, "hancoin0dPrice")
                };

                if (entry.Id == 0)
                    continue;

                Entries[entry.Id] = entry;
            }

            Log.WriteLine("Loaded {0} VShop items.", Entries.Count);
        }

        public static VShopEntry Get(uint id)
        {
            VShopEntry entry;
            return Entries.TryGetValue(id, out entry) ? entry : null;
        }

        static string AttrStr(XmlNode node, string name)
        {
            var a = node.Attributes[name];
            return a != null ? a.Value : "";
        }

        static int AttrInt(XmlNode node, string name)
        {
            int v;
            return int.TryParse(AttrStr(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0;
        }

        static uint AttrUInt(XmlNode node, string name)
        {
            uint v;
            return uint.TryParse(AttrStr(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0u;
        }

        static long AttrLong(XmlNode node, string name)
        {
            long v;
            return long.TryParse(AttrStr(node, name), NumberStyles.Integer, CultureInfo.InvariantCulture, out v) ? v : 0L;
        }
    }

    public class VShopEntry
    {
        public int Support;
        public uint Id;
        public string ItemCode;
        public int Category;
        public int CategoryIdx;
        public bool UseMito;
        public bool UseHancoin;
        public long MitoPrice;
        public long MitoSell;
        public long Mito7dPrice;
        public long Mito30dPrice;
        public long Mito90dPrice;
        public long Mito365dPrice;
        public long Mito0dPrice;
        public long Hancoin7dPrice;
        public long Hancoin30dPrice;
        public long Hancoin90dPrice;
        public long Hancoin365dPrice;
        public long Hancoin0dPrice;

        /// <summary>
        /// periodIdx: 1=7d, 2=30d, 3=90d, 4=365d, 5=infinite/0d
        /// Returns mito or hancoin cost depending on UseMito / UseHancoin.
        /// </summary>
        public long GetPrice(uint periodIdx)
        {
            if (UseHancoin && !UseMito)
                return GetHancoinPrice(periodIdx);
            return GetMitoPrice(periodIdx);
        }

        public long GetMitoPrice(uint periodIdx)
        {
            switch (periodIdx)
            {
                case 1: return Mito7dPrice;
                case 2: return Mito30dPrice;
                case 3: return Mito90dPrice;
                case 4: return Mito365dPrice;
                case 5:
                    if (Mito0dPrice > 0) return Mito0dPrice;
                    if (MitoPrice > 0) return MitoPrice;
                    return 0;
                default:
                    if (MitoPrice > 0) return MitoPrice;
                    if (Mito0dPrice > 0) return Mito0dPrice;
                    return Mito30dPrice;
            }
        }

        public long GetHancoinPrice(uint periodIdx)
        {
            switch (periodIdx)
            {
                case 1: return Hancoin7dPrice;
                case 2: return Hancoin30dPrice;
                case 3: return Hancoin90dPrice;
                case 4: return Hancoin365dPrice;
                case 5: return Hancoin0dPrice;
                default: return Hancoin30dPrice;
            }
        }

        public bool IsHancoinPurchase()
        {
            return UseHancoin && !UseMito;
        }
    }
}
