using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Shapes;
using GlassWarehouseSystem.Models;

namespace GlassWarehouseSystem
{
    public partial class PrintWindowDraggable : Window
    {
        // 私有业务逻辑变量
        private bool isDragging = false;
        private Point clickPosition;
        private UIElement draggedElement = null;
        private UIElement selectedElement = null;
        private int selectedElementZIndex = 999;

        /// <summary>
        /// 当前选中的物料（用于填充打印标签字段）
        /// </summary>
        private Material? _material;

        private static readonly string LayoutFilePath =
            System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "print_layout.json");

        public PrintWindowDraggable()
        {
            InitializeComponent();
            this.Loaded += Window_Loaded;
        }

        /// <summary>
        /// 带物料数据的构造函数 — 打印时传入选中的物料以提取真实尺寸
        /// </summary>
        public PrintWindowDraggable(Material material) : this()
        {
            _material = material;
        }

        /// <summary>
        /// 静默打印物料标签：使用当前 XAML 排版模板，将其中的占位字段（长宽、客户、订单号等）
        /// 替换为传入物料的实际数据，直接发送到系统默认打印机，整个过程对用户不可见。
        ///
        /// 出笼归档成功后由 OutboundWindow 调用，避免每片都弹出打印窗口干扰操作。
        /// 通过将窗口放置在屏幕外+不可见+不入任务栏，仅借用 WPF 的 Loaded 事件让画布完成布局。
        /// </summary>
        /// <param name="material">已归档前缓存的物料对象（含 Order 导航属性）</param>
        /// <param name="log">可选日志回调，由调用方在 UI 线程输出</param>
        public static void PrintLabelSilently(Material material, Action<string>? log = null)
        {
            var win = new PrintWindowDraggable(material)
            {
                WindowStartupLocation = WindowStartupLocation.Manual,
                Left = -32000,
                Top = -32000,
                ShowInTaskbar = false,
                ShowActivated = false,
                Opacity = 0,
                Topmost = false
            };

            win.Loaded += (s, e) =>
            {
                try
                {
                    win.UpdateLayout();
                    if (win.FindName("canvasPreview") is not Canvas canvas)
                    {
                        log?.Invoke($"未找到画布 canvasPreview，跳过打印 {material.GlassID}");
                        return;
                    }

                    // 不调用 ShowDialog → 直接使用系统默认打印机静默打印
                    var printDialog = new PrintDialog();
                    printDialog.PrintVisual(canvas, $"玻璃标签 {material.GlassID}");
                    log?.Invoke($"已发送到默认打印机: {material.GlassID}  {material.Width:F3}×{material.Length:F3}");
                }
                catch (Exception ex)
                {
                    log?.Invoke($"打印失败 {material.GlassID}: {ex.Message}");
                }
                finally
                {
                    try { win.Close(); } catch { /* ignore */ }
                }
            };

            win.Show();
        }

        private void Window_Loaded(object sender, RoutedEventArgs e)
        {
            // 获取XAML中定义的控件
            Canvas horizontalRuler = this.FindName("canvasHorizontalRuler") as Canvas;
            Canvas verticalRuler = this.FindName("canvasVerticalRuler") as Canvas;

            if (horizontalRuler != null && verticalRuler != null)
            {
                DrawHorizontalRuler(horizontalRuler);
                DrawVerticalRuler(verticalRuler);
            }

            // 标记 XAML 中预定义的可拖动元素，便于保存/恢复布局时识别
            qrCodeElement.Tag        = "named:qrCodeElement";
            txtOriginalCustomer.Tag  = "named:txtOriginalCustomer";
            txtOrderNumber.Tag       = "named:txtOrderNumber";
            txtFloorNumber.Tag       = "named:txtFloorNumber";
            txtDimensions.Tag        = "named:txtDimensions";
            txtProductName.Tag       = "named:txtProductName";
            barcodeElement.Tag       = "named:barcodeElement";

            // 如果传入了物料数据，填充预览区和底部状态栏
            if (_material != null)
            {
                PopulateMaterialData(_material);
            }

            // 恢复上次保存的布局（仅在设计模式下，静默打印时不加载）
            if (Opacity > 0)
                LoadLayout();
        }

        /// <summary>
        /// 用真实物料数据填充预览区元素和底部信息栏
        /// </summary>
        private void PopulateMaterialData(Material m)
        {
            // 更新预览区中的"短边 x 长边"元素
            if (txtDimensions != null)
            {
                txtDimensions.Child = null;
                var sp = new StackPanel { Orientation = Orientation.Horizontal };
                sp.Children.Add(new TextBlock
                {
                    Text = $"{m.Width:F3} x {m.Length:F3}",
                    FontSize = 14,
                    FontWeight = FontWeights.Bold
                });
                txtDimensions.Child = sp;
            }

            // 更新预览区中的客户名
            if (txtOriginalCustomer?.Child is TextBlock tbCust)
                tbCust.Text = m.Order?.CustomerName ?? "未知客户";

            // 更新预览区中的订单号
            if (txtOrderNumber?.Child is TextBlock tbOrder)
                tbOrder.Text = m.Order?.OrderNo ?? "无订单号";

            // 更新预览区中的产品名
            if (txtProductName?.Child is TextBlock tbProd)
                tbProd.Text = m.ProductName ?? "未知产品";

            // 更新预览区中的楼层编号
            if (txtFloorNumber?.Child is TextBlock tbFloor)
                tbFloor.Text = m.CurrentLayer.HasValue ? $"第{m.CurrentLayer}层" : "未分配层";

            // 更新底部状态栏
            if (txtID != null) txtID.Text = m.GlassID.ToString().Substring(0, Math.Min(10, m.GlassID.ToString().Length));
            if (txtProcessCard != null) txtProcessCard.Text = m.Order?.FlowCardNo ?? "";
            if (txtCustomer != null) txtCustomer.Text = m.Order?.CustomerName ?? "";
            if (txtOrder != null) txtOrder.Text = m.Order?.OrderNo ?? "";
        }

        /// <summary>
        /// 根据字段名解析实际物料值
        /// </summary>
        private string ResolveFieldValue(string fieldName)
        {
            if (_material == null) return fieldName;

            return fieldName switch
            {
                "长边" => _material.Length.ToString("F3"),
                "短边" => _material.Width.ToString("F3"),
                "厚边" => _material.Thickness.ToString("F4"),
                "层边" => _material.CurrentLayer?.ToString() ?? "N/A",
                "笼边" => _material.CurrentCage ?? "N/A",
                "原产品名" => _material.ProductName ?? "未知",
                "原流程卡号" => _material.Order?.FlowCardNo ?? "未知",
                "客户名称" => _material.Order?.CustomerName ?? "未知",
                "订单编号" => _material.Order?.OrderNo ?? "未知",
                "计划宽度" => _material.Width.ToString("F3"),
                "计划厚度" => _material.Thickness.ToString("F4"),
                "计划长度" => _material.Length.ToString("F3"),
                "质量宽度" => _material.Width.ToString("F3"),
                "质量厚度" => _material.Thickness.ToString("F4"),
                "质量长度" => _material.Length.ToString("F3"),
                "归类ID" => _material.GlassID.ToString().Substring(0, Math.Min(10, _material.GlassID.ToString().Length)),
                "流水编号" => _material.GlassID.ToString(),
                "日期" => DateTime.Now.ToString("yyyy-MM-dd"),
                "时间" => DateTime.Now.ToString("HH:mm:ss"),
                "计划日期" => _material.InboundTime?.ToString("yyyy-MM-dd") ?? DateTime.Now.ToString("yyyy-MM-dd"),
                _ => fieldName
            };
        }

        private void DrawHorizontalRuler(Canvas canvas)
        {
            canvas.Children.Clear();
            double width = 750.0;
            double pixelsPerCm = 37.795;

            for (int cm = 0; cm <= (int)(width / pixelsPerCm); cm++)
            {
                double x = cm * pixelsPerCm;

                Line mainLine = new Line();
                mainLine.X1 = x;
                mainLine.Y1 = 15;
                mainLine.X2 = x;
                mainLine.Y2 = 25;
                mainLine.Stroke = Brushes.Black;
                mainLine.StrokeThickness = 1;
                canvas.Children.Add(mainLine);

                TextBlock label = new TextBlock();
                label.Text = cm.ToString();
                label.FontSize = 9;
                label.Foreground = Brushes.Black;
                Canvas.SetLeft(label, x - 5);
                Canvas.SetTop(label, 0);
                canvas.Children.Add(label);

                if (cm < (int)(width / pixelsPerCm))
                {
                    double x2 = (cm + 0.5) * pixelsPerCm;
                    Line minorLine = new Line();
                    minorLine.X1 = x2;
                    minorLine.Y1 = 20;
                    minorLine.X2 = x2;
                    minorLine.Y2 = 25;
                    minorLine.Stroke = Brushes.Gray;
                    minorLine.StrokeThickness = 0.5;
                    canvas.Children.Add(minorLine);
                }
            }
        }

        private void DrawVerticalRuler(Canvas canvas)
        {
            canvas.Children.Clear();
            double height = 900.0;
            double pixelsPerCm = 37.795;

            for (int cm = 0; cm <= (int)(height / pixelsPerCm); cm++)
            {
                double y = cm * pixelsPerCm;

                Line mainLine = new Line();
                mainLine.X1 = 15;
                mainLine.Y1 = y;
                mainLine.X2 = 25;
                mainLine.Y2 = y;
                mainLine.Stroke = Brushes.Black;
                mainLine.StrokeThickness = 1;
                canvas.Children.Add(mainLine);

                TextBlock label = new TextBlock();
                label.Text = cm.ToString();
                label.FontSize = 9;
                label.Foreground = Brushes.Black;
                Canvas.SetLeft(label, 0);
                Canvas.SetTop(label, y - 8);
                canvas.Children.Add(label);

                if (cm < (int)(height / pixelsPerCm))
                {
                    double y2 = (cm + 0.5) * pixelsPerCm;
                    Line minorLine = new Line();
                    minorLine.X1 = 20;
                    minorLine.Y1 = y2;
                    minorLine.X2 = 25;
                    minorLine.Y2 = y2;
                    minorLine.Stroke = Brushes.Gray;
                    minorLine.StrokeThickness = 0.5;
                    canvas.Children.Add(minorLine);
                }
            }
        }

        private void Element_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            UIElement element = sender as UIElement;
            if (element == null) return;

            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas == null) return;

            isDragging = true;
            draggedElement = element;
            selectedElement = element;

            Point mousePos = e.GetPosition(canvas);
            double currentLeft = Canvas.GetLeft(element);
            double currentTop = Canvas.GetTop(element);

            if (double.IsNaN(currentLeft)) currentLeft = 0;
            if (double.IsNaN(currentTop)) currentTop = 0;

            clickPosition = new Point(mousePos.X - currentLeft, mousePos.Y - currentTop);

            element.CaptureMouse();
            Panel.SetZIndex(element, selectedElementZIndex++);
            e.Handled = true;
        }

        private void Element_MouseMove(object sender, MouseEventArgs e)
        {
            if (!isDragging || draggedElement == null || e.LeftButton != MouseButtonState.Pressed)
                return;

            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas == null) return;

            Point mousePos = e.GetPosition(canvas);
            double newLeft = mousePos.X - clickPosition.X;
            double newTop = mousePos.Y - clickPosition.Y;

            FrameworkElement element = draggedElement as FrameworkElement;
            if (element != null)
            {
                double maxLeft = canvas.ActualWidth - element.ActualWidth;
                double maxTop = canvas.ActualHeight - element.ActualHeight;

                if (maxLeft > 0) newLeft = Math.Max(0, Math.Min(newLeft, maxLeft));
                if (maxTop > 0) newTop = Math.Max(0, Math.Min(newTop, maxTop));
            }

            Canvas.SetLeft(draggedElement, newLeft);
            Canvas.SetTop(draggedElement, newTop);
            e.Handled = true;
        }

        private void Element_MouseLeftButtonUp(object sender, MouseButtonEventArgs e)
        {
            if (isDragging && draggedElement != null)
            {
                draggedElement.ReleaseMouseCapture();
                isDragging = false;
                draggedElement = null;
                e.Handled = true;
            }
        }

        private void CanvasPreview_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
        {
            Canvas canvas = sender as Canvas;
            if (e.Source == canvas)
                selectedElement = null;
        }

        private void CanvasPreview_MouseMove(object sender, MouseEventArgs e) { }

        private void CanvasPreview_MouseLeftButtonUp(object sender, MouseButtonEventArgs e) { }

        private void BtnAddTextField_Click(object sender, RoutedEventArgs e)
        {
            ListBox lstFields = this.FindName("lstFields") as ListBox;
            if (lstFields == null || lstFields.SelectedItem == null)
            {
                MessageBox.Show("请先选择一个字段", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                return;
            }

            ListBoxItem item = lstFields.SelectedItem as ListBoxItem;
            if (item == null) return;

            string fieldText = item.Content.ToString();
            string fontFamily = "Arial";
            double fontSize = 12;
            FontWeight fontWeight = FontWeights.Normal;

            ComboBox cboFontFamily = this.FindName("cboFontFamily") as ComboBox;
            if (cboFontFamily != null)
            {
                ComboBoxItem fontItem = cboFontFamily.SelectedItem as ComboBoxItem;
                if (fontItem != null) fontFamily = fontItem.Content.ToString();
            }

            ComboBox cboFontSize = this.FindName("cboFontSize") as ComboBox;
            if (cboFontSize != null)
            {
                ComboBoxItem sizeItem = cboFontSize.SelectedItem as ComboBoxItem;
                if (sizeItem != null) double.TryParse(sizeItem.Content.ToString(), out fontSize);
            }

            CheckBox chkBold = this.FindName("chkBold") as CheckBox;
            if (chkBold != null && chkBold.IsChecked == true)
                fontWeight = FontWeights.Bold;

            TextBlock textBlock = new TextBlock();
            textBlock.Text = ResolveFieldValue(fieldText);
            textBlock.FontFamily = new FontFamily(fontFamily);
            textBlock.FontSize = fontSize;
            textBlock.FontWeight = fontWeight;

            Border border = new Border();
            border.Child = textBlock;
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            border.BorderThickness = new Thickness(1);
            border.Background = Brushes.White;
            border.Padding = new Thickness(5, 3, 5, 3);
            border.Cursor = Cursors.Hand;

            border.Tag = "dynamic:text";
            border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            border.MouseMove += Element_MouseMove;
            border.MouseLeftButtonUp += Element_MouseLeftButtonUp;

            Canvas.SetLeft(border, 300);
            Canvas.SetTop(border, 400);

            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas != null)
            {
                canvas.Children.Add(border);
                selectedElement = border;
            }
        }

        private void BtnAddQRCode_Click(object sender, RoutedEventArgs e)
        {
            Rectangle qrRect = new Rectangle();
            qrRect.Fill = Brushes.Black;
            qrRect.Width = 70;
            qrRect.Height = 70;

            Grid grid = new Grid();
            grid.Background = Brushes.White;
            grid.Children.Add(qrRect);

            Border border = new Border();
            border.Child = grid;
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            border.BorderThickness = new Thickness(1);
            border.Background = Brushes.White;
            border.Width = 80;
            border.Height = 80;
            border.Cursor = Cursors.Hand;

            border.Tag = "dynamic:qrcode";
            border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            border.MouseMove += Element_MouseMove;
            border.MouseLeftButtonUp += Element_MouseLeftButtonUp;

            Canvas.SetLeft(border, 50);
            Canvas.SetTop(border, 50);

            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas != null)
            {
                canvas.Children.Add(border);
                selectedElement = border;
            }
        }

        private void BtnAddBarcode_Click(object sender, RoutedEventArgs e)
        {
            Rectangle rect = new Rectangle();
            rect.Fill = Brushes.Transparent;
            rect.Stroke = Brushes.Gray;
            rect.StrokeDashArray = new DoubleCollection(new double[] { 2, 2 });

            TextBlock text = new TextBlock();
            text.Text = "条形码区域";
            text.HorizontalAlignment = HorizontalAlignment.Center;
            text.VerticalAlignment = VerticalAlignment.Center;
            text.Foreground = Brushes.Gray;

            Grid grid = new Grid();
            grid.Background = Brushes.White;
            grid.Children.Add(rect);
            grid.Children.Add(text);

            Border border = new Border();
            border.Child = grid;
            border.BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66));
            border.BorderThickness = new Thickness(1);
            border.Background = Brushes.White;
            border.Width = 200;
            border.Height = 60;
            border.Cursor = Cursors.Hand;

            border.Tag = "dynamic:barcode";
            border.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            border.MouseMove += Element_MouseMove;
            border.MouseLeftButtonUp += Element_MouseLeftButtonUp;

            Canvas.SetLeft(border, 100);
            Canvas.SetTop(border, 300);

            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas != null)
            {
                canvas.Children.Add(border);
                selectedElement = border;
            }
        }

        private void BtnDeleteSelected_Click(object sender, RoutedEventArgs e)
        {
            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas == null) return;

            if (selectedElement != null && canvas.Children.Contains(selectedElement))
            {
                canvas.Children.Remove(selectedElement);
                selectedElement = null;
                MessageBox.Show("已删除选中元素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show("请先选择要删除的元素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void LstFields_MouseDoubleClick(object sender, MouseButtonEventArgs e)
        {
            BtnAddTextField_Click(sender, e);
        }

        private void CboFontFamily_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedElementFont();
        }

        private void CboFontSize_SelectionChanged(object sender, SelectionChangedEventArgs e)
        {
            UpdateSelectedElementFont();
        }

        private void ChkBold_Checked(object sender, RoutedEventArgs e)
        {
            UpdateSelectedElementFont();
        }

        private void ChkBold_Unchecked(object sender, RoutedEventArgs e)
        {
            UpdateSelectedElementFont();
        }

        private void UpdateSelectedElementFont()
        {
            if (selectedElement == null) return;

            Border border = selectedElement as Border;
            if (border == null) return;

            TextBlock textBlock = border.Child as TextBlock;
            if (textBlock == null) return;

            ComboBox cboFontFamily = this.FindName("cboFontFamily") as ComboBox;
            if (cboFontFamily != null)
            {
                ComboBoxItem fontItem = cboFontFamily.SelectedItem as ComboBoxItem;
                if (fontItem != null)
                    textBlock.FontFamily = new FontFamily(fontItem.Content.ToString());
            }

            ComboBox cboFontSize = this.FindName("cboFontSize") as ComboBox;
            if (cboFontSize != null)
            {
                ComboBoxItem sizeItem = cboFontSize.SelectedItem as ComboBoxItem;
                if (sizeItem != null)
                {
                    double fs = 12;
                    if (double.TryParse(sizeItem.Content.ToString(), out fs))
                        textBlock.FontSize = fs;
                }
            }

            CheckBox chkBold = this.FindName("chkBold") as CheckBox;
            if (chkBold != null)
                textBlock.FontWeight = (chkBold.IsChecked == true) ? FontWeights.Bold : FontWeights.Normal;
        }

        private void BtnPrintSettings_Click(object sender, RoutedEventArgs e)
        {
            PrintDialog printDialog = new PrintDialog();
            printDialog.ShowDialog();
        }

        private void BtnPreview_Click(object sender, RoutedEventArgs e)
        {
            Canvas canvas = this.FindName("canvasPreview") as Canvas;
            if (canvas == null)
            {
                MessageBox.Show("未找到预览画布。", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                return;
            }

            // 取消选中状态的边框高亮（可选，如果不想打印出选中框）
            if (selectedElement is Border b)
            {
                b.BorderBrush = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)); // 恢复默认边框
                selectedElement = null;
            }

            PrintDialog printDialog = new PrintDialog();
            if (printDialog.ShowDialog() == true)
            {
                try
                {
                    printDialog.PrintVisual(canvas, "玻璃标签打印");
                    MessageBox.Show("打印任务已发送！", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show($"打印出错: {ex.Message}", "错误", MessageBoxButton.OK, MessageBoxImage.Error);
                }
            }
        }

        private void BtnMoveUp_Click(object sender, RoutedEventArgs e)
        {
            if (selectedElement != null)
            {
                int currentZ = Panel.GetZIndex(selectedElement);
                Panel.SetZIndex(selectedElement, currentZ + 1);
            }
            else
            {
                MessageBox.Show("请先选择要调整的元素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnMoveDown_Click(object sender, RoutedEventArgs e)
        {
            if (selectedElement != null)
            {
                int currentZ = Panel.GetZIndex(selectedElement);
                if (currentZ > 0)
                    Panel.SetZIndex(selectedElement, currentZ - 1);
            }
            else
            {
                MessageBox.Show("请先选择要调整的元素", "提示", MessageBoxButton.OK, MessageBoxImage.Information);
            }
        }

        private void BtnSaveLayout_Click(object sender, RoutedEventArgs e)
        {
            try
            {
                SaveLayout();
                MessageBox.Show($"布局已保存至：\n{LayoutFilePath}", "保存成功",
                    MessageBoxButton.OK, MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                MessageBox.Show($"保存失败：{ex.Message}", "错误",
                    MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }

        /// <summary>
        /// 将画布上所有可拖动元素的位置和属性序列化为 JSON 文件。
        /// 预定义命名元素（Tag 以 "named:" 开头）只保存位置；
        /// 动态新增元素（Tag 以 "dynamic:" 开头）保存完整信息。
        /// </summary>
        private void SaveLayout()
        {
            var layout = new PrintLayoutData();

            foreach (UIElement child in canvasPreview.Children)
            {
                if (child is not Border border) continue;

                double left = Canvas.GetLeft(border);
                double top  = Canvas.GetTop(border);
                if (double.IsNaN(left)) left = 0;
                if (double.IsNaN(top))  top  = 0;
                int zIndex = Panel.GetZIndex(border);
                string? tag = border.Tag?.ToString();

                if (!string.IsNullOrEmpty(tag) && tag.StartsWith("named:"))
                {
                    layout.NamedElements[tag] = new ElementPosition
                        { Left = left, Top = top, ZIndex = zIndex };
                }
                else if (!string.IsNullOrEmpty(tag) && tag.StartsWith("dynamic:"))
                {
                    var info = new DynamicElementInfo
                    {
                        Type   = tag.Split(':')[1],
                        Left   = left,
                        Top    = top,
                        Width  = double.IsNaN(border.Width)  ? 0 : border.Width,
                        Height = double.IsNaN(border.Height) ? 0 : border.Height,
                        ZIndex = zIndex
                    };

                    if (info.Type == "text" && border.Child is TextBlock tb)
                    {
                        info.Text       = tb.Text;
                        info.FontFamily = tb.FontFamily?.Source ?? "Arial";
                        info.FontSize   = tb.FontSize;
                        info.IsBold     = tb.FontWeight == FontWeights.Bold;
                    }

                    layout.DynamicElements.Add(info);
                }
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            File.WriteAllText(LayoutFilePath, JsonSerializer.Serialize(layout, options));
        }

        /// <summary>
        /// 从 JSON 文件恢复布局：更新预定义元素的位置，重建所有动态元素。
        /// </summary>
        private void LoadLayout()
        {
            if (!File.Exists(LayoutFilePath)) return;

            try
            {
                var json   = File.ReadAllText(LayoutFilePath);
                var layout = JsonSerializer.Deserialize<PrintLayoutData>(json);
                if (layout == null) return;

                // 恢复预定义元素位置
                foreach (var (tag, pos) in layout.NamedElements)
                {
                    string name = tag.StartsWith("named:") ? tag["named:".Length..] : tag;
                    if (this.FindName(name) is UIElement el)
                    {
                        Canvas.SetLeft(el, pos.Left);
                        Canvas.SetTop(el,  pos.Top);
                        Panel.SetZIndex(el, pos.ZIndex);
                    }
                }

                // 移除已有动态元素
                var toRemove = canvasPreview.Children
                    .OfType<Border>()
                    .Where(b => b.Tag?.ToString()?.StartsWith("dynamic:") == true)
                    .ToList();
                foreach (var el in toRemove)
                    canvasPreview.Children.Remove(el);

                // 重建动态元素
                foreach (var info in layout.DynamicElements)
                {
                    Border border = info.Type switch
                    {
                        "qrcode"  => BuildQRCodeBorder(),
                        "barcode" => BuildBarcodeBorder(),
                        _         => BuildTextBorder(info.Text, info.FontFamily, info.FontSize, info.IsBold)
                    };

                    border.Tag = $"dynamic:{info.Type}";
                    Canvas.SetLeft(border, info.Left);
                    Canvas.SetTop(border,  info.Top);
                    Panel.SetZIndex(border, info.ZIndex);
                    if (info.Width  > 0) border.Width  = info.Width;
                    if (info.Height > 0) border.Height = info.Height;

                    canvasPreview.Children.Add(border);
                }
            }
            catch (Exception ex)
            {
                System.Diagnostics.Debug.WriteLine($"[PrintWindow] LoadLayout failed: {ex.Message}");
            }
        }

        private Border BuildTextBorder(string text, string fontFamily, double fontSize, bool isBold)
        {
            var tb = new TextBlock
            {
                Text       = text,
                FontFamily = new FontFamily(fontFamily),
                FontSize   = fontSize,
                FontWeight = isBold ? FontWeights.Bold : FontWeights.Normal
            };
            var b = new Border
            {
                Child           = tb,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                BorderThickness = new Thickness(1),
                Background      = Brushes.White,
                Padding         = new Thickness(5, 3, 5, 3),
                Cursor          = Cursors.Hand
            };
            b.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            b.MouseMove           += Element_MouseMove;
            b.MouseLeftButtonUp   += Element_MouseLeftButtonUp;
            return b;
        }

        private Border BuildQRCodeBorder()
        {
            var grid = new Grid { Background = Brushes.White };
            grid.Children.Add(new Rectangle { Fill = Brushes.Black, Width = 70, Height = 70, Margin = new Thickness(4) });
            var b = new Border
            {
                Child           = grid,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                BorderThickness = new Thickness(1),
                Background      = Brushes.White,
                Width = 80, Height = 80,
                Cursor = Cursors.Hand
            };
            b.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            b.MouseMove           += Element_MouseMove;
            b.MouseLeftButtonUp   += Element_MouseLeftButtonUp;
            return b;
        }

        private Border BuildBarcodeBorder()
        {
            var rect = new Rectangle
            {
                Fill            = Brushes.Transparent,
                Stroke          = Brushes.Gray,
                StrokeDashArray = new DoubleCollection(new double[] { 2, 2 })
            };
            var tb = new TextBlock
            {
                Text                = "条形码区域",
                HorizontalAlignment = HorizontalAlignment.Center,
                VerticalAlignment   = VerticalAlignment.Center,
                Foreground          = Brushes.Gray
            };
            var grid = new Grid { Background = Brushes.White };
            grid.Children.Add(rect);
            grid.Children.Add(tb);
            var b = new Border
            {
                Child           = grid,
                BorderBrush     = new SolidColorBrush(Color.FromRgb(0x66, 0x66, 0x66)),
                BorderThickness = new Thickness(1),
                Background      = Brushes.White,
                Width = 200, Height = 60,
                Cursor = Cursors.Hand
            };
            b.MouseLeftButtonDown += Element_MouseLeftButtonDown;
            b.MouseMove           += Element_MouseMove;
            b.MouseLeftButtonUp   += Element_MouseLeftButtonUp;
            return b;
        }

        // ── 布局序列化数据模型 ──────────────────────────────────────────

        private class ElementPosition
        {
            public double Left   { get; set; }
            public double Top    { get; set; }
            public int    ZIndex { get; set; }
        }

        private class DynamicElementInfo
        {
            public string Type       { get; set; } = "text";
            public double Left       { get; set; }
            public double Top        { get; set; }
            public double Width      { get; set; }
            public double Height     { get; set; }
            public string Text       { get; set; } = "";
            public string FontFamily { get; set; } = "Arial";
            public double FontSize   { get; set; } = 12;
            public bool   IsBold     { get; set; }
            public int    ZIndex     { get; set; }
        }

        private class PrintLayoutData
        {
            public Dictionary<string, ElementPosition> NamedElements    { get; set; } = new();
            public List<DynamicElementInfo>            DynamicElements  { get; set; } = new();
        }
    }
}