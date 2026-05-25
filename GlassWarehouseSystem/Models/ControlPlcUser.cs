using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models
{
    [Table("controlplcuser")] // 确保与 SQL 表名一致
    public class ControlPlcUser
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        [Column("id")] // 必须映射到 SQL 中的小写 id
        public int Id { get; set; }

        [Required]
        [MaxLength(50)]
        [Column("username")] // 必须映射到 SQL 中的小写 username
        public string Username { get; set; }

        [Required]
        [MaxLength(255)]
        [Column("password")] // 必须映射到 SQL 中的小写 password
        public string Password { get; set; }
    }
}