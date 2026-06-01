using MySqlConnector;

var connectionString = "server=127.0.0.1;port=3306;database=glasswarehousedb;user=root;password=282814;";

Console.WriteLine("尝试连接数据库...");
Console.WriteLine($"连接字符串：{connectionString}");

try
{
    using var conn = new MySqlConnection(connectionString);
    conn.Open();
    Console.WriteLine("✓ 数据库连接成功！");
    
    using var cmd = new MySqlCommand("SELECT COUNT(*) FROM materials", conn);
    var count = cmd.ExecuteScalar();
    Console.WriteLine($"✓ materials 表中有 {count} 条记录");
}
catch (Exception ex)
{
    Console.WriteLine($"✗ 连接失败：{ex.Message}");
    Console.WriteLine($"详细错误：{ex}");
}

Console.WriteLine("\n按任意键退出...");
Console.ReadKey();
