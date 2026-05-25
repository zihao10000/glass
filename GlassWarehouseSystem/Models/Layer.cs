using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("layers")]
public class Layer
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("ID")]
    public int Id { get; set; }

    [StringLength(50)]
    public string LayerID { get; set; } = string.Empty;

    [StringLength(50)]
    public string? CageID { get; set; }

    public int? LayerNo { get; set; }
    public bool? IsOccupied { get; set; }

    [StringLength(50)]
    public string? GlassID { get; set; }

    [StringLength(50)]
    public string? PLCAddress { get; set; }

    public double? Length { get; set; }
    public double? RemainingLength { get; set; }

    [StringLength(100)]
    public string? GlassSpec { get; set; }

    public double? Width { get; set; }
    public decimal? Coordinate { get; set; }
    public int? StartNo { get; set; }
    public double? Space { get; set; }
    public bool? IsDamaged { get; set; }

    [NotMapped]
    public decimal? LayerSpacing { get; set; }

    public virtual Cage? Cage { get; set; }

    public virtual Material? Material { get; set; }
}
