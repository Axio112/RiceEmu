using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Rice.Server.Structures
{
    public struct CarInfo : ISerializable
    {
        public CarUnit CarUnit;
        public uint Color;
        public uint Color2;
        public float MitronCapacity;
        public float MitronEfficiency;
        public bool AuctionOn;
        public bool SBBOn;

        public void Serialize(PacketWriter writer)
        {
            CarUnit.Serialize(writer);
            writer.Write(Color);
            writer.Write(Color2);
            writer.Write(MitronCapacity);
            writer.Write(MitronEfficiency);
            writer.Write(AuctionOn);
            writer.Write(SBBOn);
        }
    }

    public struct CarUnit : ISerializable
    {
        public int CarID;
        public uint CarType;
        public uint BaseColor;
        public uint Grade;
        public uint SlotType;
        public uint AuctionCnt;
        public float Mitron;
        public float Kmh;

        public void Serialize(PacketWriter writer)
        {
            writer.Write(CarID);
            writer.Write(CarType);
            writer.Write(BaseColor);
            writer.Write(Grade);
            writer.Write(SlotType);
            writer.Write(AuctionCnt);
            writer.Write(Mitron);
            writer.Write(Kmh);
        }
    }

    public struct StatUpdate : ISerializable
    {
        public int BaseSpeed;
        public int BaseDurability;
        public int BaseAcceleration;
        public int BaseBoost;

        public int EquipSpeed;
        public int EquipDurability;
        public int EquipAcceleration;
        public int EquipBoost;

        public int CharSpeed;
        public int CharDurability;
        public int CharAcceleration;
        public int CharBoost;

        public int ItemUseSpeed;
        public int ItemUseDurability;
        public int ItemUseAcceleration;
        public int ItemUseBoost;

        // US client left-column performance floats (mph / accel-sec / crash min-max / boost-sec).
        // Wire order after the 20 StatInfo ints: PERF (40) then XiStrEnChantBonus.
        // Putting EnChant before Perf caused the client to read enchant ints as floats
        // (left column 0.0) and Perf mph as Assist "Maximum Speed Increase".
        public float PerfSpeed;
        public float PerfAcceleration;
        public float PerfCrashMin;
        public float PerfCrashMax;
        public float PerfBoost;

        // XiStrEnChantBonus — US GameClient.h is 4 ints + 4 floats (NO AddSpeed = 32 bytes).
        // ZoneServer.h KR has an extra AddSpeed int (36). Rice targets the US client.
        public int EnchantSpeed;
        public int EnchantCrash;
        public int EnchantAccel;
        public int EnchantBoost;
        public float Drop;
        public float Exp;
        public float MitronCapacity;
        public float MitronEfficiency;

        public void Serialize(PacketWriter writer)
        {
            // XiStrStatInfo — 20 int32s. Totals MUST stay int32 (right-hand point columns).
            writer.Write(BaseSpeed);
            writer.Write(BaseDurability);
            writer.Write(BaseAcceleration);
            writer.Write(BaseBoost);

            writer.Write(EquipSpeed);
            writer.Write(EquipDurability);
            writer.Write(EquipAcceleration);
            writer.Write(EquipBoost);

            writer.Write(CharSpeed);
            writer.Write(CharDurability);
            writer.Write(CharAcceleration);
            writer.Write(CharBoost);

            writer.Write(ItemUseSpeed);
            writer.Write(ItemUseDurability);
            writer.Write(ItemUseAcceleration);
            writer.Write(ItemUseBoost);

            int totalSpeed = BaseSpeed + EquipSpeed + CharSpeed + ItemUseSpeed;
            int totalDurability = BaseDurability + EquipDurability + CharDurability + ItemUseDurability;
            int totalAcceleration = BaseAcceleration + EquipAcceleration + CharAcceleration + ItemUseAcceleration;
            int totalBoost = BaseBoost + EquipBoost + CharBoost + ItemUseBoost;

            writer.Write(totalSpeed);
            writer.Write(totalDurability);
            writer.Write(totalAcceleration);
            writer.Write(totalBoost);

            // 40-byte US performance trailer FIRST (client left column).
            writer.Write(PerfSpeed);
            writer.Write(PerfAcceleration);
            writer.Write(PerfCrashMin);
            writer.Write(PerfCrashMax);
            writer.Write(PerfBoost);
            writer.Write(new byte[20]);

            // XiStrEnChantBonus (32 bytes US) AFTER perf.
            writer.Write(EnchantSpeed);
            writer.Write(EnchantCrash);
            writer.Write(EnchantAccel);
            writer.Write(EnchantBoost);
            writer.Write(Drop);
            writer.Write(Exp);
            writer.Write(MitronCapacity);
            writer.Write(MitronEfficiency);

            // Pad remaining 4 bytes so trailer stays 76 (matches historic Rice body size).
            writer.Write(new byte[4]);
        }
    }
}
