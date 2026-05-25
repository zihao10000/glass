using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace GlassWarehouseSystem.Models
{
    /// <summary>
    /// 对应 MySQL 数据库中的 Config 表，每一行就是一整套系统参数。
    /// 
    ///  ConfigRow（键值对行结构），AppConfig.Initialize() 
    /// 会优先尝试新版格式，失败时回退到这个旧版宽表格式读取参数。
    /// 这些参数对应需求文档"全局参数"中提到的 5 个上位机参数。
    /// </summary>
    [Table("Config")]
    public class Config
    {
        /// <summary>
        /// 配置表主键ID
        /// </summary>
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.None)]
        public int ConfigID { get; set; }

        /// <summary>
        /// 测量长宽时的允许误差范围（单位：mm）。
        /// 对应需求文档中的 Config_MeasurementError。
        /// CageFinder 在匹配笼子尺寸时使用此容差值。
        /// </summary>
        public decimal? MeasureErrorAllowance { get; set; }
        
        /// <summary>
        /// 数据获取方式（枚举值）。
        ///   0 = 扫码获取（Scan）：通过扫码枪读取条码后查询数据库获得玻璃信息。
        ///   1 = 测量获取（Measure）：通过 PLC 传感器直接读取长度（C地址）和宽度（D地址）。
        /// 对应需求文档中的 Config_DataSource。
        /// </summary>
        public int? DataSourceType { get; set; }
        
        /// <summary>
        /// 扫码/获取数据失败时的最大重试次数。
        /// 超过此次数后放弃获取，记录错误日志并等待人工处理。
        /// 对应需求文档中的 Config_ScanRetryLimit（"n次扫描不成的处理情况，n是可变的"）。
        /// </summary>
        public int? MaxScanRetryTimes { get; set; }
        
        /// <summary>
        /// 笼内物理计算距离参数 K（单位：mm）。
        /// 对应需求文档中的 Config_CageInnerDist（"笼内距离为k"）。
        /// </summary>
        public decimal? CageInnerDistanceK { get; set; }
        
        /// <summary>
        /// 相邻玻璃存放时的安全间距（单位：mm）。
        /// 入库时每放入一片玻璃，该层的 RemainingLength 扣减 = Length + GlassSpacing。
        /// 对应需求文档中的 Config_GlassSpacing（"玻璃间距"）。
        /// </summary>
        public decimal? GlassSpacing { get; set; }
        
        /// <summary>
        /// 最后一次通过配置界面修改参数的时间戳。
        /// </summary>
        public DateTime? UpdateTime { get; set; }
    }
}
