// Quick query to check config table Addr_* entries
using MySqlConnector;

var connStr = "server=127.0.0.1;port=3306;database=GlassWarehouseDB;user=root;password=123456;";
using var conn = new MySqlConnection(connStr);
conn.Open();

using var cmd = conn.CreateCommand();
cmd.CommandText = "SELECT `Describe`, `Value`, `Address` FROM config WHERE `Describe` LIKE 'Addr_%' ORDER BY `Address`";
using var reader = cmd.ExecuteReader();

Console.WriteLine($"{"Describe",-30} {"Value",-10} {"Address",-10}");
Console.WriteLine(new string('-', 55));
while (reader.Read())
{
    var desc = reader["Describe"]?.ToString() ?? "(null)";
    var val = reader["Value"]?.ToString() ?? "(null)";
    var addr = reader["Address"] == DBNull.Value ? "(null)" : reader["Address"]?.ToString();
    Console.WriteLine($"{desc,-30} {val,-10} {addr,-10}");
}
