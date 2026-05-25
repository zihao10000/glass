using System.Collections.Generic;
using System.Globalization;
using Newtonsoft.Json;

namespace GlassWarehouseSystem.LocalConfig;

public class LocalSystemSettings
{
    public DatabaseConfig Database { get; set; } = new();
    public List<PlcConnectionConfig> PlcConnections { get; set; } = [];
    public LabelPrinterConfig LabelPrinter { get; set; } = new();
}

public class DatabaseConfig
{
    public string ServerAddress { get; set; } = "127.0.0.1";
    public string DatabaseName { get; set; } = "glasswarehousedb";
    public int ServerPort { get; set; } = 3306;
    public int RedisDbIndex { get; set; } = 0;
    public int RedisPort { get; set; } = 6379;
    public string DbUser { get; set; } = "root";
    public string DbPassword { get; set; } = "123456";

    public string BuildConnectionString()
        => string.Format(CultureInfo.InvariantCulture,
            "Server={0};Port={1};Database={2};Uid={3};Pwd={4};",
            ServerAddress,
            ServerPort,
            DatabaseName,
            DbUser,
            DbPassword);

    public string BuildRedisConnectionString()
        => string.Format(CultureInfo.InvariantCulture, "{0}:{1},defaultDatabase={2}",
            ServerAddress,
            RedisPort,
            RedisDbIndex);
}

public class PlcConnectionConfig
{
    public int Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string PlcIp { get; set; } = string.Empty;
    public string Type { get; set; } = "S71200";
    public int Rack { get; set; }
    public int Slot { get; set; } = 1;

    [JsonIgnore]
    public string DisplayText => $"{Id}.{Name}:{PlcIp}";
}

public class LabelPrinterConfig
{
    public string PrinterName { get; set; } = string.Empty;
    public bool BatchPrint { get; set; } = true;
    public string PrintMode { get; set; } = "Network";
    public int ServicePort { get; set; } = 8100;
    public string ServiceIp { get; set; } = "192.168.88.2";
    public decimal PageWidth { get; set; } = 50m;
    public decimal PageHeight { get; set; } = 30m;
}
