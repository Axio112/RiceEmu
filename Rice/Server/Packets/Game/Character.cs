using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Rice.Server.Core;

namespace Rice.Server.Packets.Game
{
    public static class Character
    {
        [RicePacket(123, RiceServer.ServerType.Game)]
        public static void LoadCharacter(RicePacket packet)
        {
            string characterName = packet.Reader.ReadUnicode();
            packet.Sender.Player.ActiveCharacter = null;

            foreach(var c in packet.Sender.Player.Characters)
            {
                if (c.Name == characterName)
                    packet.Sender.Player.ActiveCharacter = c;
            }

            if(packet.Sender.Player.ActiveCharacter == null)
            {
                Log.WriteLine("Rejecting {0} for invalid user-character combination.", packet.Sender.Player.User.Name);
                packet.Sender.Error("you_tried.avi");
                return;
            }

            var character = packet.Sender.Player.ActiveCharacter;
            var ack = new RicePacket(124);

            var ackStruct = new Structures.LoadCharAck
            {
                CharInfo = character.GetInfo(),
                nCarSize = (uint)character.Garage.Count,
                CarInfo = character.Garage.Select(v => v.GetInfo()).ToList()
            };

            ack.Writer.Write(ackStruct);
            packet.Sender.Send(ack);
            Log.WriteLine("Sent LoadCharAck");

            // E) Clear ephemeral in-progress missions on login so Drive Dome HUD
            // does not stick after a relog (State 0 = started / active).
            try
            {
                var active = Rice.Game.Quest.Retrieve(character.CID);
                foreach (var q in active)
                {
                    if (q.State == 0)
                    {
                        q.Fail(true); // state 4 = gave up
                        Log.WriteLine(string.Format("Cleared sticky mission QID={0} for {1} on LoadChar", q.QID, character.Name));
                    }
                }
                character.Quest = null;
            }
            catch (Exception ex)
            {
                Log.WriteError("Mission clear on LoadChar failed: {0}", ex.Message);
            }

            var stat = new RicePacket(760);
            stat.Writer.Write(character.GetStatUpdate());
            packet.Sender.Send(stat);
        }



        [RicePacket(1200, RiceServer.ServerType.Game)]
        public static void VisualItemList(RicePacket packet)
        {
            var character = packet.Sender.Player.ActiveCharacter;
            if (character == null)
            {
                var ack = new RicePacket(1201);
                ack.Writer.Write(262144);
                ack.Writer.Write(0);
                ack.Writer.Write(new byte[120]);
                packet.Sender.Send(ack);
                return;
            }
            character.SendVisualItemList(packet.Sender);
        }

        [RicePacket(400, RiceServer.ServerType.Game)]
        public static void ItemList(RicePacket packet)
        {
            byte[] testItem = { 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 0x01, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 
    0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x69, 0x91, 0x00, 0x00, 0x00, 0x00, 0x80, 0xBF, 
    0x00, 0x00, 0x00, 0x00, 0xD9, 0x04, 0x00, 0x00, 0x50, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00, 0x00};
            var ack = new RicePacket(401);
            ack.Writer.Write(262144); // ListUpdate (262144 = First packet from list queue, 262145 = sequential)
            ack.Writer.Write(1); // ItemNum
            ack.Writer.Write(testItem); // Null Item (96 bytes per XiStrMyItem)
            packet.Sender.Send(ack);
        }

        [RicePacket(801, RiceServer.ServerType.Game)]
        public static void PlayerInfoReq(RicePacket packet)
        {
            var reqCnt = packet.Reader.ReadUInt32();
            var serial = packet.Reader.ReadUInt16(); // No known scenarios where the requested info count is > 1
            // Followed by the session age of the player we are requesting info for. nplutowhy.avi
 
            foreach(var p in RiceServer.GetPlayers())
            {
                if(p.ActiveCharacter != null && p.ActiveCharacter.Serial == serial)
                {
                    var character = p.ActiveCharacter;
                    var playerInfo = new Structures.PlayerInfo()
                    {
                        Name = character.Name,
                        Serial = character.Serial,
                        Age = 0
                    };
                    var res = new RicePacket(802);
                    res.Writer.Write(1);
                    res.Writer.Write(playerInfo);
                    packet.Sender.Send(res);
                    break;
                }
            }
        }
    }
}

