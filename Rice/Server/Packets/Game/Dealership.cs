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
    public static class Dealership
    {
        [RicePacket(85, RiceServer.ServerType.Game)]
        public static void BuyCar(RicePacket packet)
        {
            Log.WriteDebug($"buycar {BitConverter.ToString(packet.Buffer)}");
            string charName = packet.Reader.ReadUnicodeStatic(21); // what the fug npludo
            uint carId = packet.Reader.ReadUInt32();
            uint color = packet.Reader.ReadUInt32();

            Log.WriteDebug($"char {charName} buying car {carId} color {color}");
            if (!VehicleTable.Vehicles.ContainsKey((int) carId))
            {
                packet.Sender.Error("fuckity fuck off");
                Log.WriteError("Attempted to purchase vehicle not in table");
                return;
            }

            var vehEntry = VehicleTable.Vehicles[(int) carId];
            if (vehEntry.HasCondition())
            {
                Log.WriteError("Attempted to purchase car that has not-null sell condition, not implemented");
                return;
            }

            int grade = vehEntry.Level;
            var vehUpgradeEntry = VehicleTable.VehicleUpgrades[(int) carId][grade - 1];
            int price = vehUpgradeEntry.Price;

            var character = packet.Sender.Player.ActiveCharacter;

            bool tookMoney = character.SpendMito((ulong) price);
            if (!tookMoney)
            {
                Log.WriteError($"Failed to spend mito for vehicle {carId}");
                return;
            }

            Vehicle givenVehicle;
            Item givenKey;
            bool gaveVehicle = character.GrantVehicle((int) carId, out givenVehicle, out givenKey, (int) color, grade);
            if (!gaveVehicle)
            {
                Log.WriteError($"Failed to grant vehicle {carId}");
                return;
            }

            bool selectedCar = character.SelectCar(givenVehicle.CarID);
            if (!selectedCar)
            {
                Log.WriteError($"Failed to select vehicle {carId}");
                return;
            }

            var ack = new RicePacket(86);
            ack.Writer.Write(givenVehicle.GetInfo());
            ack.Writer.Write(price);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }

        [RicePacket(87, RiceServer.ServerType.Game)]
        public static void SellCar(RicePacket packet)
        {
            string charName = packet.Reader.ReadUnicodeStatic(21); // what the fug npludo
            uint carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            if (carId == character.CurrentCarID)
            {
                packet.Sender.Error("dude you're driving that");
                return;
            }

            var vehicle = character.Garage.FirstOrDefault(v => v.CarID == carId);
            if (vehicle == null)
            {
                packet.Sender.Error("fuck you man this aint your car");
                Log.WriteError($"Client attempted to select invalid vehicle id {carId}");
                return;
            }

            var price = vehicle.VehicleUpgradeEntry.SellPrice;

            bool tookVehicle = character.RemoveVehicle((int) carId);
            if (!tookVehicle)
            {
                Log.WriteError($"Failed to take vehicle {carId} for SellCar");
                return;
            }

            bool gaveMoney = character.GrantMito((ulong) price);
            if (!gaveMoney)
            {
                packet.Sender.Error("took your car but you aint gettin any mito sry");
                Log.WriteError($"Failed to give mito for vehicle {carId} for SellCar");
                return;
            }

            var ack = new RicePacket(88);
            ack.Writer.Write(carId);
            ack.Writer.Write((int)price);
            packet.Sender.Send(ack);

            character.FlushModInfo(packet.Sender);
        }

        [RicePacket(89, RiceServer.ServerType.Game)]
        public static void SelectCar(RicePacket packet)
        {
            uint carId = packet.Reader.ReadUInt32();

            var character = packet.Sender.Player.ActiveCharacter;

            var vehicle = character.Garage.FirstOrDefault(v => v.CarID == carId);
            if (vehicle == null)
            {
                packet.Sender.Error("fuck you man this aint your car");
                Log.WriteError($"Client attempted to select invalid vehicle id {carId}");
                return;
            }

            character.SelectCar((int) carId);

            var ack = new RicePacket(90);
            ack.Writer.Write(vehicle.GetInfo());
            packet.Sender.Send(ack);

            var stat = new RicePacket(760);
            stat.Writer.Write(character.GetStatUpdate());
            packet.Sender.Send(stat);
        }


        [RicePacket(91, RiceServer.ServerType.Game)]
        public static void UpgradeCar(RicePacket packet)
        {
            uint carId = packet.Reader.ReadUInt32();
            var character = packet.Sender.Player.ActiveCharacter;
            if (character == null)
                return;

            var vehicle = character.Garage.FirstOrDefault(v => v.CarID == (int)carId);
            if (vehicle == null)
            {
                Log.WriteLine(string.Format("UpgradeCar: car {0} not owned by {1}", carId, character.Name));
                // Soft-fail with ack of current grade so client does not hard-lock.
                var fail = new RicePacket(92);
                fail.Writer.Write(carId);
                fail.Writer.Write(1u);
                fail.Writer.Write((uint)character.Mito);
                packet.Sender.Send(fail);
                return;
            }

            int currentGrade = (int)vehicle.Grade;
            int nextGrade = currentGrade + 1;
            if (nextGrade > 9)
            {
                Log.WriteLine(string.Format("UpgradeCar: car {0} already max grade {1}", carId, currentGrade));
                var maxAck = new RicePacket(92);
                maxAck.Writer.Write(carId);
                maxAck.Writer.Write(vehicle.Grade);
                maxAck.Writer.Write((uint)character.Mito);
                packet.Sender.Send(maxAck);
                return;
            }

            VehicleUpgradeEntry nextEntry = null;
            try
            {
                nextEntry = VehicleTable.GetVehicleUpgrade((int)vehicle.CarType, nextGrade);
            }
            catch (Exception ex)
            {
                // Relax class/level gate: if the next row exists in the table list, use it even if IsValidGrade is picky.
                Log.WriteLine(string.Format("UpgradeCar GetVehicleUpgrade failed ({0}), trying raw table", ex.Message));
                List<VehicleUpgradeEntry> list;
                if (VehicleTable.VehicleUpgrades.TryGetValue((int)vehicle.CarType, out list)
                    && nextGrade - 1 < list.Count)
                {
                    nextEntry = list[nextGrade - 1];
                    if (nextEntry == null || string.IsNullOrEmpty(nextEntry.Name))
                        nextEntry = null;
                }
            }

            if (nextEntry == null)
            {
                Log.WriteLine(string.Format("UpgradeCar: no grade {0} for carType {1} (client class/level toast bypassed server-side)", nextGrade, vehicle.CarType));
                // Still report current state ? client showed "Modification is not allowed..." when we sent nothing.
                var none = new RicePacket(92);
                none.Writer.Write(carId);
                none.Writer.Write(vehicle.Grade);
                none.Writer.Write((uint)character.Mito);
                packet.Sender.Send(none);
                return;
            }

            int cost = nextEntry.UpgradeCost;
            if (cost < 0) cost = 0;
            if (cost > 0 && !character.SpendMito((ulong)cost))
            {
                Log.WriteLine(string.Format("UpgradeCar: insufficient mito need={0} have={1}", cost, character.Mito));
                packet.Sender.Error("Not enough Mito for upgrade.");
                return;
            }

            vehicle.Grade = (uint)nextGrade;
            vehicle.VehicleUpgradeEntry = nextEntry;

            using (var rc = Rice.Database.GetContext())
            {
                var dbVehicle = rc.Vehicles.FirstOrDefault(
                    veh => veh.CID == (long)character.CID && veh.CarID == vehicle.CarID);
                if (dbVehicle != null)
                {
                    dbVehicle.Grade = nextGrade;
                    rc.SaveChanges();
                }
            }

            var ack = new RicePacket(92); // UpgradeCarAck: CarID, Grade, Gold(mito)
            ack.Writer.Write(carId);
            ack.Writer.Write(vehicle.Grade);
            ack.Writer.Write((uint)character.Mito);
            packet.Sender.Send(ack);

            var select = new RicePacket(90);
            select.Writer.Write(vehicle.GetInfo());
            packet.Sender.Send(select);

            var stat = new RicePacket(760);
            stat.Writer.Write(character.GetStatUpdate());
            packet.Sender.Send(stat);

            Log.WriteLine(string.Format("UpgradeCar OK {0} car={1} type={2} {3}->{4} cost={5} mito={6}",
                character.Name, carId, vehicle.CarType, currentGrade, nextGrade, cost, character.Mito));
        }

    }
}
