using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("logs")]
public class SystemLog
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int LogID { get; set; }

    [StringLength(500)]
    public string? LogContent { get; set; }

    public DateTime? RecordTime { get; set; }

    [StringLength(255)]
    public string? Type { get; set; }

    [StringLength(50)]
    public string? RelatedID { get; set; }

    [StringLength(30)]
    public string? Module { get; set; }
}
