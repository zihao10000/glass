namespace GlassWarehouseSystem.Models
{
    public enum MaterialStatus
    {
        Pending = 0,
        InStock = 1,
        Locked = 2,
        Outbounded = 3,
        Damaged = 4,
        Error = 5,

        空闲 = Pending,
        在库 = InStock,
        待入库 = Pending,
        锁定中 = Locked,
        已出库 = Outbounded,
        破损 = Damaged,
        异常 = Error
    }

    public enum CageStatus
    {
        可用 = 0,
        满载 = 1,
        作业中 = 2,
        故障 = 3,
        离线 = 4
    }
}
