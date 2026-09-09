using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    public static class Misc
    {
        [RicePacket(784, RiceServer.ServerType.Game)]
        public static void GetDateTime(RicePacket packet)
        {
            /*
            var ack = new RicePacket(785);
            var timestamp = (Int32)DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds;
            ack.Writer.Write(packet.Reader.ReadUInt32());
            ack.Writer.Write(packet.Reader.ReadUInt32());
            ack.Writer.Write(packet.Reader.ReadSingle());
            ack.Writer.Write(Environment.TickCount);
            ack.Writer.Write(timestamp);
            ack.Writer.Write(1);
            ack.Writer.Write((short)DateTime.Now.Year);
            ack.Writer.Write((short)DateTime.Now.Month);
            ack.Writer.Write((short)DateTime.Now.Day);
            ack.Writer.Write((short)DateTime.Now.DayOfWeek);
            ack.Writer.Write((byte)DateTime.Now.Hour);
            ack.Writer.Write((byte)DateTime.Now.Minute);
            ack.Writer.Write((byte)DateTime.Now.Second);
            ack.Writer.Write((byte)0);
            packet.Sender.Send(ack);
            */
        }
        [RicePacket(120, RiceServer.ServerType.Game)]
        public static void CheckInGame(RicePacket packet)
        {
            Log.WriteLine("CheckInGame");
            var version = packet.Reader.ReadUInt32();
            var ticket = packet.Reader.ReadUInt32();
            var username = packet.Reader.ReadUnicodeStatic(32);
            // Followed by int m_IsPcBang, 21 bytes of weird shit we don't know about, and yet another 21 byte chunk of weird shit we don't know about.

            var serverTicket = RiceServer.GetTicket(ticket);

            if (serverTicket == null || !serverTicket.ValidateOrigin(packet.Sender, username))
            {
                Log.WriteLine("Ticket is non-existent or invalid for current user.");
                Log.WriteLine("ticket: {0}, packet sender: {1}, username: {2}", serverTicket.Identifier, packet.Sender.GetRemoteIP(), username);
                packet.Sender.Error("water u even doin");
                return;
            }

            packet.Sender.Player = serverTicket.GetOwner();
            packet.Sender.Player.GameClient = packet.Sender;

            var ack = new RicePacket(121);
            ack.Writer.Write(1L);
            packet.Sender.Send(ack);
        }

        [RicePacket(125, RiceServer.ServerType.Game)]
        public static void JoinChannel(RicePacket packet)
        {
            var serial = RiceServer.CreateSerial(packet.Sender);

            var ack = new RicePacket(126);
            ack.Writer.WriteUnicodeStatic("speeding", 10);
            ack.Writer.WriteUnicodeStatic(packet.Sender.Player.ActiveCharacter.Name, 16);
            ack.Writer.Write(serial.Identifier);
            ack.Writer.Write((ushort)123); // Age
            packet.Sender.Send(ack);
        }

        [RicePacket(4, RiceServer.ServerType.Game)]
        public static void Latency(RicePacket packet)
        {
            // TODO: log SkidRush and check this

            float time = packet.Reader.ReadSingle();
            //Log.WriteLine("Latency: {0}", time);

            var ack = new RicePacket(4); // no idea if this is right, pls check Cmd_Latency on client
            ack.Writer.Write(time);
            packet.Sender.Send(ack);
        }
        
        [RicePacket(3916, RiceServer.ServerType.Game)]
        public static void LatencyRelated(RicePacket packet)
        {
            // TODO: log SkidRush and check this
            // possibly Cmd_PartyPing (client's Cmd_PartyPing handler is 3915)

            float time = packet.Reader.ReadSingle();
            //Log.WriteLine("Latency??: {0}", BitConverter.ToString(packet.Buffer));
        }

        [RicePacket(3917, RiceServer.ServerType.Game)]
        public static void UnknownSync(RicePacket packet)
        {
            // hide sync packets for now
        }

        [RicePacket(723, RiceServer.ServerType.Game)]
        public static void FuelChargeReq(RicePacket packet)
        {
            uint carId = packet.Reader.ReadUInt32();
            long pay = packet.Reader.ReadInt64();
            float requested = packet.Reader.ReadSingle();

            var character = packet.Sender.Player.ActiveCharacter;
            var vehicle = character.Garage.FirstOrDefault(veh => veh.CarID == (int) carId);

            if (vehicle == null)
            {
                Log.WriteError("FuelChargeReq for car {0}, which the character does not own.", carId);
                return;
            }

            if (vehicle.VehicleUpgradeEntry == null)
            {
                Log.WriteError("FuelChargeReq for car {0} with no upgrade entry, cannot know its capacity.", carId);
                return;
            }

            float capacity = vehicle.VehicleUpgradeEntry.MitronCapacity;

            // Deliver at most what fits in the tank, and never charge for more than
            // the character can pay.
            float delta = requested;
            if (vehicle.Mitron + delta > capacity)
                delta = capacity - vehicle.Mitron;
            if (delta < 0f)
                delta = 0f;

            if (pay < 0)
                pay = 0;
            if (pay > character.Mito)
                pay = character.Mito;

            if (pay > 0)
                character.SpendMito((ulong) pay);

            vehicle.Mitron += delta;

            using (var rc = Rice.Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.FirstOrDefault(
                    veh => veh.CID == (long) character.CID && veh.CarID == vehicle.CarID);

                if (dbVehicle != null)
                {
                    dbVehicle.Mitron = vehicle.Mitron;
                    rc.SaveChanges();
                }
            }

            var ack = new RicePacket(724);
            ack.Writer.Write(carId);
            ack.Writer.Write(pay);
            ack.Writer.Write(delta);
            ack.Writer.Write(character.Mito);
            ack.Writer.Write(vehicle.Mitron);
            ack.Writer.Write(20.0f); // unit price per mitron
            ack.Writer.Write(15.0f); // discounted unit price
            ack.Writer.Write(capacity);
            ack.Writer.Write(vehicle.VehicleUpgradeEntry.MitronEfficiency);
            ack.Writer.Write(0); // sale flag
            ack.Writer.Write(new byte[4]);
            packet.Sender.Send(ack);

            Log.WriteLine("Refuelled car {0}: +{1} mitron ({2}/{3}) for {4} mito.",
                carId, delta, vehicle.Mitron, capacity, pay);
        }

        // Payload is two little-endian words: the inventory index, then how many.
        [RicePacket(417, RiceServer.ServerType.Game)]
        public static void ItemUse(RicePacket packet)
        {
            uint invIdx = packet.Reader.ReadUInt32();
            uint count = packet.Reader.ReadUInt32();

            if (count == 0)
                count = 1;

            var character = packet.Sender.Player.ActiveCharacter;
            var item = character.Inventory.FirstOrDefault(i => i.invIdx == invIdx);

            if (item == null || item.itemEntry == null)
            {
                Log.WriteError("ItemUse for inventory slot {0}, which holds nothing.", invIdx);
                return;
            }

            var useEntry = item.itemEntry as Rice.Server.Structures.Resources.UseItemTableEntry;

            if (useEntry == null)
            {
                Log.WriteError("ItemUse for {0}, which is not a usable item.", item.itemEntry.ID);
                return;
            }

            if (useEntry.Category != "fuel")
            {
                Log.WriteLine("ItemUse: category '{0}' ({1}) is not implemented yet.",
                    useEntry.Category, useEntry.ID);
                return;
            }

            var vehicle = character.Vehicle;

            if (vehicle == null || vehicle.VehicleUpgradeEntry == null)
            {
                Log.WriteError("ItemUse: no active vehicle to refuel.");
                return;
            }

            // The item table records fuel in millilitres while tank capacity is in
            // litres, so a "20L" bottle carries a stat of 20000.
            float millilitres;
            if (!float.TryParse(useEntry.StatModifier, out millilitres))
            {
                Log.WriteError("ItemUse: {0} has an unreadable fuel value '{1}'.",
                    useEntry.ID, useEntry.StatModifier);
                return;
            }

            float capacity = vehicle.VehicleUpgradeEntry.MitronCapacity;
            float before = vehicle.Mitron;
            float after = before + (millilitres / 1000f) * count;

            if (after > capacity)
                after = capacity;

            // Spending a bottle into a full tank throws it away for nothing.
            if (after <= before)
            {
                Log.WriteLine("ItemUse: tank already full ({0} of {1}), keeping {2}.",
                    before, capacity, useEntry.ID);
                return;
            }

            vehicle.Mitron = after;

            using (var rc = Rice.Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.FirstOrDefault(
                    veh => veh.CID == (long) character.CID && veh.CarID == vehicle.CarID);

                if (dbVehicle != null)
                {
                    dbVehicle.Mitron = vehicle.Mitron;
                    rc.SaveChanges();
                }
            }

            character.ConsumeItem((int) invIdx, count);

            var ack = new RicePacket(418);
            ack.Writer.Write(invIdx);
            ack.Writer.Write(count);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);

            Log.WriteLine("Used {0} x{1}: fuel {2} -> {3} of {4}.",
                useEntry.ID, count, before, vehicle.Mitron, capacity);
        }

        // Payload is an index word followed by the item table indices the client
        // wants in its quick slots.
        [RicePacket(2000, RiceServer.ServerType.Game)]
        public static void UpdateQuickSlot(RicePacket packet)
        {
            uint index = packet.Reader.ReadUInt32();
            uint slot1 = packet.Reader.ReadUInt32();
            uint slot2 = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            character.SaveQuickSlots(slot1, slot2);

            // The payload is six words and only three are understood so far, so keep
            // the raw bytes next to the parse until the layout is confirmed.
            Log.WriteLine("Quick slots for {0}: index {1}, slots {2}/{3} | raw {4}",
                character.Name, index, slot1, slot2,
                packet.Buffer == null ? "(empty)" : BitConverter.ToString(packet.Buffer));
        }
    }
}
