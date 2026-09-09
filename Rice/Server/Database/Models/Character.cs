using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using System.Text;

namespace Rice.Server.Database.Models
{
    public class Character
    {
        [Key]
        public long ID { get; set; }

        [StringLength(32)]
        public string Name { get; set; }

        public long Mito { get; set; }
        public long Experience { get; set; }

        public int Avatar { get; set; }
        public int Level { get; set; }

        public int City { get; set; }

        public float PosX { get; set; }
        public float PosY { get; set; }
        public float PosZ { get; set; }

        public int PosState { get; set; }

        public int CurrentCarID { get; set; }
        public int GarageLevel { get; set; }

        // The client packs its quick slot assignments into two words. They were
        // always sent as zero because there was nowhere to keep them.
        public long QuickSlot1 { get; set; }
        public long QuickSlot2 { get; set; }

        public long UID { get; set; }

        public long TID { get; set; }
        // TODO: Crew relation
    }
}
