using System;
using System.ComponentModel;
using System.Runtime.CompilerServices;
using System.Windows.Media; // 需要引用 PresentationCore 程序集
using GlassWarehouseSystem.Models; // 引用你的 Models 命名空间

namespace GlassWarehouseSystem.ViewModels
{

    /// <summary>
    /// 看板专用：每一层（每一个黑框框）的视图模型
    /// </summary>
    public class CageLayerViewModel : INotifyPropertyChanged
    {
        // ===========================
        // 1. 基础数据属性
        // ===========================

        public int LayerNo { get; set; }      // 层号 (显示在左上角)
        public string CageID { get; set; }    // 笼号

        // 这一层里存放的物料的详细信息 (用于底部详情栏显示)
        private string _materialID;
        public string MaterialID
        {
            get => _materialID;
            set { _materialID = value; OnPropertyChanged(); OnPropertyChanged(nameof(BackgroundColor)); } // ID改变时，触发背景色更新
        }

        private string _clientName;
        public string ClientName
        {
            get => _clientName;
            set { _clientName = value; OnPropertyChanged(); }
        }

        private string _orderNo;
        public string OrderNo
        {
            get => _orderNo;
            set { _orderNo = value; OnPropertyChanged(); }
        }

        private float _width;
        public float Width
        {
            get => _width;
            set { _width = value; OnPropertyChanged(); }
        }

        private float _length;
        public float Length
        {
            get => _length;
            set { _length = value; OnPropertyChanged(); }
        }

        private float _thickness;
        public float Thickness
        {
            get => _thickness;
            set { _thickness = value; OnPropertyChanged(); }
        }

        private string _flowCardNo;
        public string FlowCardNo
        {
            get => _flowCardNo;
            set { _flowCardNo = value; OnPropertyChanged(); }
        }

        private string _sequence; // 对应“订序”
        public string Sequence
        {
            get => _sequence;
            set { _sequence = value; OnPropertyChanged(); }
        }


        // ===========================
        // 2. UI 视觉属性 (核心)
        // ===========================

        // 状态：用于逻辑判断

        //这快应该没用上
        private MaterialStatus _status;

        public MaterialStatus Status
        {
            get => _status;
            set
            {
                _status = value;
                OnPropertyChanged(); // 通知数据变了
                OnPropertyChanged(nameof(BackgroundColor)); // 核心：状态变了，通知颜色也该变了！
            }
        }

        // 背景颜色字符串 (绑定到 XAML 的 Background)
        //public string BackgroundColor
        //{
        //    get
        //    {
        //        // 如果没有物料，或是空的
        //        if (string.IsNullOrEmpty(MaterialID))
        //        {
        //            return "#333333"; // 黑色/深灰 (空闲状态)
        //        }

        //        // 根据 MaterialStatus 枚举返回对应颜色
        //        switch (_status)
        //        {
        //            case MaterialStatus.在库:
        //                return "#0000FF"; // 蓝色 (正常)
        //            case MaterialStatus.破损:
        //                return "#FF0000"; // 红色 (破损)
        //            case MaterialStatus.锁定中:
        //                return "#FFA500"; // 橙色 (锁定)
        //            case MaterialStatus.异常:
        //                return "#FFFF00"; // 黄色 (异常)
        //            default:
        //                return "#0000FF"; // 默认蓝色
        //        }
        //    }
        //}

        public string BackgroundColor
        {
            get
            {
                // 逻辑极其简单：只看笼层是否被占用
                // 占用 = 蓝色，未占用 = 黑色/深灰
                return IsOccupied ? "#0000FF" : "#333333";
            }
        }

        // 选中状态 (用于给框框加个白色边框，表示被选中)
        private bool _isSelected;
        public bool IsSelected
        {
            get => _isSelected;
            set
            {
                _isSelected = value;
                OnPropertyChanged();
                OnPropertyChanged(nameof(BorderColor)); // 选中变了，边框颜色也要变
            }
        }

        public string BorderColor => IsSelected ? "#FFFFFF" : "#666666"; // 选中白边，没选中灰边

        // ===========================
        // 3. 构造函数与数据加载
        // ===========================
        // 核心：对应 CageLayer 实体类中的 IsOccupied
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
        public CageLayerViewModel() { }

        /// <summary>
        /// 辅助方法：从数据库实体加载数据
        /// </summary>
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

                // 【关键修正】：这里直接使用从 Orders 表 Join 出来的真实数据
                // 彻底抛弃 material.ProductName 赋值给 ClientName 的错误逻辑
                this.ClientName = realCustomer;  // 真正的客户名
                this.OrderNo = realOrderNo;      // 真正的订单号 (OrderNo)
                this.FlowCardNo = realFlowCard;  // 真正的流程卡号 (FlowCardNo)

                // 【终极修复】：在数字后面加 M，明确告诉编译器这是 decimal 默认值，彻底解决 CS0266 报错
                // =================【终极破局：统一强转为 float】=================
                // 物理属性依然来自物料表（因为你ViewModel里的属性是 float 类型）
                this.Width = (float)material.Width;
                this.Length = (float)material.Length;
                this.Thickness = (float)material.Thickness;
                // ===============================================================
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

        // ===========================
        // 4. INotifyPropertyChanged 实现 (WPF 固定写法)
        // ===========================
        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged([CallerMemberName] string propertyName = null)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }
    }

    // ====================================================================
    // 删除了 daiding 后缀，与 QueryWindow.xaml.cs 调用的类名严格保持一致
    // ====================================================================

    
}