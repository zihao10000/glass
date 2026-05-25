CREATE DATABASE IF NOT EXISTS GlassWarehouseDB;
USE GlassWarehouseDB;

-- 先按外键依赖顺序删除旧表，方便彻底重构
DROP TABLE IF EXISTS Layers;
DROP TABLE IF EXISTS MaterialsHistory;
DROP TABLE IF EXISTS Materials;
DROP TABLE IF EXISTS Orders;
DROP TABLE IF EXISTS Cages;
DROP TABLE IF EXISTS Logs;
DROP TABLE IF EXISTS Config;
DROP TABLE IF EXISTS PlcAddressMapping;

-- 1. 订单表 (Orders)
CREATE TABLE IF NOT EXISTS Orders (
    OrderID VARCHAR(50) PRIMARY KEY COMMENT '订单唯一流水号 (如系统自动生成的GUID或特定规则单号)',
    OrderNo VARCHAR(50) COMMENT '客户原始订单编号',
    FlowCardNo VARCHAR(50) COMMENT '流程卡号 (用于扫码检索)',
    CustomerName VARCHAR(100) COMMENT '客户名称',
    Status INT COMMENT '订单整体状态 (0: 未开始, 1: 生产中, 2: 已完成)',
    CreateTime DATETIME COMMENT '订单导入或创建时间'
) COMMENT='订单表';

-- 2. 物料表 (Materials)
CREATE TABLE IF NOT EXISTS Materials (
    GlassID VARCHAR(50) PRIMARY KEY COMMENT '玻璃的唯一标识ID',
    OrderID VARCHAR(50) COMMENT '关联的Orders表的OrderID',
    ProductName VARCHAR(100) COMMENT '产品名称',
    Length DECIMAL(10,2) COMMENT '玻璃长边 (单位：mm)',
    Width DECIMAL(10,2) COMMENT '玻璃短边 (单位：mm)',
    Thickness DECIMAL(5,2) COMMENT '玻璃厚度 (单位：mm)',
    Status INT COMMENT '核心状态机：0 = 未入笼 1 = 入笼A 2 = 入笼B 3 = 出笼B 4 = 已出库(最终状态)',
    IsDamaged BIT COMMENT '是否标记破损 (0: 正常, 1: 破损)',
    CurrentCage VARCHAR(20) COMMENT '当前所在笼编号 (A 或 B)',
    CurrentLayer INT COMMENT '当前所在笼的层数 (1~120)',
    InboundTime DATETIME COMMENT '实际入笼时间',
    OutboundTime DATETIME COMMENT '实际出笼时间',
    FOREIGN KEY (OrderID) REFERENCES Orders(OrderID) ON DELETE SET NULL
) COMMENT='物料表';

-- 2.5 物料历史表 (MaterialsHistory)
CREATE TABLE IF NOT EXISTS MaterialsHistory (
    HistoryID INT AUTO_INCREMENT PRIMARY KEY COMMENT '历史记录自增ID',
    GlassID VARCHAR(50) COMMENT '玻璃的唯一标识ID',
    OrderID VARCHAR(50) COMMENT '关联的Orders表的OrderID',
    ProductName VARCHAR(100) COMMENT '产品名称',
    Length DECIMAL(10,2) COMMENT '玻璃长边 (单位：mm)',
    Width DECIMAL(10,2) COMMENT '玻璃短边 (单位：mm)',
    Thickness DECIMAL(5,2) COMMENT '玻璃厚度 (单位：mm)',
    Status INT COMMENT '历史记录状态',
    IsDamaged BIT COMMENT '是否标记破损 (0: 正常, 1: 破损)',
    CurrentCage VARCHAR(20) COMMENT '当前所在笼编号 (A 或 B)',
    CurrentLayer INT COMMENT '当前所在笼的层数 (1~120)',
    InboundTime DATETIME COMMENT '实际入笼时间',
    OutboundTime DATETIME COMMENT '实际出笼时间',
    ArchiveTime DATETIME COMMENT '归档入历史表的时间'
) COMMENT='物料历史表(与物料表强对应)';

-- 3. 笼表 (Cages)
CREATE TABLE IF NOT EXISTS Cages (
    CageCode VARCHAR(50) PRIMARY KEY COMMENT '主键。笼的唯一编码，如：VM01',
    EquipmentName VARCHAR(100) COMMENT '绑定的设备名称/型号，如：D7 移动立理笼',
    CageType INT COMMENT '笼类型',
    LayerCount INT COMMENT '总层数，如：120',
    Length DECIMAL(10,2) COMMENT '物理长度 (mm)',
    Width DECIMAL(10,2) COMMENT '物理宽度 (mm)',
    Height DECIMAL(10,2) COMMENT '物理高度 (mm)',
    ZeroCoordinate DECIMAL(10,2) COMMENT '零点坐标',
    NegativeLimit DECIMAL(10,2) COMMENT '负向限位',
    PositiveLimit DECIMAL(10,2) COMMENT '正向限位',
    InboundGap DECIMAL(8,2) COMMENT '入笼口间隙',
    OutboundGap DECIMAL(8,2) COMMENT '出笼口间隙',
    CageSequenceNo INT COMMENT '笼序号',
    XCoordinate DECIMAL(10,2) COMMENT 'X坐标',
    YCoordinate DECIMAL(10,2) COMMENT 'Y坐标',
    TransitionLayerNo INT COMMENT '过渡层序号',
    GridInitSeqNo INT COMMENT '栅格初始序号',
    GridStartCoord DECIMAL(10,2) COMMENT '栅格起始坐标',
    IsOneWay BIT COMMENT '是否单向出入笼',
    LocationType VARCHAR(10) COMMENT '单选：A笼 或 B笼',
    UpdateTime DATETIME COMMENT '最后一次修改配置的时间'
) COMMENT='笼表';

-- 4. 层表 (Layers)
CREATE TABLE IF NOT EXISTS Layers (
    LayerID VARCHAR(50) PRIMARY KEY COMMENT '唯一标识 (如 A-001, B-120)',
    CageID VARCHAR(50) COMMENT '关联到 Cages 表 (注: MD中写CageID, 对应Cages表的CageCode)',
    LayerNo INT COMMENT '层号 (1 到 120)',
    IsOccupied BIT COMMENT '是否被占用 (0: 空闲, 1: 已存玻璃)',
    GlassID VARCHAR(50) COMMENT '当前存放的玻璃ID。如果为空，则表示该层空闲',
    PLCAddress VARCHAR(50) COMMENT '该层对应的独立 PLC 地址',
    FOREIGN KEY (CageID) REFERENCES Cages(CageCode) ON DELETE CASCADE,
    FOREIGN KEY (GlassID) REFERENCES Materials(GlassID) ON DELETE SET NULL
) COMMENT='层表';

-- 5. 日志表 (Logs)
CREATE TABLE IF NOT EXISTS Logs (
    LogID INT AUTO_INCREMENT PRIMARY KEY COMMENT '日志流水号',
    LogContent VARCHAR(500) COMMENT '具体操作内容或报错详情',
    RecordTime DATETIME COMMENT '发生时间'
) COMMENT='日志表';

-- 6. 参数表 (Config)
CREATE TABLE IF NOT EXISTS Config (
    ConfigID INT PRIMARY KEY COMMENT '配置表ID（通常固定为 1，单行记录）',
    MeasureErrorAllowance DECIMAL(8,2) COMMENT '测量长宽时的允许误差范围 (单位：mm)',
    DataSourceType INT COMMENT '枚举值。0：扫码获取，1：测量获取',
    MaxScanRetryTimes INT COMMENT '扫码失败的最大重试次数 (可变的 n 值)',
    CageInnerDistanceK DECIMAL(8,2) COMMENT '笼内物理计算距离参数 K (单位：mm)',
    GlassSpacing DECIMAL(8,2) COMMENT '相邻玻璃存放时的安全间距 (单位：mm)',
    UpdateTime DATETIME COMMENT '最后一次修改参数的时间'
) COMMENT='参数表';

-- 7. PLC 地址映射表 (PlcAddressMapping)
CREATE TABLE IF NOT EXISTS PlcAddressMapping (
    KeyName VARCHAR(100) PRIMARY KEY COMMENT '逻辑地址名称，如：Addr_In_GlassArrived',
    RealAddress VARCHAR(100) COMMENT '真实物理地址，如：DB10.DBW0',
    DataType VARCHAR(50) COMMENT '数据类型，如：Int, Float, Bool',
    Description VARCHAR(200) COMMENT '地址描述，如：入笼检查到位'
) COMMENT='PLC物理地址映射配置表';
