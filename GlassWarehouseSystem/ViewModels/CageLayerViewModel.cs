using GlassWarehouseSystem.Models;
using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace GlassWarehouseSystem.ViewModels;

/// <summary>
/// 对前端 UI 中的那一大堆由动态笼子、每一层玻璃层架框呈现所做的数据与通知隔离呈现桥接。
/// 它是一个将纯粹没有任何UI特征响应能力的 `Layer` 和 `Material` 业务模型捏合打包、掺加上 `IsSelected` 及动态改色运算 `BackgroundColor` 用于控制 XAML WPF 层如何做触发刷新 UI 外观绑核的驱动体！
/// 通过了实现 INotifyPropertyChanged ，一旦后台任何一个层信息发生了变卖或更名属性，界面自动知道变化该格子的颜色和文字不需任何背后代码强查并更新界线。
/// </summary>
public class CageLayerViewModel : INotifyPropertyChanged
{
    /// <summary>该ViewModel表征的那行物理代表的展示用真实该位置是第一几的层数序号编号号</summary>
    public int LayerNo { get; set; }
    
    /// <summary>它的父亲所在铁大货架名称缩写（A01等）</summary>
    public string CageID { get; set; } = string.Empty;

    // 以下所有这些打有 SetField 监控的属性，均可以支持绑定进 DataTemplate 中作呈现。
    // 一旦修改某项如：vm.Status = MaterialStatus.Damaged。SetField即会察觉改变而大肆通报 WPF界说本对象的那个字段变了！WPF框架便自动来抽新值变到屏幕外给实施者。

    private string? _materialID;
    public string? MaterialID
    {
        get => _materialID;
        set => SetField(ref _materialID, value);
    }

    private string _clientName = string.Empty;
    public string ClientName
    {
        get => _clientName;
        set => SetField(ref _clientName, value);
    }

    private string _orderNo = string.Empty;
    public string OrderNo
    {
        get => _orderNo;
        set => SetField(ref _orderNo, value);
    }

    private decimal _width;
    public decimal Width
    {
        get => _width;
        set => SetField(ref _width, value);
    }

    private decimal _length;
    public decimal Length
    {
        get => _length;
        set => SetField(ref _length, value);
    }

    private decimal _thickness;
    public decimal Thickness
    {
        get => _thickness;
        set => SetField(ref _thickness, value);
    }

    private string _flowCardNo = string.Empty;
    public string FlowCardNo
    {
        get => _flowCardNo;
        set => SetField(ref _flowCardNo, value);
    }

    private string _sequence = string.Empty;
    public string Sequence
    {
        get => _sequence;
        set => SetField(ref _sequence, value);
    }

    private MaterialStatus _status;
    public MaterialStatus Status
    {
        get => _status;
        set
        {
            if (SetField(ref _status, value))
            {
                // 在属性变化附加操作里：不仅通报本状态字变动，连带呼叫要求刷新外挂算出的底色BackgroundColor
                OnPropertyChanged(nameof(BackgroundColor));
            }
        }
    }

    /// <summary>
    /// 最为核心且酷炫的响应式被绑点算算特性。这里根据玻璃身背的那个 Status 标识所做状态机推测应该刷个如何警示反馈色的给展示人员。
    /// 前台那个界面格子的 Background=" {Binding BackgroundColor}" 就指着这个属性变脸换色！
    /// </summary>
    public string BackgroundColor => Status switch
    {
        MaterialStatus.InStock => "#0000FF", // 有货蓝色
        MaterialStatus.Damaged => "#FF0000", // 破废红色
        MaterialStatus.Locked  => "#FFA500", // 预出库了不让碰黄色锁定挂边（橙）
        MaterialStatus.Error   => "#FFFF00", // 报错错乱抛黄
        _ => "#333333"                       // 空黑底色无状态无物品的空层没放置物品空载深灰
    };

    private bool _isSelected;
    /// <summary>
    /// 被用来关联那个可让用户在UI操作格子时产生光标打白发包被选取到特效反馈状态支持标识
    /// </summary>
    public bool IsSelected
    {
        get => _isSelected;
        set
        {
            if (SetField(ref _isSelected, value))
            {
                // 一旦选定变更为被主圈住：立刻抛信息号令重算描边显示亮度，使其突出
                OnPropertyChanged(nameof(BorderColor));
            }
        }
    }

    /// <summary>展示方若发光白突发其是否目前受用户圈选中之展示勾边色彩结果回抛值</summary>
    public string BorderColor => IsSelected ? "#FFFFFF" : "#666666";

    /// <summary>
    /// 工具型灌注封口接口法。让 ViewModel 完全断掉和 EF Core 或者是具体后台提取方式的联系仅保留如何用展示传参数去接受纯在底层组转好抛过来的数据源实体去冲刷更新目前内部。
    /// 有人用了将重置或者赋值它。如果传null表示该位格空层将其归回深灰待战初始化空态设置。
    /// </summary>
    private bool _isOccupied;
    public bool IsOccupied
    {
        get => _isOccupied;
        set
        {
            _isOccupied = value;
            OnPropertyChanged();
            OnPropertyChanged(nameof(BackgroundColor)); // 状态一变，背景色跟着变
        }
    }
    public void LoadFromEntity(Layer layer, Material? material, string realCustomer, string realOrderNo, string realFlowCard, int? orderAutoId)
    {
        // 1. 只读 IsOccupied 状态，直接决定颜色
        this.IsOccupied = (layer.IsOccupied ?? false) || material != null;
        this.LayerNo = layer.LayerNo ?? 0;
        this.CageID = layer.CageID;

        // 2. 只要有货，就把关联的那条玻璃信息挂载上来显示
        // 2. 只要有货，且传进来的物料不为空
        if (this.IsOccupied && material != null)
        {
            this.MaterialID = material.GlassID;

            // 【关键修正】：这里直接使用从 Order 表 Join 出来的真实数据
            // 彻底抛弃 material.ProductName 赋值给 ClientName 的错误逻辑
            this.ClientName = realCustomer;  // 真正的客户名
            this.OrderNo = realOrderNo;      // 真正的订单号 (OrderNo)
            this.FlowCardNo = realFlowCard;  // 真正的流程卡号 (FlowCardNo)

            // 物理属性依然来自物料表
            this.Width = (decimal)(material.Width);
            this.Length = (decimal)(material.Length);
            this.Thickness = (decimal)(material.Thickness);
            // this.Sequence = "1";
            // ==========================================
            // 【核心修改】：将订单表的自增 ID 赋值给定序
            // 如果 orderAutoId 有值则显示，否则显示 "-"
            // ==========================================
            this.Sequence = orderAutoId?.ToString() ?? "-";
        }
        else
        {
            // 3. 没货就清空详情 (保留你原有的清空逻辑)
            this.MaterialID = null;
            this.ClientName = "";
            this.OrderNo = "";
            this.Width = 0;
            this.Length = 0;
            this.Thickness = 0;
            this.FlowCardNo = "";
            this.Sequence = "";
        }
    }


    // --- 【纯正的 INotifyPropertyChanged 通讯机制实现样板】 ---

    public event PropertyChangedEventHandler? PropertyChanged;

    /// <summary>
    /// 通用发通报兼设新值变更捕手保护助手：如果有不一样的值再设入字段并发发信号；防止重复给旧值相同不断死锁空发信通知刷废UI卡死帧数。
    /// CallerMemberName 特性支持自动不用手打当前调它的属性名字
    /// </summary>
    private bool SetField<T>(ref T field, T value, [CallerMemberName] string? propertyName = null)
    {
        if (Equals(field, value)) return false;

        field = value;
        OnPropertyChanged(propertyName);
        return true;
    }

    private void OnPropertyChanged([CallerMemberName] string? propertyName = null)
    {
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
    }
}
