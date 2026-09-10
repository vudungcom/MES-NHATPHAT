using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace MES.Web.Data.Entities
{
    [Table("PartMachiningTimings")]
    public class PartMachiningTiming
    {
        [Key]
        public long TimingId { get; set; }

        public long StepId { get; set; }

        [Required]
        [MaxLength(50)]
        public string SoMay { get; set; } = "";

        public int DisplayOrder { get; set; } = 1;

        [MaxLength(500)]
        public string? FixtureType { get; set; }

        [MaxLength(500)]
        public string? ToolType { get; set; }

        public decimal? SetupTime { get; set; }
        public decimal? MachiningTime { get; set; }
        public decimal? InspectionTime { get; set; }
        public decimal? PreparationTime { get; set; }
        public decimal? TrialRunTime { get; set; }

        public bool IsActive { get; set; } = true;

        public int? CreatedBy { get; set; }
        public DateTime CreatedAt { get; set; } = DateTime.Now;
        public int? UpdatedBy { get; set; }
        public DateTime? UpdatedAt { get; set; }

        // Navigation
        [ForeignKey("StepId")]
        public virtual PartMachiningStep? Step { get; set; }
    }
}
