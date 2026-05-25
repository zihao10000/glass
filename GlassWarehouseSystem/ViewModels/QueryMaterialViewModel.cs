using System;

namespace GlassWarehouseSystem.ViewModels
{
    /// <summary>
    /// 计划查询专用视图模型
    /// 严格对接数据库：Materials 表 (Status=0) 与 Orders 表 (Status=0/1)
    /// </summary>
    public class QueryMaterialViewModel
    {
        // UI 显示辅助：行号
        public int RowNo { get; set; }

        // --- 对应 Materials 表字段 ---

        /// <summary>
        /// 玻璃唯一标识 (对应数据库 GlassID)
        /// </summary>
        public string ID { get; set; }

        /// <summary>
        /// 产品名称 (对应数据库 ProductName)
        /// </summary>
        public string Name { get; set; }

        public float Length { get; set; }

        public float Width { get; set; }

        /// <summary>
        /// 玻璃厚度 (对应数据库 Thickness)
        /// </summary>
        public float Thickness { get; set; }

        /// <summary>
        /// 当前所在层 (对应数据库 CurrentLayer)
        /// </summary>
        public string ModelNo { get; set; }

        /// <summary>
        /// 入笼时间 (对应数据库 InboundTime)
        /// </summary>
        public DateTime InboundTime { get; set; }


        // --- 对应 Orders 表字段 ---
        public int   SequenceID { get; set; }
        /// <summary>
        /// 客户名称 (对应数据库 CustomerName)
        /// </summary>
        public string ClientName { get; set; }

        /// <summary>
        /// 客户原始订单编号 (对应数据库 OrderNo)
        /// </summary>
        public string OrderCard { get; set; }

        /// <summary>
        /// 流程卡号 (对应数据库 FlowCardNo)
        /// </summary>
        public string FlowCardNo { get; set; }


        // --- UI 逻辑补充字段 (数据库无对应则赋默认值) ---

        /// <summary>
        /// 产品组/分类
        /// </summary>
        public string ProductGroup { get; set; }

        /// <summary>
        /// 组名
        /// </summary>
        public string GroupName { get; set; }

        /// <summary>
        /// 数量 (默认通常为 1)
        /// </summary>
        public int Quantity { get; set; }
    }
}