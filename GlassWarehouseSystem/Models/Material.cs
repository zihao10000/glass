using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("materials")]
public class Material
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("ID")]
    public int Id { get; set; }

    [StringLength(50)]
    public string GlassID { get; set; } = string.Empty;

    [StringLength(50)]
    public string? OrderID { get; set; }

    [StringLength(100)]
    public string? OrderName { get; set; }

    [StringLength(100)]
    public string? ProductName { get; set; }

    public decimal Length { get; set; }
    public decimal Width { get; set; }
    public decimal Thickness { get; set; }

    public MaterialStatus Status { get; set; }

    public bool? IsDamaged { get; set; }

    [StringLength(20)]
    public string? CurrentCage { get; set; }

    public int? CurrentLayer { get; set; }
    public int? SlotNo { get; set; }

    [StringLength(200)]
    public string? GroupID { get; set; }

    public DateTime? ImportTime { get; set; }
    public DateTime? InboundTime { get; set; }
    public DateTime? OutboundTime { get; set; }

    [StringLength(200)]
    public string? ErrorMessage { get; set; }

    public virtual Order? Order { get; set; }
}
