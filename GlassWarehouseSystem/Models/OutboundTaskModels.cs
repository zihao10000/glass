using System;
using System.Collections.Generic;

namespace GlassWarehouseSystem.Models
{
    /// <summary>
    /// 【出库数据模型】出库任务实体（内存对象，不直接映射到数据库）。
    /// 当用户点击"出笼"按钮并输入订单号/流程卡号后，
    /// OutboundService 会在数据库中检索所有"在库"状态的物料，
    /// 构建此 OutboundTask 对象作为出库执行计划。
    /// 
    /// 一个 OutboundTask 包含多个 OutboundTaskItem，每个 Item 对应一片待出库的玻璃。
    /// Items 按照笼号→层号排序，确保出库顺序合理（从大到小或从小到大）。
    /// </summary>
    public class OutboundTask
    {
        /// <summary>任务唯一标识</summary>
        public Guid TaskID { get; set; }

        /// <summary>
        /// 触发类型：
        ///   订单驱动(0) — 通过扫描订单编号触发
        ///   计划驱动(1) — 通过流程卡号触发
        /// </summary>
        public TriggerType TriggerType { get; set; }

        /// <summary>触发关键字（订单号或流程卡号）</summary>
        public string? TriggerKey { get; set; }

        /// <summary>任务优先级（数值越高优先级越高）</summary>
        public int Priority { get; set; }

        /// <summary>任务创建时间</summary>
        public DateTime CreatedTime { get; set; } = DateTime.Now;

        /// <summary>完成进度 0.0 ~ 1.0（已出库件数 / 总件数）</summary>
        public float Progress { get; set; }

        /// <summary>任务下属的所有出库子项（每项对应一片玻璃）</summary>
        public virtual ICollection<OutboundTaskItem> Items { get; set; } = new List<OutboundTaskItem>();
    }

    /// <summary>
    /// 【出库数据模型】出库任务子项，代表一片待出库的玻璃。
    /// 记录了该物料在出库队列中的顺序以及当前执行状态。
    /// </summary>
    public class OutboundTaskItem
    {
        /// <summary>子项自增ID</summary>
        public int ItemID { get; set; }

        /// <summary>所属出库任务ID（外键）</summary>
        public Guid TaskID { get; set; }

        /// <summary>关联的物料GlassID</summary>
        public string MaterialID { get; set; }

        /// <summary>
        /// 理片顺序号（出库先后次序）。
        /// 对应需求"出笼有先后顺序，顺序可以从小到大或从大到小"。
        /// </summary>
        public int Sequence { get; set; }

        /// <summary>该子项的执行状态</summary>
        public TaskItemStatus Status { get; set; }

        /// <summary>导航属性：所属的出库任务</summary>
        public virtual OutboundTask Task { get; set; }

        /// <summary>导航属性：关联的物料实体</summary>
        public virtual Material Material { get; set; }
    }

    /// <summary>
    /// 出库任务触发方式枚举。
    /// </summary>
    public enum TriggerType
    {
        /// <summary>通过扫描订单编号触发出库</summary>
        订单驱动 = 0,
        /// <summary>通过流程卡号触发出库</summary>
        计划驱动 = 1
    }

    /// <summary>
    /// 出库子项执行状态枚举。
    /// </summary>
    public enum TaskItemStatus
    {
        /// <summary>等待出库（尚未开始）</summary>
        等待 = 0,
        /// <summary>正在执行出库动作</summary>
        执行中 = 1,
        /// <summary>出库完成</summary>
        完成 = 2,
        /// <summary>缺片（计划出库但库中无此物料）</summary>
        缺片 = 3,
        /// <summary>物料破损（出库前发现损坏）</summary>
        破损 = 4
    }

    /// <summary>
    /// 【补片数据模型】补片请求实体。
    /// 当出库检测发现：计划出库数量 > 实际库存数量，或出库时发现破损，
    /// 系统会生成补片请求，提示工厂需要重新生产或补录物料。
    /// </summary>
    public class ReplenishRequest
    {
        /// <summary>补片请求唯一标识</summary>
        public Guid RequestID { get; set; }

        /// <summary>触发补片的原始物料ID（可能已破损或缺失）</summary>
        public string? SourceMaterialID { get; set; }

        /// <summary>补片原因</summary>
        public ReplenishReason Reason { get; set; }

        /// <summary>关联的订单号</summary>
        public string? OrderNo { get; set; }

        /// <summary>需要补充的玻璃长度</summary>
        public decimal Length { get; set; }

        /// <summary>需要补充的玻璃宽度</summary>
        public decimal Width { get; set; }

        /// <summary>需要补充的玻璃厚度</summary>
        public decimal Thickness { get; set; }

        /// <summary>需要补充的产品名称</summary>
        public string? ProductName { get; set; }

        /// <summary>补片报告时间</summary>
        public DateTime ReportedTime { get; set; } = DateTime.Now;

        /// <summary>补片处理状态</summary>
        public ReplenishStatus Status { get; set; }
    }

    /// <summary>补片原因枚举</summary>
    public enum ReplenishReason
    {
        /// <summary>缺少（库存中没有对应物料）</summary>
        缺片 = 0,
        /// <summary>破损（物料已损坏不可使用）</summary>
        破损 = 1,
        /// <summary>尺寸不符（实际尺寸与订单要求不匹配）</summary>
        尺寸不符 = 2
    }

    /// <summary>补片处理状态枚举</summary>
    public enum ReplenishStatus
    {
        /// <summary>等待处理</summary>
        待处理 = 0,
        /// <summary>工厂正在生产补片</summary>
        生产中 = 1,
        /// <summary>补片已入库完成</summary>
        已入库 = 2
    }
}
