using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("cages")]
public class Cage
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("ID")]
    public int Id { get; set; }

    [StringLength(50)]
    public string CageCode { get; set; } = string.Empty;

    [StringLength(100)]
    public string? EquipmentName { get; set; }

    public int? CageType { get; set; }
    public int? LayerCount { get; set; }

    public decimal? Length { get; set; }
    public decimal? Width { get; set; }
    public decimal? Height { get; set; }

    public decimal? ZeroCoordinate { get; set; }
    public decimal? NegativeLimit { get; set; }
    public decimal? PositiveLimit { get; set; }

    public decimal? InboundGap { get; set; }
    public decimal? OutboundGap { get; set; }

    public int? CageSequenceNo { get; set; }

    public decimal? XCoordinate { get; set; }
    public decimal? YCoordinate { get; set; }

    public int? TransitionLayerNo { get; set; }
    public int? GridInitSeqNo { get; set; }
    public decimal? GridStartCoord { get; set; }
    public decimal? Space { get; set; }

    public bool? IsOnline { get; set; }
    public bool? IsOneWay { get; set; }

    [StringLength(10)]
    public string? LocationType { get; set; }

    public DateTime? UpdateTime { get; set; }

    public virtual ICollection<Layer> Layers { get; set; } = new List<Layer>();
}
