namespace GlassWarehouseSystem.Models;

/// <summary>
/// CageFinder 笼位分配算法的返回结果。
/// 当 CageFinder.FindCage() 成功找到一个合适的笼子和层时，
/// 将目标信息封装在此对象中返回给 InboundService。
/// 
/// InboundService 据此：
///   1. 向 PLC E 地址写入 TargetPosValue（目标位置数值）
///   2. 在数据库落库时使用 CageCode / LayerNo / LayerID 记录物料坐标
/// </summary>
public class FindCageResult
{
    /// <summary>
    /// 目标笼子编码（如 "A01"），用于数据库记录 Material.CurrentCage。
    /// </summary>
    public string CageCode { get; set; } = string.Empty;

    /// <summary>
    /// 目标层号（1~N），用于数据库记录 Material.CurrentLayer。
    /// </summary>
    public int LayerNo { get; set; }

    /// <summary>
    /// 目标层的数据库主键 LayerID（如 "A01_L3"），用于更新层的剩余容量。
    /// </summary>
    public string LayerID { get; set; } = string.Empty;

    /// <summary>
    /// PLC 目标定位数值，直接写入 Addr_In_TargetCagePos (E 地址)。
    /// 计算公式：GridStartCoord + (LayerNo - 1) × Space
    /// </summary>
    public int TargetPosValue { get; set; }

    /// <summary>
    /// 笼子位置类型（"A笼" 或 "B笼"），用于入库时校验只允许 A笼。
    /// </summary>
    public string? LocationType { get; set; }
}
