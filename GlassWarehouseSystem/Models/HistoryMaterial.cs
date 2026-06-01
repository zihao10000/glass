using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("materialshistory")]
public class HistoryMaterial
{
    [Key]
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("HistoryID")]
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
    public int? Status { get; set; }
    public bool? IsDamaged { get; set; }
    public MaterialStatus OriginalStatus { get; set; }


    [StringLength(20)]
    [Column("CurrentCage")]
    public string? CageCode { get; set; }

    [Column("CurrentLayer")]
    public int? LayerNo { get; set; }

    [NotMapped]
    public int? SlotNo { get; set; }

    public DateTime? InboundTime { get; set; }
    public DateTime? OutboundTime { get; set; }
    public DateTime? ArchiveTime { get; set; }

    [StringLength(50)]
    public string? OutboundBatchNo { get; set; }

    [StringLength(200)]
    public string? GroupID { get; set; }
    /// <summary>订单号（冗余快照，DB暂无此列）</summary>
    [NotMapped]
    public string? OrderNo { get; set; }

    /// <summary>流程卡号（冗余快照，DB暂无此列）</summary>
    [NotMapped]
    public string? FlowCardNo { get; set; }
    [StringLength(200)]
    public string? ErrorMessage { get; set; }
    /// <summary>客户名称（冗余快照，DB暂无此列）</summary>
    [NotMapped]
    public string? CustomerName { get; set; }

}
