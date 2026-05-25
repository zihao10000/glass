using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

[Table("plcaddressmapping")]
public class PLCAddressMapping
{
    [Key]
    [StringLength(100)]
    public string KeyName { get; set; }

    [StringLength(100)]
    public string RealAddress { get; set; }

    [StringLength(50)]
    public string DataType { get; set; }

    [StringLength(200)]
    public string Description { get; set; }
}