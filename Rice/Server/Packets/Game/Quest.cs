using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Rice.Server.Core;
using Rice.Server.Structures;

namespace Rice.Server.Packets.Game
{
    public static class Quest
    {
        [RicePacket(272, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void MyQuestList(RicePacket packet)
        {
            var quests = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID);

            var ack = new RicePacket(273);
            ack.Writer.Write(quests.Count);
            foreach (var quest in quests)
            {
                ack.Writer.Write(quest.QID);
                ack.Writer.Write(quest.State);
                ack.Writer.Write(quest.Progress);
                ack.Writer.Write(quest.FailCount);
            }
            packet.Sender.Send(ack);
        }

        [RicePacket(262, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestStart(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var quest = Rice.Game.Quest.Retrieve(packet.Sender.Player.ActiveCharacter.CID, questId);
            quest = quest ?? Rice.Game.Quest.CreateEntry(packet.Sender.Player.ActiveCharacter.CID, questId);

            packet.Sender.Player.ActiveCharacter.Quest = quest;

            var ack = new RicePacket(263);
            ack.Writer.Write(quest.QID);
            ack.Writer.Write((uint) quest.FailCount);
            packet.Sender.Send(ack);
            // TODO: verify if player is eligible
        }

        [RicePacket(264, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestComplete(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();
            var totalTime = packet.Reader.ReadSingle();

            var character = packet.Sender.Player.ActiveCharacter;
            var quest = character.Quest;

            // The active quest is only held in memory, so a reconnect or a server
            // restart loses it. Recover the quest from storage instead of dropping
            // the player back to character selection.
            if (quest == null || quest.QID != questId)
                quest = Rice.Game.Quest.Retrieve(character.CID, questId);

            if (quest == null)
            {
                Log.WriteError("Quest {0} is not started for character {1}.", questId, character.CID);
                return;
            }

            quest.SetState(1);

            var ack = new RicePacket(265);
            ack.Writer.Write(questId);
            packet.Sender.Send(ack);
            // TODO: verify quest completion
        }

        [RicePacket(266, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestReward(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();
            var character = packet.Sender.Player.ActiveCharacter;

            var quest = character.Quest;

            // The active quest is only held in memory, so it is lost across a
            // reconnect or a server restart. Recover it from storage instead of
            // dropping the player back to character selection.
            if (quest == null || quest.QID != questId)
                quest = Rice.Game.Quest.Retrieve(character.CID, questId);

            if (quest == null || quest.QuestInfo == null)
            {
                Log.WriteError("QuestReward for quest {0}, which character {1} has not started.",
                    questId, character.CID);
                packet.Sender.Error("That quest could not be found.");
                return;
            }

            quest.SetState(2);
            var rewards = quest.QuestInfo.GetRewards();
            character.Quest = null;

            int expGotten = quest.QuestInfo.Experience;
            int moneyGotten = quest.QuestInfo.Mito;

            character.GrantExperience(expGotten);
            character.GrantMito((ulong) moneyGotten);

            var ack = new RicePacket(267);
            ack.Writer.Write(questId);
            ack.Writer.Write(expGotten);
            ack.Writer.Write(moneyGotten);
            ack.Writer.Write(character.GetExpInfo());
            ack.Writer.Write(character.Level);
            ack.Writer.Write((ushort) rewards.Length); // reward item num

            // The client reads a fixed three reward slots. Write a table index where
            // a reward exists and zero everywhere else; the previous nested loop
            // could emit the wrong number of slots and desync the packet.
            for (int i = 0; i < 3; i++)
            {
                if (i >= rewards.Length)
                {
                    ack.Writer.Write(0);
                    continue;
                }

                var itemID = rewards[i];
                int tblIdx = Structures.Resources.ItemTable.Items.FindIndex(itm => itm.ID == itemID);

                if (tblIdx == -1)
                {
                    Log.WriteError("Quest {0} rewards unknown item {1}.", questId, itemID);
                    ack.Writer.Write(0);
                    continue;
                }

                Rice.Game.Item resultItem;
                character.GrantItem(itemID, 1, out resultItem);
                ack.Writer.Write((uint) tblIdx);
            }

                        Log.WriteLine("Quest {0} rewarded: {1} exp, {2} mito, {3} item(s).",
                questId, expGotten, moneyGotten, rewards.Length);
            packet.Sender.Send(ack);

            // F) Auto-return to station after quest complete (custom; retail uses a yes/no popup).
            try
            {
                const float stationX = -3372.566f;
                const float stationY = 1200.367f;
                const float stationZ = 85.519f;
                int stationCity = 1;
                int stationPosState = 2;

                character.Position = new Vector4(stationX, stationY, stationZ);
                character.City = stationCity;
                character.PosState = stationPosState;
                character.SaveCarPos(0, character.Position, stationCity, stationPosState);

                var posAck = new RicePacket(783);
                posAck.Writer.Write(stationCity);
                posAck.Writer.Write(0);
                posAck.Writer.Write(character.Position);
                posAck.Writer.Write(stationPosState);
                posAck.Writer.Write((byte)0);
                packet.Sender.Send(posAck);

                Log.WriteLine(string.Format("QuestReward auto-teleport {0} -> station ({1},{2},{3}) city={4} posState={5}",
                    character.Name, stationX, stationY, stationZ, stationCity, stationPosState));
            }
            catch (Exception ex)
            {
                Log.WriteError("QuestReward teleport failed: {0}", ex.Message);
            }
        }

        [RicePacket(268, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestFail(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            var quest = character.Quest;

            // The active quest is only held in memory, so a reconnect or a server
            // restart loses it. Recover the quest from storage instead of dropping
            // the player back to character selection.
            if (quest == null || quest.QID != questId)
                quest = Rice.Game.Quest.Retrieve(character.CID, questId);

            if (quest == null)
            {
                Log.WriteError("Quest {0} is not started for character {1}.", questId, character.CID);
                return;
            }

            quest.Fail();
            packet.Sender.Player.ActiveCharacter.Quest = null;

            var ack = new RicePacket(269);
            ack.Writer.Write(questId);
            packet.Sender.Send(ack);
        }

        [RicePacket(270, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestGiveUp(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            var quest = character.Quest;

            // The active quest is only held in memory, so a reconnect or a server
            // restart loses it. Recover the quest from storage instead of dropping
            // the player back to character selection.
            if (quest == null || quest.QID != questId)
                quest = Rice.Game.Quest.Retrieve(character.CID, questId);

            if (quest == null)
            {
                Log.WriteError("Quest {0} is not started for character {1}.", questId, character.CID);
                return;
            }

            quest.Fail(true);
            packet.Sender.Player.ActiveCharacter.Quest = null;

            var ack = new RicePacket(271);
            ack.Writer.Write(questId);
            packet.Sender.Send(ack);
        }

        [RicePacket(274, RiceServer.ServerType.Game, CheckedIn = true)]
        public static void QuestGoalPlace(RicePacket packet)
        {
            var questId = packet.Reader.ReadInt32();

            var character = packet.Sender.Player.ActiveCharacter;
            var quest = character.Quest;

            // The active quest is only held in memory, so a reconnect or a server
            // restart loses it. Recover the quest from storage instead of dropping
            // the player back to character selection.
            if (quest == null || quest.QID != questId)
                quest = Rice.Game.Quest.Retrieve(character.CID, questId);

            if (quest == null)
            {
                Log.WriteError("Quest {0} is not started for character {1}.", questId, character.CID);
                return;
            }

            quest.IncrementProgress();
        }
    }
}
