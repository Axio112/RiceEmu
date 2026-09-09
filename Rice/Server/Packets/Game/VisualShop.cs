using Rice.Server.Core;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Game;
using Rice.Server.Structures;
using Rice.Server.Structures.Resources;

namespace Rice.Server.Packets.Game
{
    public static class VisualShop
    {
        [RicePacket(1400, RiceServer.ServerType.Game)]
        public static void GetMyHancoin(RicePacket packet)
        {
            var ack = new RicePacket(1401);
            ack.Writer.Write((uint)packet.Sender.Player.User.Credits);
            ack.Writer.Write(0L); // mileage?? masangpluto pls
            packet.Sender.Send(ack);
        }

        [RicePacket(1203, RiceServer.ServerType.Game)]
        public static void BuyVisualItem(RicePacket packet)
        {
            // Client (capture): tableIdx, carId, plate[10], unknown1[20], periodIdx, useMileage(u16), curCash(i64)
            // ZoneServer.h omits unknown1; live US client includes it.
            long payloadLen = packet.Reader.BaseStream.Length - packet.Reader.BaseStream.Position;
            uint tableIdx = packet.Reader.ReadUInt32();
            uint carId = packet.Reader.ReadUInt32();
            string plateName = packet.Reader.ReadUnicodeStatic(10);
            // Skip 20-byte unknown block when present (longer client packet)
            if (payloadLen >= 62)
                packet.Reader.ReadBytes(20);
            uint periodIdx = packet.Reader.ReadUInt32();
            ushort useMileage = packet.Reader.ReadUInt16();
            long curCash = packet.Reader.ReadInt64();

            Log.WriteLine($"BuyVisualItem attempt idx={tableIdx} car={carId} plate='{plateName}' period={periodIdx} mileage={useMileage} cash={curCash} payloadLen={payloadLen}");

            var character = packet.Sender.Player.ActiveCharacter;
            var user = packet.Sender.Player.User;
            if (character == null)
            {
                Log.WriteLine("BuyVisualItem FAIL: no active character");
                packet.Sender.Error("No active character");
                return;
            }

            var shopItem = VShopTable.Get(tableIdx);
            if (shopItem == null)
            {
                Log.WriteLine($"BuyVisualItem FAIL: unknown VShop id {tableIdx}");
                packet.Sender.Error("Unknown visual item");
                return;
            }

            long price = shopItem.GetPrice(periodIdx);
            bool paid = true;
            if (price > 0)
            {
                if (shopItem.IsHancoinPurchase())
                {
                    paid = character.SpendHancoin((uint)price, user);
                    if (!paid)
                    {
                        Log.WriteLine($"BuyVisualItem: insufficient hancoin ({user.Credits} < {price}), granting free for offline debug");
                        paid = true; // offline private server: still grant
                    }
                }
                else
                {
                    paid = character.SpendMito((ulong)price);
                    if (!paid)
                    {
                        Log.WriteLine($"BuyVisualItem: insufficient mito ({character.Mito} < {price}), granting free for offline debug");
                        paid = true;
                    }
                }
            }

            var granted = character.GrantVisualItem(tableIdx, carId, periodIdx, plateName);
            Log.WriteLine($"BuyVisualItem OK: granted table={tableIdx} inv={granted.InvenIdx} car={carId} to {character.Name} (cid={character.CID})");

            // Auto-equip onto the target car
            bool equipped = character.EquipVisualItem(granted.InvenIdx, carId);
            Log.WriteLine($"BuyVisualItem equip inv={granted.InvenIdx} car={carId} result={equipped}");

            // BS_PktBuyVisualItemAck (ZoneServer.h): all int/uint 4-byte fields — Mito is int32 NOT int64
            var ack = new RicePacket(1204);
            ack.Writer.Write((uint)0);                       // Type (Visual=0)
            ack.Writer.Write(tableIdx);                      // TableIdx
            ack.Writer.Write(carId);                         // CarID
            ack.Writer.Write(granted.InvenIdx);              // InvenIdx
            ack.Writer.Write((int)periodIdx);                // Period
            ack.Writer.Write((int)character.Mito);           // Mito (int32!)
            ack.Writer.Write((int)user.Credits);             // Hancoin
            ack.Writer.Write(0);                             // BonusMito
            ack.Writer.Write(0);                             // Mileage
            packet.Sender.Send(ack);
            Log.WriteLine($"BuyVisualItem sent 1204 ack (mito={(int)character.Mito} hancoin={(int)user.Credits})");

            // Refresh visual inventory — opcode 1201 (VisualItemListAck), NOT 1801
            character.SendVisualItemList(packet.Sender);

            // Best-effort VisualUpdate 1061
            SendVisualUpdate(packet.Sender, character, (int)carId);

            // Refresh hancoin display
            var coinAck = new RicePacket(1401);
            coinAck.Writer.Write((uint)user.Credits);
            coinAck.Writer.Write(0L);
            packet.Sender.Send(coinAck);
        }

        [RicePacket(1205, RiceServer.ServerType.Game)]
        public static void EquipVisualItem(RicePacket packet)
        {
            // ZoneServer: InvenIdx, DstSlotIdx, CarID ? also tolerate CarID-first capture layouts.
            long payloadLen = packet.Reader.BaseStream.Length - packet.Reader.BaseStream.Position;
            byte[] raw = packet.Reader.ReadBytes((int)payloadLen);
            packet.Reader.BaseStream.Position -= raw.Length;

            uint invenIdx = packet.Reader.ReadUInt32();
            uint dstSlotIdx = packet.Reader.ReadUInt32();
            uint carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            if (character == null)
                return;

            Log.WriteLine(string.Format("EquipVisualItem inv={0} slot={1} car={2} raw={3}",
                invenIdx, dstSlotIdx, carId, BitConverter.ToString(raw)));

            // If inv looks bogus but carId looks like an inv index, try swapped layout.
            if (character.VisualInventory != null
                && character.VisualInventory.All(i => i.InvenIdx != invenIdx)
                && character.VisualInventory.Any(i => i.InvenIdx == carId))
            {
                uint tmp = invenIdx;
                invenIdx = carId;
                carId = dstSlotIdx;
                dstSlotIdx = tmp;
                Log.WriteLine(string.Format("EquipVisualItem swapped parse inv={0} car={1}", invenIdx, carId));
            }

            bool ok = character.EquipVisualItem(invenIdx, carId);
            if (!ok)
            {
                // Still ACK so the client does not toast "Item Equipping Error".
                Log.WriteLine(string.Format("EquipVisualItem FAIL inv={0} (acking anyway)", invenIdx));
            }

            var ack = new RicePacket(1206);
            ack.Writer.Write(invenIdx);
            ack.Writer.Write(dstSlotIdx);
            ack.Writer.Write(carId);
            packet.Sender.Send(ack);

            character.SendVisualItemList(packet.Sender);
            SendVisualUpdate(packet.Sender, character, (int)carId);
        }

        [RicePacket(1207, RiceServer.ServerType.Game)]
        public static void UnEquipVisualItem(RicePacket packet)
        {
            uint invenIdx = packet.Reader.ReadUInt32();
            uint carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            if (character == null)
                return;

            Log.WriteLine($"UnEquipVisualItem inv={invenIdx} car={carId}");

            if (!character.UnEquipVisualItem(invenIdx, carId))
            {
                // Do not Error-toast — ACK anyway so client UI stays stable.
                Log.WriteLine(string.Format("UnEquipVisualItem FAIL inv={0} (acking anyway)", invenIdx));
            }

            var ack = new RicePacket(1208);
            ack.Writer.Write(invenIdx);
            ack.Writer.Write(carId);
            packet.Sender.Send(ack);

            character.SendVisualItemList(packet.Sender);
            SendVisualUpdate(packet.Sender, character, (int)carId);
        }

        static void SendVisualUpdate(RiceClient client, Rice.Game.Character character, int carId)
        {
            character.RebuildEquippedVisuals();
            VisualItem vis;
            if (!character.EquippedVisuals.TryGetValue(carId, out vis))
            {
                vis = new VisualItem
                {
                    Reserve = new short[6],
                    PlateString = ""
                };
            }
            if (vis.Reserve == null)
                vis.Reserve = new short[6];
            if (vis.PlateString == null)
                vis.PlateString = "";

            try
            {
                // Serial + Age + CarId + XiVisualItem (US-friendly VisualUpdate)
                var update = new RicePacket(1061);
                update.Writer.Write(character.Serial);
                update.Writer.Write((ushort)0);
                update.Writer.Write(carId);
                update.Writer.Write(vis);
                client.Send(update);
                Log.WriteLine(string.Format(
                    "VisualUpdate 1061 sent car={0} neon={1} plate={2} aeroBump={3} aeroIC={4} aeroSet={5} wheel={6} spoiler={7}",
                    carId, vis.Neon, vis.Plate, vis.AeroBumper, vis.AeroIntercooler, vis.AeroSet, vis.Wheel, vis.Spoiler));
            }
            catch (Exception ex)
            {
                Log.WriteError("VisualUpdate 1061 failed: {0}", ex.Message);
            }
        }

        [RicePacket(1300, RiceServer.ServerType.Game)]
        public static void BuyHistoryList(RicePacket packet)
        {
            uint offset = packet.Reader.ReadUInt32();
            uint count = packet.Reader.ReadUInt32();
            uint tab = packet.Reader.ReadUInt32(); // 1 = purchase history, 2 = sent gift, 3 = received gift
            Log.WriteDebug($"BuyHistoryList offset {offset} count {count} tab {tab}");

            var ack = new RicePacket(1301);
            ack.Writer.Write(offset);
            ack.Writer.Write(0); // count
            ack.Writer.Write(tab);
            packet.Sender.Send(ack);
        }

        [RicePacket(1306, RiceServer.ServerType.Game)]
        public static void IsValidCharName(RicePacket packet)
        {
            string charName = packet.Reader.ReadUnicodeStatic(21);
            Log.WriteDebug($"IsValidCharName name {charName}");
        }
    }
}
