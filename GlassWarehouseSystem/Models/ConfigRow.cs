using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("config")]
public class ConfigRow
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    public int ConfigID { get; set; }

    [Column("Type")]
    public string? Type { get; set; }

    [Column("Type2")]
    public string? Type2 { get; set; }

    [Column("KeyName")]
    public string? KeyName { get; set; }

    [Column("Value")]
    public string? Value { get; set; }

    [Column("Address")]
    public int? Address { get; set; }

    [Column("Describe")]
    public string? Describe { get; set; }

    [Column("UpdateTime")]
    public DateTime? UpdateTime { get; set; }

    [NotMapped]
    public string DisplayID => ConfigID == 0 ? "（新）" : ConfigID.ToString();
}
