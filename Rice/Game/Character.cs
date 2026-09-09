using System;
using System.IO;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using Rice.Server;
using Rice.Server.Core;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;
using Newtonsoft.Json;
using Models = Rice.Server.Database.Models;

namespace Rice.Game
{
    public class Character
    {
        public User User;
        public ulong UID;
        public ulong CID;

        public string Name;
        public long Mito;
        public int Avatar;
        public int Level;
        public long Experience;
        public int City;
        public Vector4 Position;
        public int PosState;
        public int CurrentCarID;
        public int GarageLevel;

        public uint QuickSlot1;
        public uint QuickSlot2;
        public long TID;

        public List<Item> Inventory;
        public List<Vehicle> Garage;

        private List<ItemModInfo> PendingItemMods;

        public List<VisualInvItem> VisualInventory;
        public Dictionary<int, VisualItem> EquippedVisuals;

        public Area Area;
        public Quest Quest;
        public Vehicle Vehicle => Garage.FirstOrDefault(veh => veh.CarID == CurrentCarID);
        public ushort Serial;

        private Character(Models.Character dbCharacter)
        {
            CID = (ulong) dbCharacter.ID;
            UID = (ulong) dbCharacter.UID;
            Name = dbCharacter.Name;
            Mito = dbCharacter.Mito;
            Avatar = dbCharacter.Avatar;
            Level = dbCharacter.Level;
            Experience = dbCharacter.Experience;
            City = dbCharacter.City;
            Position = new Vector4(dbCharacter.PosX, dbCharacter.PosY, dbCharacter.PosZ);
            PosState = dbCharacter.PosState;
            CurrentCarID = dbCharacter.CurrentCarID;
            QuickSlot1 = (uint) dbCharacter.QuickSlot1;
            QuickSlot2 = (uint) dbCharacter.QuickSlot2;
            GarageLevel = dbCharacter.GarageLevel;
            TID = dbCharacter.TID;
            Inventory = dbCharacter.Items.Select(Item.FromDB).ToList();
            Garage = dbCharacter.Vehicles.Select(Vehicle.FromDB).ToList();
            PendingItemMods = new List<ItemModInfo>();
            VisualInventory = new List<VisualInvItem>();
            EquippedVisuals = new Dictionary<int, VisualItem>();
            LoadVisualInventory();
            // Level used to be left out when experience was saved, so a stored
            // character can carry more experience than its level accounts for.
            // Experience is the authoritative value, so derive the level from it.
            int storedLevel = Level;
            if (Level < 1)
                Level = 1;
            while (Level < RiceServer.ExpSumTable.Length - 1
                   && Experience > RiceServer.ExpSumTable[Level])
                Level++;
            if (Level != storedLevel)
                Log.WriteLine("Corrected {0} from level {1} to {2} for {3} experience.",
                    Name, storedLevel, Level, Experience);

            Log.WriteDebug($"char init, {Inventory.Count} items in inv, {Garage.Count} vehicles");
        }

        public ExpInfo GetExpInfo()
        {
            return ExpInfo.FromLevelExp(Level, Experience);
        }

        public bool SelectCar(int carId)
        {
            CurrentCarID = carId;
            using (var rc = Database.GetContext())
            {
                var character = rc.Characters.Find((long)CID);
                character.CurrentCarID = carId;
                rc.SaveChanges();
                return true;
            }
        }

        public bool GrantExperience(long exp)
        {
            Experience += exp;

            // ExpSumTable has a fixed length, so a large reward could walk Level past
            // the end of it and throw. Stop at the last real entry.
            while (Level < RiceServer.ExpSumTable.Length - 1
                   && Experience > RiceServer.ExpSumTable[Level])
                Level++;

            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long) CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for GrantMito");
                    return false;
                }

                ch.Experience = Experience;
                ch.Level = Level;
                rc.SaveChanges();
            }
            return true;
        }

        public bool SaveQuickSlots(uint slot1, uint slot2)
        {
            QuickSlot1 = slot1;
            QuickSlot2 = slot2;

            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long) CID);
                if (ch == null)
                {
                    Log.WriteError("Could not find char {0} to save quick slots.", CID);
                    return false;
                }

                ch.QuickSlot1 = slot1;
                ch.QuickSlot2 = slot2;
                rc.SaveChanges();
            }
            return true;
        }

        public bool GrantMito(ulong mito)
        {
            Mito += (long)mito;

            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long)CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for GrantMito");
                    return false;
                }

                ch.Mito = Mito;
                rc.SaveChanges();
            }
            return true;
        }

        public bool SpendMito(ulong mito)
        {
            long mitoAfter = Mito - (long)mito;
            if (mitoAfter < 0)
                return false;

            Mito = mitoAfter;
            
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long)CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for SpendMito");
                    return false;
                }

                ch.Mito = Mito;
                rc.SaveChanges();
            }
            return true;
        }

        public bool GrantItem(string ID, uint StackNum, out Item resultItem)
        {
            Log.WriteLine($"Attempting to grant CID {CID} item {ID} (count {StackNum})");
            
            // TODO: Merge stacks
            var item = Item.CreateEntry(CID, ID, StackNum);
            resultItem = item;

            if (item == null)
                return false;
            
            Inventory.Add(item); // should be auto-added to inv in db at CreateEntry
            addItemMod(item);
            return true;
        }

        public bool EquipItem(int invIdx, short destSlot, int carId)
        {
            var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);
            if (invItem == null)
            {
                Log.WriteError($"Attempted to equip non-existant item at invIdx {invIdx}");
                return false;
            }

            if (Garage.SingleOrDefault(v => v.CarID == carId) == null)
            {
                Log.WriteError($"Couldn't find car id {carId} to equip item at invIdx {invIdx}");
                return false;
            }

            var itemCat = invItem.itemEntry.Category;
            if (!ItemTable.SlotMap.ContainsKey(itemCat) ||
                !ItemTable.SlotMap[itemCat].Contains(destSlot))
            {
                Log.WriteError($"Attempted to equip invIdx {invIdx} to {destSlot} which is invalid for category {itemCat}");
                return false;
            }

            using (var rc = Database.GetContext())
            {
                var itemsInSlot = rc.Items.Count(i => i.CID == (long) CID && i.State == 1 && i.Slot == destSlot);
                if (itemsInSlot >= 3)
                {
                    Log.WriteError(
                        $"Attempted to equip invIdx {invIdx} to {destSlot} which already has {itemsInSlot} items");
                    return false;
                }

                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long) CID && i.InvIdx == invIdx);
                if (dbItem == null)
                {
                    Log.WriteError($"Failed to find item invIdx {invIdx} for Character.EquipItem");
                    return false;
                }

                dbItem.Slot = destSlot;
                invItem.slot = (ushort) destSlot;

                dbItem.CurCarID = carId;
                invItem.curCarID = (uint) carId;

                dbItem.State = 1;
                invItem.state = 1;

                rc.SaveChanges();
                addItemMod(invItem, true);
            }

            return true;
        }

        public bool UnEquipItem(int invIdx, int carId)
        {
            var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);
            if (invItem == null)
            {
                Log.WriteError($"Attempted to unequip non-existant item at invIdx {invIdx}");
                return false;
            }

            if (Garage.SingleOrDefault(v => v.CarID == carId) == null)
            {
                Log.WriteError($"Couldn't find car id {carId} to unequip item at invIdx {invIdx}");
                return false;
            }

            using (var rc = Database.GetContext())
            {
                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long)CID && i.InvIdx == invIdx && i.CurCarID == carId);
                if (dbItem == null)
                {
                    Log.WriteError($"Failed to find item invIdx {invIdx} carId {carId} for Character.UnEquipItem");
                    return false;
                }

                dbItem.Slot = 0;
                invItem.slot = 0;

                dbItem.CurCarID = 0;
                invItem.curCarID = 0;

                dbItem.State = 0;
                invItem.state = 0;

                rc.SaveChanges();
                addItemMod(invItem, true);
            }

            return true;
        }

        public bool GrantVehicle(int carSort, out Vehicle resultVehicle, out Item resultKeyItem, int color = 0, int grade = 1)
        {
            Log.WriteLine($"Attempting to grant CID {CID} car {carSort}");

            var vehicle = Vehicle.CreateEntry(CID, carSort, color, grade);
            resultVehicle = vehicle;
            if (vehicle == null)
            {
                resultKeyItem = null;
                return false;
            }

            var vehicleListEntry = VehicleTable.Vehicles[carSort];
            var tableKeyItem = ItemTable.Items.FirstOrDefault(i => i.Category == "car" && i.ID == vehicleListEntry.GetKeyId());

            if (tableKeyItem == null)
            {
                Log.WriteError("Failed to find key in item table");
                // TODO: roll back vehicle creation :|
                resultKeyItem = null;
                return false;
            }

            var keyItem = Item.CreateEntry(CID, tableKeyItem.ID, 1, carId: vehicle.CarID);
            resultKeyItem = keyItem;
            if (keyItem == null)
            {
                Log.WriteError("Failed to create key");
                return false;
            }

            Garage.Add(vehicle);
            Inventory.Add(keyItem);
            addItemMod(keyItem);
            return true;
        }

        public bool RemoveVehicle(int carId)
        {
            var vehicle = Garage.SingleOrDefault(v => v.CarID == carId);
            if (vehicle == null)
            {
                Log.WriteError($"Failed to find vehicle for RemoveVehicle({carId})");
                return false;
            }

            var key = Inventory.SingleOrDefault(i => i.curCarID == vehicle.CarID);
            if (key == null)
            {
                Log.WriteError($"Failed to find vehicle key for RemoveVehicle({carId})");
                return false;
            }

            using (var rc = Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.SingleOrDefault(v => v.CID == (long) CID && v.CarID == vehicle.CarID);
                var dbKey = rc.Items.SingleOrDefault(i => i.CID == (long) CID && i.CurCarID == vehicle.CarID);

                if (dbVehicle == null || dbKey == null)
                {
                    Log.WriteError($"Failed to find db counterparts of vehicle/key for RemoveVehicle({carId})");
                    return false;
                }

                rc.Vehicles.Remove(dbVehicle);
                rc.Items.Remove(dbKey);
                rc.SaveChanges();

                Inventory.Remove(key);
                Garage.Remove(vehicle);

                key.stackNum = 0;
                addItemMod(key);
            }
            return true;
        }

        // DropItem refuses anything that is not sellable, which is the wrong test
        // for using a consumable. This spends the stack and nothing else.
        public bool ConsumeItem(int invIdx, uint count = 1)
        {
            using (var rc = Database.GetContext())
            {
                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long) CID && i.InvIdx == invIdx && i.StackNum >= count);
                var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);

                if (dbItem == null || invItem == null)
                {
                    Log.WriteError("Could not consume {0} of item {1} for character {2}.", count, invIdx, CID);
                    return false;
                }

                if (count >= dbItem.StackNum)
                {
                    rc.Items.Remove(dbItem);
                    Inventory.Remove(invItem);
                    invItem.stackNum = 0;
                }
                else
                {
                    dbItem.StackNum -= (int) count;
                    invItem.stackNum -= count;
                }

                rc.SaveChanges();
                addItemMod(invItem);
                return true;
            }
        }

        public bool DropItem(int invIdx, out Item resultItem, uint count = 1)
        {
            using (var rc = Database.GetContext())
            {
                var dbItem = rc.Items.SingleOrDefault(i => i.CID == (long)CID && i.InvIdx == invIdx && i.StackNum >= count);
                var invItem = Inventory.SingleOrDefault(i => i.invIdx == invIdx);

                if (dbItem == null || invItem == null || !invItem.itemEntry.IsSellable())
                {
                    Log.WriteError($"Failed to find droppable item invIdx {invIdx} count >= {count} owned by {CID}");
                    resultItem = null;
                    return false;
                }

                bool destroyItem = count == dbItem.StackNum && count == invItem.stackNum;
                if (destroyItem)
                {
                    rc.Items.Remove(dbItem);
                    Inventory.Remove(invItem);
                    invItem.stackNum = 0;
                }
                else
                {
                    dbItem.StackNum -= (int)count;
                    invItem.stackNum -= count;
                }
                rc.SaveChanges();
                resultItem = invItem;
                addItemMod(invItem);
                return true;
            }
        }

        private void addItemMod(Item item, bool moved = false)
        {
            var itemInfo = item.GetInfo();
            var modInfo = new ItemModInfo
            {
                Item = itemInfo,
                State = moved ? 3 : itemInfo.StackNum == 0 ? 2 : 0
            };
            PendingItemMods.Add(modInfo);
        }

        public void FlushModInfo(RiceClient client)
        {
            var mods = PendingItemMods.ToArray();
            PendingItemMods.Clear();

            //ItemModList
            var packet = new RicePacket(402);
            packet.Writer.Write(mods.Length);
            foreach (var itemMod in mods)
                packet.Writer.Write(itemMod);
            client.Send(packet);

            var stat = new RicePacket(760);
            stat.Writer.Write(GetStatUpdate());
            client.Send(stat);
        } 

        public CharInfo GetInfo()
        {
            return new CharInfo
            {
                Avatar = (ushort)Avatar,
                Name = Name,
                CID = CID,
                City = City,
                Position = Position,
                PosState = PosState,
                CurrentCarID = CurrentCarID,
                QuickSlot1 = QuickSlot1,
                QuickSlot2 = QuickSlot2,
                PType = (byte)'A',
                TeamJoinDate = DateTime.Now,
                TeamLeaveDate = DateTime.Now,
                TeamCloseDate = DateTime.Now,
                ExpInfo = GetExpInfo(),
                HancoinGarage = GarageLevel,
                Level = (ushort)Level,
                TeamId = TID,
                TeamName = "", // load this
                MitoMoney = Mito,
                Flags = -1
            };
        }

        public static Character Retrieve(string charname)
        {
            Character character;

            using (var rc = Database.GetContext())
                character = new Character(rc.Characters.SingleOrDefault(c => c.Name == charname));

            return character;
        }

        public static bool IsNameUsable(string name)
        {
            using (var rc = Database.GetContext())
                return rc.Characters.Count(c => c.Name == name) == 0;
        }

        public static bool Create(ulong uid, string name, ushort avatar)
        {
            using (var rc = Database.GetContext())
            {
                rc.Characters.Add(new Server.Database.Models.Character
                {
                    UID = (long) uid,
                    Name = name,
                    Avatar = avatar,
                    TID = -1,

                    // A character left at the column defaults is unplayable: level 0
                    // indexes ExpTable[-1], and city 0 with a 0,0,0 position is not a
                    // place the client can load. Start everyone at the first city.
                    Level = 1,
                    City = 1,
                    PosX = -3372.566f,
                    PosY = 1200.367f,
                    PosZ = 85.519f,
                    PosState = 2,

                    // Matches what fix-new-character.ps1 has been granting by hand.
                    Mito = 1000000
                });

                try
                {
                    rc.SaveChanges();
                }
                catch (DataException ex)
                {
                    Log.WriteError(ex.ToString());
                    return false;
                }
            }

            try
            {
                // Beginner car: Hyundai Click, grade 1, granted the same way the
                // dealership does it so the key item and garage entry are consistent.
                // The character row above already committed, so a failure here
                // shouldn't fail character creation as a whole.
                var character = Retrieve(name);
                if (character.GrantVehicle(1, out Vehicle starterVehicle, out _, grade: 1))
                {
                    using (var rc = Database.GetContext())
                    {
                        var ch = rc.Characters.Find((long) character.CID);
                        ch.CurrentCarID = starterVehicle.CarID;
                        rc.SaveChanges();
                    }
                }
                else
                {
                    Log.WriteError($"Failed to grant starter car to new character {name}");
                }
            }
            catch (Exception ex)
            {
                Log.WriteError($"Failed to set up starter car for {name}: {ex}");
            }

            return true;
        }

        public static bool Delete(string name)
        {
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.SingleOrDefault(c => c.Name == name);
                if (ch == null)
                {
                    Log.WriteError($"Tried to delete non-existing character named {name}");
                    return false;
                }

                rc.Characters.Remove(ch);

                try
                {
                    rc.SaveChanges();
                }
                catch (DataException ex)
                {
                    Log.WriteError(ex.ToString());
                    return false;
                }
                return true;
            }
        }

        public bool SaveCarPos(int channelId, Vector4 pos, int cityId, int posState)
        {
            using (var rc = Database.GetContext())
            {
                var ch = rc.Characters.Find((long) CID);
                if (ch == null)
                {
                    Log.WriteError($"Could not find char {CID} for SaveCarPos");
                    return false;
                }

                ch.PosX = pos.X;
                ch.PosY = pos.Y;
                ch.PosZ = pos.Z;
                ch.City = cityId;
                ch.PosState = posState;
                rc.SaveChanges();
            }
            return true;
        }

        public static List<Character> Retrieve(ulong uid)
        {
            using (var rc = Database.GetContext())
            {
                // Not user.Characters: that lazy-loads through the Owner/UID
                // navigation property, which EF maps to a nonexistent Owner_ID
                // column instead of honoring [ForeignKey("UID")] - queries
                // straight off Characters like every other method here instead.
                return rc.Characters.Where(ch => ch.UID == (long)uid).Select(ch => new Character(ch)).ToList();
            }
        }


        string VisualInventoryPath()
        {
            return Path.Combine("res", string.Format("vsitems_{0}.json", CID));
        }

        void LoadVisualInventory()
        {
            VisualInventory = new List<VisualInvItem>();
            EquippedVisuals = new Dictionary<int, VisualItem>();
            try
            {
                string path = VisualInventoryPath();
                if (!File.Exists(path))
                    return;

                string json = File.ReadAllText(path);
                var loaded = JsonConvert.DeserializeObject<List<VisualInvItem>>(json);
                if (loaded != null)
                    VisualInventory = loaded;

                foreach (var item in VisualInventory.Where(i => i.State == 1))
                    ApplyEquippedVisual(item, false);
            }
            catch (Exception ex)
            {
                Log.WriteError("Failed to load visual inventory for CID {0}: {1}", CID, ex.Message);
                VisualInventory = new List<VisualInvItem>();
            }
        }

        public void SaveVisualInventory()
        {
            try
            {
                string path = VisualInventoryPath();
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllText(path, JsonConvert.SerializeObject(VisualInventory, Formatting.Indented));
            }
            catch (Exception ex)
            {
                Log.WriteError("Failed to save visual inventory for CID {0}: {1}", CID, ex.Message);
            }
        }


        public void RebuildEquippedVisuals()
        {
            EquippedVisuals = new Dictionary<int, VisualItem>();
            if (VisualInventory == null)
                return;
            foreach (var item in VisualInventory.Where(i => i.State == 1))
                ApplyEquippedVisual(item, true);
        }

        public uint NextVisualInvenIdx()
        {
            if (VisualInventory == null || VisualInventory.Count == 0)
                return 1;
            return VisualInventory.Max(i => i.InvenIdx) + 1;
        }

        public VisualInvItem GrantVisualItem(uint tableIdx, uint carId, uint period, string plateName)
        {
            var item = new VisualInvItem
            {
                InvenIdx = NextVisualInvenIdx(),
                TableIdx = tableIdx,
                CarId = carId,
                State = 0,
                Period = period,
                PlateName = plateName ?? "",
                CreateTime = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds,
                UpdateTime = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds
            };
            VisualInventory.Add(item);
            SaveVisualInventory();
            return item;
        }

        public bool EquipVisualItem(uint invenIdx, uint carId)
        {
            var item = VisualInventory.FirstOrDefault(i => i.InvenIdx == invenIdx);
            if (item == null)
            {
                Log.WriteError("EquipVisualItem: missing inv {0}", invenIdx);
                return false;
            }

            var shop = VShopTable.Get(item.TableIdx);
            int category = shop != null ? shop.Category : -1;

            foreach (var other in VisualInventory.Where(i => i.CarId == carId && i.State == 1 && i.InvenIdx != invenIdx))
            {
                var otherShop = VShopTable.Get(other.TableIdx);
                if (otherShop != null && otherShop.Category == category)
                {
                    other.State = 0;
                    other.UpdateTime = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
                }
            }

            item.CarId = carId;
            item.State = 1;
            item.UpdateTime = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            SaveVisualInventory();
            RebuildEquippedVisuals();
            return true;
        }

        public bool UnEquipVisualItem(uint invenIdx, uint carId)
        {
            var item = VisualInventory.FirstOrDefault(i => i.InvenIdx == invenIdx);
            if (item == null)
                return false;
            item.State = 0;
            item.UpdateTime = (uint)(DateTime.UtcNow - new DateTime(1970, 1, 1)).TotalSeconds;
            SaveVisualInventory();
            RebuildEquippedVisuals();
            return true;
        }

        static short ResolveVisualIdx(VShopEntry shop)
        {
            if (shop == null)
                return 0;
            // Prefer VShop categoryIdx — that is the client visual-table / mesh index.
            if (shop.CategoryIdx != 0)
                return (short)shop.CategoryIdx;
            if (!string.IsNullOrEmpty(shop.ItemCode))
            {
                // Fall back to trailing digits of item code (i_d_* style), NOT the VShop unique id.
                var digits = new string(shop.ItemCode.Where(c => char.IsDigit(c)).ToArray());
                short parsed;
                if (digits.Length > 0)
                {
                    if (digits.Length > 4)
                        digits = digits.Substring(digits.Length - 4);
                    if (short.TryParse(digits, out parsed) && parsed != 0)
                        return parsed;
                }
            }
            return 0;
        }

        /// <summary>
        /// Map a VShop entry onto an XiVisualItem field. VShop "category" is a shop UI bucket,
        /// not the XiVisualItem slot — prefer item-code prefixes for aero/neon/wheel/etc.
        /// </summary>
        static void ApplyVisualSlot(ref VisualItem vis, VShopEntry shop, short idx)
        {
            if (shop == null || idx == 0)
                return;

            string code = (shop.ItemCode ?? "").ToLowerInvariant();

            // Aero kits (pc_Aeroset*, pc_AeroMK*, exaero) → AeroSet mesh slot
            if (code.Contains("aeroset") || code.Contains("aeromk") || code.Contains("exaero")
                || code.StartsWith("pc_aero"))
            {
                vis.AeroSet = idx;
                return;
            }

            // Wings / spoilers
            if (code.Contains("aerowing") || code.Contains("boosterwing") || code.Contains("gwing")
                || code.Contains("_wing") || code.StartsWith("i_d_e_"))
            {
                vis.Spoiler = idx;
                return;
            }

            // Neon / wheel / plate / flame by classic i_d_* prefixes
            if (code.StartsWith("i_d_n_") || code.Contains("neon"))
            {
                vis.Neon = idx;
                return;
            }
            if (code.StartsWith("i_d_w_") || code.Contains("tire") || code.Contains("wheel"))
            {
                vis.Wheel = idx;
                return;
            }
            if (code.StartsWith("i_d_p_") || code.Contains("plate"))
            {
                vis.Plate = idx;
                return;
            }
            if (code.StartsWith("i_d_f_") || code.Contains("flame") || code.Contains("muffler"))
            {
                vis.MufflerFlame = idx;
                return;
            }
            if (code.Contains("bumper"))
            {
                vis.AeroBumper = idx;
                return;
            }
            if (code.Contains("intercooler") || code.Contains("interc"))
            {
                vis.AeroIntercooler = idx;
                return;
            }

            // Legacy numeric category fallback (only when item code did not classify).
            switch (shop.Category)
            {
                case 2: vis.Neon = idx; break;
                case 3: vis.Wheel = idx; break;
                case 4: vis.Plate = idx; break;
                case 5: vis.Decal = idx; break;
                case 7: vis.AeroBumper = idx; break;
                case 8: vis.AeroIntercooler = idx; break;
                case 9: vis.AeroSet = idx; break;
                case 12: vis.MufflerFlame = idx; break;
                case 13: vis.Spoiler = idx; break;
                default:
                    // Unknown — do not force DecalColor; AeroSet is the safest mesh slot.
                    vis.AeroSet = idx;
                    break;
            }
        }

        void ApplyEquippedVisual(VisualInvItem item, bool createIfMissing)
        {
            var shop = VShopTable.Get(item.TableIdx);
            if (shop == null)
                return;

            VisualItem vis;
            if (!EquippedVisuals.TryGetValue((int)item.CarId, out vis))
            {
                if (!createIfMissing && item.State != 1)
                    return;
                vis = NewEmptyVisualItem();
            }

            short idx = ResolveVisualIdx(shop);
            ApplyVisualSlot(ref vis, shop, idx);

            if (!string.IsNullOrEmpty(item.PlateName) && item.PlateName.Trim().Length > 0)
            {
                // Only treat as plate text when this really is a plate item.
                string code = (shop.ItemCode ?? "").ToLowerInvariant();
                if (code.StartsWith("i_d_p_") || code.Contains("plate") || shop.Category == 4 || shop.Category == 9)
                {
                    if (idx != 0)
                        vis.Plate = idx;
                    vis.PlateString = item.PlateName.Trim();
                }
            }

            EquippedVisuals[(int)item.CarId] = vis;
            Log.WriteLine("ApplyVisual car={0} table={1} code={2} cat={3} idx={4} => aeroSet={5} spoiler={6} neon={7} wheel={8}",
                item.CarId, item.TableIdx, shop.ItemCode, shop.Category, idx,
                vis.AeroSet, vis.Spoiler, vis.Neon, vis.Wheel);
        }

        static VisualItem NewEmptyVisualItem()
        {
            return new VisualItem
            {
                Neon = 0,
                Plate = 0,
                Decal = 0,
                DecalColor = 0,
                AeroBumper = 0,
                AeroIntercooler = 0,
                AeroSet = 0,
                MufflerFlame = 0,
                Wheel = 0,
                Spoiler = 0,
                Reserve = new short[6],
                PlateString = ""
            };
        }

        public void WriteVisualItemEntry(PacketWriter writer, VisualInvItem item)
        {
            // XiStrMyVSItem fields (48) padded to 120-byte US list stride
            long start = writer.BaseStream.Position;
            writer.Write(item.CarId);          // 4
            writer.Write(item.State);          // 4
            writer.Write(item.TableIdx);       // 4
            writer.Write(item.InvenIdx);       // 4
            writer.WriteUnicodeStatic(item.PlateName ?? "", 10); // 20
            writer.Write(item.Period);         // 4
            writer.Write(item.UpdateTime);     // 4
            writer.Write(item.CreateTime);     // 4
            // = 48 so far
            int written = (int)(writer.BaseStream.Position - start);
            if (written < 120)
                writer.Write(new byte[120 - written]);
        }

        public void SendVisualItemList(RiceClient client)
        {
            var ack = new RicePacket(1201);
            ack.Writer.Write(262144); // ListUpdate first
            int count = VisualInventory != null ? VisualInventory.Count : 0;
            ack.Writer.Write(count);
            if (count == 0)
            {
                // Keep prior empty-list behaviour that already works for login
                ack.Writer.Write(new byte[120]);
            }
            else
            {
                foreach (var item in VisualInventory)
                    WriteVisualItemEntry(ack.Writer, item);
            }
            client.Send(ack);
            Log.WriteLine(string.Format("SendVisualItemList 1201 count={0} cid={1}", count, CID));
        }

        public bool SpendHancoin(uint amount, User user)
        {
            if (user == null)
                return false;
            if (user.Credits < amount)
                return false;
            user.Credits -= amount;
            using (var rc = Database.GetContext())
            {
                var dbUser = rc.Users.Find((long)user.UID);
                if (dbUser == null)
                {
                    Log.WriteError("SpendHancoin: missing user {0}", user.UID);
                    return false;
                }
                dbUser.Credits = user.Credits;
                rc.SaveChanges();
            }
            return true;
        }

        public StatUpdate GetStatUpdate()
        {
            var vehicleEntry = Vehicle.VehicleUpgradeEntry;
            var listEntry = Vehicle.VehicleListEntry;
            var itemsOnVehicle = Inventory
                .Where(item => item.itemEntry is ItemTableEntry && item.curCarID == Vehicle.CarID)
                .Select(item => item.itemEntry as ItemTableEntry)
                .ToList();

            int equipAccel = 0;
            int equipDura = 0;
            int equipSpeed = 0;
            int equipBoost = 0;

            // First pass: raw part / op points (docks have basepoints=0; they boost via partassist).
            foreach (var item in itemsOnVehicle)
            {
                int pts = (int)item.BasePoints;
                switch (item.Category)
                {
                    case "accel":
                    case "op_A":
                        equipAccel += pts;
                        break;
                    case "crash":
                    case "op_C":
                        equipDura += pts;
                        break;
                    case "speed":
                    case "op_S":
                        equipSpeed += pts;
                        break;
                    case "boost":
                    case "op_B":
                        equipBoost += pts;
                        break;
                    case "op_F":
                        equipAccel += pts;
                        equipDura += pts;
                        equipSpeed += pts;
                        equipBoost += pts;
                        break;
                }
            }

            int partAccel = equipAccel;
            int partDura = equipDura;
            int partSpeed = equipSpeed;
            int partBoost = equipBoost;

            // Second pass: docking assists (docking_p = % of category part points, docking_b = flat).
            int dockAccel = 0, dockDura = 0, dockSpeed = 0, dockBoost = 0;
            foreach (var item in itemsOnVehicle)
            {
                if (item.Category == null || !item.Category.StartsWith("dock_"))
                    continue;
                if (string.IsNullOrEmpty(item.PartAssist) || item.PartAssist.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                    continue;

                var assist = AssistTable.Assists.FirstOrDefault(a =>
                    a.ID != null && a.ID.Equals(item.PartAssist, StringComparison.OrdinalIgnoreCase));
                if (assist == null || string.IsNullOrEmpty(assist.Stat) || assist.Stat.Equals("n/a", StringComparison.OrdinalIgnoreCase))
                    continue;

                double statVal;
                if (!double.TryParse(assist.Stat, System.Globalization.NumberStyles.Float,
                        System.Globalization.CultureInfo.InvariantCulture, out statVal))
                    continue;

                int bonus = 0;
                string fn = (assist.Function ?? "").ToLowerInvariant();
                if (fn == "docking_p")
                {
                    int basePts = 0;
                    switch (item.Category)
                    {
                        case "dock_A": basePts = partAccel; break;
                        case "dock_C": basePts = partDura; break;
                        case "dock_S": basePts = partSpeed; break;
                        case "dock_B": basePts = partBoost; break;
                    }
                    bonus = (int)(basePts * statVal / 100.0);
                }
                else if (fn == "docking_b")
                {
                    bonus = (int)statVal;
                }
                else
                    continue;

                switch (item.Category)
                {
                    case "dock_A": dockAccel += bonus; equipAccel += bonus; break;
                    case "dock_C": dockDura += bonus; equipDura += bonus; break;
                    case "dock_S": dockSpeed += bonus; equipSpeed += bonus; break;
                    case "dock_B": dockBoost += bonus; equipBoost += bonus; break;
                }
            }

            int totalSpeed = vehicleEntry.Speed + equipSpeed + Level;
            int totalDura = vehicleEntry.Durability + equipDura + Level;
            int totalAccel = vehicleEntry.Acceleration + equipAccel + Level;
            int totalBoost = vehicleEntry.Boost + equipBoost + Level;

            // Left-column performance metrics (mph / accel-sec / crash range / boost-sec).
            // Wire layout AFTER StatInfo ints: Perf(40) then EnChantBonus(32 US) + pad4 = 76.
            // Do NOT put EnChant before Perf — that made left column read 0 and Assist show mph.
            float mph = 90f + totalSpeed * 0.32f;
            float accelSec = Math.Max(2.0f, 14.5f - totalAccel / 55f);
            float crashMin = Math.Max(10f, totalDura * 0.35f);
            float crashMax = Math.Max(crashMin + 5f, totalDura * 0.75f);
            float boostSec = Math.Max(1.0f, totalBoost / 110f);

            // Hex-log full StatUpdate body (80 StatInfo + 76 trailer) as it will appear on the wire.
            var body = new byte[156];
            int o = 0;
            Buffer.BlockCopy(BitConverter.GetBytes(vehicleEntry.Speed), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(vehicleEntry.Durability), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(vehicleEntry.Acceleration), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(vehicleEntry.Boost), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(equipSpeed), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(equipDura), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(equipAccel), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(equipBoost), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(Level), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(Level), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(Level), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(Level), 0, body, o, 4); o += 4;
            o += 16; // ItemUse zeros
            Buffer.BlockCopy(BitConverter.GetBytes(totalSpeed), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(totalDura), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(totalAccel), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(totalBoost), 0, body, o, 4); o += 4;
            // Perf 40
            Buffer.BlockCopy(BitConverter.GetBytes(mph), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(accelSec), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(crashMin), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(crashMax), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(boostSec), 0, body, o, 4); o += 4;
            o += 20; // pad
            // EnChant 32 (US, no AddSpeed) — mitron floats only
            o += 16; // enchant ints zero
            o += 8;  // Drop/Exp zero
            Buffer.BlockCopy(BitConverter.GetBytes(vehicleEntry.MitronCapacity), 0, body, o, 4); o += 4;
            Buffer.BlockCopy(BitConverter.GetBytes(vehicleEntry.MitronEfficiency), 0, body, o, 4); o += 4;
            // pad4 zero

            Log.WriteLine(
                "StatUpdate {0} car={1}/{2} g={3} lvl={4} | base {5}/{6}/{7}/{8} equip {9}/{10}/{11}/{12} (parts {13}/{14}/{15}/{16} docks {17}/{18}/{19}/{20}) | totals {21}/{22}/{23}/{24} | perf mph={25:0.#} accelSec={26:0.##} crash={27:0.#}-{28:0.#} boostSec={29:0.##} | body156={30}",
                Name, Vehicle.CarID, Vehicle.CarType, Vehicle.Grade, Level,
                vehicleEntry.Speed, vehicleEntry.Durability, vehicleEntry.Acceleration, vehicleEntry.Boost,
                equipSpeed, equipDura, equipAccel, equipBoost,
                partSpeed, partDura, partAccel, partBoost,
                dockSpeed, dockDura, dockAccel, dockBoost,
                totalSpeed, totalDura, totalAccel, totalBoost,
                mph, accelSec, crashMin, crashMax, boostSec,
                BitConverter.ToString(body));
            Log.WriteLine("StatUpdate verify totals@64 ints={0}/{1}/{2}/{3}",
                BitConverter.ToInt32(body, 64), BitConverter.ToInt32(body, 68),
                BitConverter.ToInt32(body, 72), BitConverter.ToInt32(body, 76));

            return new StatUpdate
            {
                BaseAcceleration = vehicleEntry.Acceleration,
                BaseDurability = vehicleEntry.Durability,
                BaseSpeed = vehicleEntry.Speed,
                BaseBoost = vehicleEntry.Boost,

                CharAcceleration = Level,
                CharDurability = Level,
                CharSpeed = Level,
                CharBoost = Level,

                EquipAcceleration = equipAccel,
                EquipDurability = equipDura,
                EquipSpeed = equipSpeed,
                EquipBoost = equipBoost,

                PerfSpeed = mph,
                PerfAcceleration = accelSec,
                PerfCrashMin = crashMin,
                PerfCrashMax = crashMax,
                PerfBoost = boostSec,

                MitronCapacity = vehicleEntry.MitronCapacity,
                MitronEfficiency = vehicleEntry.MitronEfficiency,
            };
        }

    }

    public class VisualInvItem
    {
        public uint InvenIdx;
        public uint TableIdx;
        public uint CarId;
        public uint State; // 0 inventory, 1 equipped
        public uint Period;
        public string PlateName;
        public uint CreateTime;
        public uint UpdateTime;
    }
}
