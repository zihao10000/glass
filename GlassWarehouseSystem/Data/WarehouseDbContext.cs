using GlassWarehouseSystem.Config;
using GlassWarehouseSystem.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace GlassWarehouseSystem.Data;

/// <summary>
/// 【数据访问层核心】EF Core 数据库上下文类。
/// 负责管理与 MySQL 数据库的所有交互，包括连接管理、表映射、关系配置。
/// 
/// 数据库连接：
///   连接串硬编码在 ConnectionString 常量中。
/// 
/// 包含的表（DbSet）：
///   Orders           — 订单表
///   Materials        — 物料/玻璃表（核心业务表）
///   Cages            — 笼子设备表
///   Layers           — 笼内层结构表
///   Logs             — 系统日志表
///   Configs          — 全局配置表（旧版宽表格式）
///   ConfigRows       — 全局配置表（新版键值对行格式）
///   PLC 地址映射统一由 config 表中的 Addr_* 键值对提供
/// </summary> 
public class WarehouseDbContext : DbContext
{
    /// <summary>
    /// MySQL 连接字符串。
    /// 指向本机 3306 端口的 GlassWarehouseDB 数据库。
    /// 注意：生产部署时应将此值移至 appsettings.json 或环境变量中。glasswarehousedb
    /// </summary>
    public const string ConnectionString = "server=127.0.0.1;port=3306;database=glasswarehousedb;user=root;password=123456;";


    // ================== DbSet 属性  每个对应一张 MySQL 表 ==================

    /// <summary>订单表</summary>
    public DbSet<Order> Orders => Set<Order>();

    /// <summary>物料/玻璃表（核心）</summary>
    public DbSet<Material> Materials => Set<Material>();

    /// <summary>笼子设备表</summary>
    public DbSet<Cage> Cages => Set<Cage>();

    /// <summary>笼内层结构表</summary>
    public DbSet<Layer> Layers => Set<Layer>();

    /// <summary>系统日志表</summary>
    public DbSet<SystemLog> Logs => Set<SystemLog>();

    /// <summary>历史物料表（出库归档）</summary>
    public DbSet<HistoryMaterial> HistoryMaterials => Set<HistoryMaterial>();

    /// <summary>全局配置表（旧版宽表格式）</summary>
    public DbSet<Models.Config> Configs => Set<Models.Config>();

    /// <summary>全局配置表（含PLC映射）</summary>
    public DbSet<ConfigRow> ConfigRows => Set<ConfigRow>();

    public DbSet<ControlPlcUser> ControlPlcUser { get; set; }

    /// <summary>
    /// 数据库连接配置。
    /// 使用 Pomelo.EntityFrameworkCore.MySql 驱动连接 MySQL 8.0。
    /// </summary>
    protected override void OnConfiguring(DbContextOptionsBuilder optionsBuilder)
    {
        if (!optionsBuilder.IsConfigured)
        {
            var connStr = AppConfig.GetDatabaseConnectionString();
            var serverVersion = new MySqlServerVersion(new Version(8, 0, 45));
            optionsBuilder.UseMySql(connStr, serverVersion);
        }
    }

    /// <summary>
    /// 数据库模型配置（Fluent API）。
    /// 定义表名映射、主键约定以及实体间的关联关系。
    /// </summary>
    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        // ================== 表名映射 ==================
        modelBuilder.Entity<Order>().ToTable("Orders");
        modelBuilder.Entity<Material>().ToTable("Materials");
        modelBuilder.Entity<Cage>().ToTable("Cages").HasKey(x => x.CageCode); // Cage 使用 CageCode 字符串作为主键
        modelBuilder.Entity<Layer>().ToTable("Layers");
        modelBuilder.Entity<SystemLog>().ToTable("Logs");
        modelBuilder.Entity<HistoryMaterial>().ToTable("materialshistory");
        modelBuilder.Entity<HistoryMaterial>().HasIndex(h => h.GlassID);
        modelBuilder.Entity<Models.Config>().ToTable("Config");
        modelBuilder.Entity<ControlPlcUser>().ToTable("controlplcuser");

        // ================== 关联关系配置 ==================

        // Order 1 : N Material（一个订单包含多片玻璃）
        // 删除订单时不级联删除物料，而是将 Material.OrderID 置为 null
        modelBuilder.Entity<Order>()
            .HasMany(o => o.Materials)
            .WithOne(m => m.Order)
            .HasForeignKey(m => m.OrderID)
            .OnDelete(DeleteBehavior.SetNull);

        // Cage 1 : N Layer（一个笼包含多个层）
        // 删除笼时级联删除所有层记录
        modelBuilder.Entity<Cage>()
            .HasMany(c => c.Layers)
            .WithOne(l => l.Cage)
            .HasForeignKey(l => l.CageID)
            .OnDelete(DeleteBehavior.Cascade);

        // Layer 1 : 0..1 Material（一个层可选地关联一片玻璃作为快照引用）
        // 删除物料时将 Layer.GlassID 置为 null
        // 使用 Material.GlassID 作为主键（而非 Material.Id）
        modelBuilder.Entity<Layer>()
            .HasOne(l => l.Material)
            .WithMany()
            .HasForeignKey(l => l.GlassID)
            .HasPrincipalKey(m => m.GlassID)
            .OnDelete(DeleteBehavior.SetNull);

        // ConfigRow 使用无键实体映射到 config 表（EF Core 5+ 的 HasNoKey 模式）
        modelBuilder.Entity<ConfigRow>().HasNoKey().ToTable("config");
    }
}
