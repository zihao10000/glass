using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;

namespace GlassWarehouseSystem.Models
{
    /// <summary>
    /// 入库队列
    /// </summary>
    public class InboundQueue
    {
        [Key]
        public int QueueID { get; set; }
        public int SequenceNo { get; set; }  // 排序序号
        public Guid MaterialID { get; set; }
        public int PriorityScore { get; set; }  // 优先级分数

        [Required]
        [StringLength(50)]
        public string TargetCageID { get; set; }  // 目标笼号

        public QueueStatus Status { get; set; }
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        public virtual Material Material { get; set; }
    }

    public enum QueueStatus
    {
        等待中 = 0,
        扫描中 = 1,
        动作中 = 2,
        完成 = 3
    }

    /// <summary>
    /// 出库任务
    /// </summary>
    
    /// <summary>
    /// 优先级配置
    /// </summary>
    public class PriorityConfig
    {
        [Key]
        public int ConfigID { get; set; }
        public PriorityCategory Category { get; set; }

        [Required]
        [StringLength(100)]
        public string Value { get; set; }  // 客户名称或流程卡号

        public int PriorityLevel { get; set; }  // 1为最高
        public PriorityStrategy Strategy { get; set; }
    }

    public enum PriorityCategory
    {
        客户 = 0,
        流程卡号 = 1,
        品牌 = 2
    }

    public enum PriorityStrategy
    {
        优先出笼 = 0,
        序号完成 = 1
    }

    /// <summary>
    /// 操作日志
    /// </summary>
    public class OperationLog
    {
        [Key]
        public long LogID { get; set; }
        public DateTime Timestamp { get; set; } = DateTime.Now;
        public LogLevel LogLevel { get; set; }
        public LogModule Module { get; set; }

        [Required]
        [StringLength(500)]
        public string Message { get; set; }

        [Required]
        [StringLength(100)]
        public string RelatedID { get; set; }

        [Required]
        [StringLength(100)]
        public string Operator { get; set; }
    }

    public enum LogLevel
    {
        INFO = 0,
        WARNING = 1,
        ERROR = 2,
        SUCCESS = 3
    }

    public enum LogModule
    {
        入笼 = 0,
        出笼 = 1,
        PLC = 2,
        扫描 = 3,
        系统 = 4
    }
}