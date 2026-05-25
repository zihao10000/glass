using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("Cagesbeixuan")]   // 对应数据库中的表名
public class Cagesbeixuan
{
    [Key]
    [StringLength(20)]
    public string CageID { get; set; }                // 主键，长度 20

    public int TotalLayers { get; set; }               // 总层数，默认值 120（可在构造函数或配置中处理）

    public decimal LayerSpacing { get; set; }          // 层间距，精度 (10,2)

    [StringLength(50)]
    public string? PLCStartAddr { get; set; }          // 可为空的 PLC 起始地址

    public int Status { get; set; }                     // 状态，默认值 1（正常工作）
}