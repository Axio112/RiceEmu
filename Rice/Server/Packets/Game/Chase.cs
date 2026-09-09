using Rice.Server.Core;
using Rice.Server.Structures;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace Rice.Server.Packets.Game
{
    /*
     * Disclaimer:
     * This entire Chase implementation is unfinished, and only meant to pass the missions.
     * The chases need the Traffic Agent to control them, so all of these are at a standstill for now.
     * It needs a Chase game-object state implementation to store with players.
     * In addition to that, it's also lacking party implementation, sticking strictly to single-player.
     * Rewards also need to be generated and verified.
     */

    public static class Chase
    {
        // Deduplicate Tab-accept spam: the client resends ChaseRequest every tick until it gets a begin-confirm.
        static readonly Dictionary<ulong, DateTime> RecentChaseStarts = new Dictionary<ulong, DateTime>();

        [RicePacket(189, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseRequest(RicePacket packet)
        {
            bool now = packet.Reader.ReadByte() > 0;
            var position = Vector4.Deserialize(packet.Reader);
            Log.WriteLine($"ChaseRequest: Now - {now} | Pos {position}");

            // Tab accept with "now" means start the chase immediately at the player's position.
            // Without a ChaseBeginConfirm (181), the client never advances past the accept UI.
            if (!now)
                return;

            var character = packet.Sender.Player.ActiveCharacter;
            if (character == null)
                return;

            DateTime last;
            if (RecentChaseStarts.TryGetValue(character.CID, out last)
                && (DateTime.UtcNow - last).TotalSeconds < 3)
            {
                return;
            }
            RecentChaseStarts[character.CID] = DateTime.UtcNow;

            int huvLevel = 1;
            int huvId = 1;
            int huvNum = 1;
            var activeQuest = character.Quest;
            if (activeQuest?.QuestInfo != null && activeQuest.QuestInfo.HuvLevel > 0)
            {
                huvLevel = activeQuest.QuestInfo.HuvLevel;
                huvId = activeQuest.QuestInfo.HuvID;
                Log.WriteLine("ChaseRequest starting quest chase {0} (HUV {1}/{2})",
                    activeQuest.QuestInfo.Title, huvLevel, huvId);
            }
            else
            {
                // Free call-mission: scale a single HUV roughly to player level.
                huvLevel = Math.Max(1, character.Level);
                huvId = 1;
            }

            SendChaseBeginConfirm(packet.Sender, character, position, huvLevel, huvId, huvNum);
        }

        [RicePacket(180, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseBegin(RicePacket packet)
        {
            int chaseType = packet.Reader.ReadInt32();
            var startPos = Vector4.Deserialize(packet.Reader);
            int firstHuvLevel = packet.Reader.ReadInt32();
            int firstHuvId = packet.Reader.ReadInt32();
            int huvNum = packet.Reader.ReadInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            var activeQuest = character.Quest;

            int huvLevel = firstHuvLevel;
            int huvId = firstHuvId;
            if ((activeQuest?.QuestInfo?.HuvLevel ?? 0) > 0)
            {
                var info = activeQuest.QuestInfo;
                Log.WriteLine("Player has active quest {0}, using huv info ({1}, {2})",
                    info.Title, info.HuvLevel, info.HuvID);
                huvLevel = info.HuvLevel;
                huvId = info.HuvID;
            }

            if (huvNum < 1)
                huvNum = 1;

            SendChaseBeginConfirm(packet.Sender, character, startPos, huvLevel, huvId, huvNum);
        }

        static void SendChaseBeginConfirm(RiceClient client, Rice.Game.Character character,
            Vector4 startPos, int huvLevel, int huvId, int huvNum)
        {
            var ack = new RicePacket(181); // Cmd_ChaseBeginConfirm / BS_PktChaseBeginConfirm
            ack.Writer.Write(startPos); // struct XiVec4 m_StartPos
            ack.Writer.Write((byte)1); // char m_Accepted
            ack.Writer.Write(180); // long m_FindTimeout
            ack.Writer.Write(180); // long m_ArrestTimeout
            ack.Writer.Write((ushort)huvNum); // unsigned short m_huvSize
            for (int i = 0; i < huvNum; i++)
            {
                // HUV unit serials must be distinct from the player's car serial.
                ushort huvSerial = (ushort)(character.Serial + 1000 + i);
                ack.Writer.Write(huvSerial); // unsigned short m_Serial
                ack.Writer.Write((ushort)(20397 + i)); // unsigned short m_CarSort
                ack.Writer.Write(huvLevel); // int m_huvLevel
                ack.Writer.Write(huvId); // int m_huvId
                ack.Writer.Write(140f); // float m_Speed
                ack.Writer.Write(300f); // float m_MaxSpeed
                ack.Writer.Write(240f); // float m_NosAccel
                ack.Writer.Write(3f); // float m_NosTime
                ack.Writer.Write(3f); // float m_NosRefreshRate
                ack.Writer.Write(43f); // float m_Durability
                ack.Writer.Write(2f); // float m_FrontPlayerAvoidanceRate
                ack.Writer.Write(2f); // float m_FrontTrafficAvoidanceRate
                ack.Writer.Write(1f); // float m_RearPlayerAvoidanceRate
            }
            client.Send(ack);
            Log.WriteLine("ChaseBeginConfirm sent for {0}: {1} HUV(s) lv{2} id{3} at {4}",
                character.Name, huvNum, huvLevel, huvId, startPos);
        }

        [RicePacket(185, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseProgress(RicePacket packet)
        {
            ushort serial = packet.Reader.ReadUInt16();
            ushort targetSerial = packet.Reader.ReadUInt16();
            ushort targetCarSort = packet.Reader.ReadUInt16();
            int time = packet.Reader.ReadInt32();
            int state = packet.Reader.ReadInt32();

            var ack = new RicePacket(185);
            ack.Writer.Write(serial);
            ack.Writer.Write(targetSerial);
            ack.Writer.Write(targetCarSort);
            ack.Writer.Write(time);
            ack.Writer.Write(state);
            packet.Sender.Send(ack);
        }

        [RicePacket(187, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseHit(RicePacket packet)
        {
            ushort serial = packet.Reader.ReadUInt16();
            ushort targetSerial = packet.Reader.ReadUInt16();
            ushort targetCarSort = packet.Reader.ReadUInt16();
            float damage = packet.Reader.ReadSingle();
            int time = packet.Reader.ReadInt32();
            uint flags = packet.Reader.ReadUInt32();
            float resultLife = packet.Reader.ReadSingle();
            string name = packet.Reader.ReadUnicodeStatic(10);
            ushort what = packet.Reader.ReadUInt16();
            ushort what2 = packet.Reader.ReadUInt16();
        }

        [RicePacket(183, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void ChaseEnd(RicePacket packet)
        {
            ushort serial = packet.Reader.ReadUInt16();
            ushort carSort = packet.Reader.ReadUInt16();
            uint type = packet.Reader.ReadUInt32();
            float life = packet.Reader.ReadSingle();
            uint result = packet.Reader.ReadUInt32();
            var pos = Vector4.Deserialize(packet.Reader);
            var vel = Vector4.Deserialize(packet.Reader);
            int time = packet.Reader.ReadInt32();

            // Layout above is imperfect but aligned enough for the result window.
            var character = packet.Sender.Player.ActiveCharacter;

            // Grant a small payout on success-looking results so the result UI has numbers.
            // result semantics are not fully documented; non-zero is treated as a finished chase.
            int deltaMoney = 0;
            int deltaExp = 0;
            if (result != 0 || life <= 0f)
            {
                deltaMoney = 250;
                deltaExp = 50;
                character.GrantExperience(deltaExp);
                character.GrantMito((ulong)deltaMoney);
            }

            var ack = new RicePacket(184); // ChaseResult — client shows post-chase UI / return-to-station from this
            ack.Writer.Write(result);
            ack.Writer.Write(time);
            ack.Writer.Write((ushort)1); // unitSize - player count
            ack.Writer.Write(new byte[4 * 4 + 4 * 4]); // int selfdaily[4]; int teamdaily[4];

            ack.Writer.WriteUnicodeStatic(character.Name, 0x20);
            ack.Writer.Write(serial);
            ack.Writer.Write(carSort);
            ack.Writer.Write(deltaMoney); // deltaHuvMoney
            ack.Writer.Write(0); // deltaBonusMoney
            ack.Writer.Write((long)character.Mito); // money
            ack.Writer.Write(deltaExp); // deltaHuvExp
            ack.Writer.Write(0); // deltaBonusExp
            ack.Writer.Write(character.GetExpInfo());
            ack.Writer.Write(character.Level);
            ack.Writer.Write(1f); // point?

            ack.Writer.Write(0); // rewardItemCount
            ack.Writer.Write(0); // reward 1
            ack.Writer.Write(0); // reward 2
            packet.Sender.Send(ack);

            RecentChaseStarts.Remove(character.CID);
            Log.WriteLine("ChaseEnd/Result for {0}: result={1} life={2} time={3} +{4} mito +{5} exp",
                character.Name, result, life, time, deltaMoney, deltaExp);
        }
    }
}
