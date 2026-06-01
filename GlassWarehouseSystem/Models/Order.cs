using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models;

[Table("orders")]
public class Order
{
    [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
    [Column("ID")]
    public int Id { get; set; }

    [Key]
    [StringLength(50)]
    public string OrderID { get; set; } = string.Empty;

    [StringLength(50)]
    public string? OrderNo { get; set; }

    [StringLength(50)]
    public string? FlowCardNo { get; set; }

    [StringLength(100)]
    public string? CustomerName { get; set; }

    public int? Status { get; set; }
    public DateTime? CreateTime { get; set; }
    
    public DateTime? DeliveryDate { get; set; }
    
    public int? TotalCount { get; set; }

    public virtual ICollection<Material> Materials { get; set; } = new List<Material>();
}
