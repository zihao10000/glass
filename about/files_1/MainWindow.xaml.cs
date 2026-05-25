using System;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using HslCommunication;
using HslCommunication.ModBus;
using HslDataFormat = HslCommunication.Core.DataFormat;

namespace PlcModbusTool
{
    public partial class MainWindow : Window
    {
        private ModbusTcpNet _modbus = null;

        public MainWindow()
        {
            InitializeComponent();
        }

        // ── 连接 / 断开 ────────────────────────────────────────────
        private void BtnConnect_Click(object sender, RoutedEventArgs e)
        {
            if (_modbus != null)
            {
                try { _modbus.ConnectClose(); } catch { }
                _modbus = null;
                SetStatus(false);
                Log("已断开连接");
                return;
            }

            if (!int.TryParse(txtPort.Text.Trim(), out int port))
            { Log("端口号无效"); return; }

            if (!byte.TryParse(txtStation.Text.Trim(), out byte station))
            { Log("站号无效（范围 0-255）"); return; }

            try
            {
                string dfStr = ((ComboBoxItem)cmbDataFormat.SelectedItem).Content.ToString();
                HslDataFormat df = (HslDataFormat)Enum.Parse(typeof(HslDataFormat), dfStr);

                _modbus = new ModbusTcpNet(txtIp.Text.Trim(), port, station)
                {
                    AddressStartWithZero = chkAddrZero.IsChecked == true,
                    DataFormat           = df
                };

                OperateResult r = _modbus.ConnectServer();

                if (r.IsSuccess)
                {
                    SetStatus(true);
                    Log("连接成功  IP=" + txtIp.Text + "  端口=" + port +
                        "  DataFormat=" + df + "  地址从零=" + chkAddrZero.IsChecked);
                }
                else
                {
                    _modbus = null;
                    SetStatus(false, "连接失败");
                    Log("连接失败: " + r.Message);
                }
            }
            catch (Exception ex)
            {
                _modbus = null;
                SetStatus(false, "连接失败");
                Log("连接异常: " + ex.Message);
            }
        }

        // ── 写入 ───────────────────────────────────────────────────
        private void BtnWrite_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckConnected()) return;

            string addr = txtAddress.Text.Trim();
            string type = ((ComboBoxItem)cmbType.SelectedItem).Content.ToString();
            string val  = txtValue.Text.Trim();

            if (string.IsNullOrEmpty(addr)) { Log("请输入地址"); return; }
            if (string.IsNullOrEmpty(val))  { Log("请输入值");   return; }

            try
            {
                OperateResult wr;
                switch (type)
                {
                    case "bool":   wr = _modbus.Write(addr, bool.Parse(val));               break;
                    case "short":  wr = _modbus.Write(addr, short.Parse(val));              break;
                    case "int":    wr = _modbus.Write(addr, int.Parse(val));                break;
                    case "float":  wr = _modbus.Write(addr, float.Parse(val));              break;
                    case "double": wr = _modbus.Write(addr, double.Parse(val));             break;
                    case "string": wr = _modbus.Write(addr, val);                           break;
                    default:       Log("不支持的类型: " + type);                            return;
                }

                if (wr.IsSuccess)
                {
                    Log("写入成功  [" + addr + "]  " + type + " = " + val);
                    DoRead(addr, type);
                }
                else
                {
                    Log("写入失败: " + wr.Message);
                }
            }
            catch (Exception ex)
            {
                Log("写入异常: " + ex.Message);
            }
        }

        // ── 读取 ───────────────────────────────────────────────────
        private void BtnRead_Click(object sender, RoutedEventArgs e)
        {
            if (!CheckConnected()) return;

            string addr = txtAddress.Text.Trim();
            string type = ((ComboBoxItem)cmbType.SelectedItem).Content.ToString();

            if (string.IsNullOrEmpty(addr)) { Log("请输入地址"); return; }

            try
            {
                DoRead(addr, type);
            }
            catch (Exception ex)
            {
                Log("读取异常: " + ex.Message);
            }
        }

        // Enter 键触发写入
        private void TxtValue_KeyDown(object sender, KeyEventArgs e)
        {
            if (e.Key == Key.Enter)
                BtnWrite_Click(sender, e);
        }

        // 清空日志
        private void BtnClearLog_Click(object sender, RoutedEventArgs e)
        {
            rtbLog.Document.Blocks.Clear();
        }

        // ── 核心读取 ───────────────────────────────────────────────
        private void DoRead(string addr, string type)
        {
            string result = "";
            bool   ok     = false;

            switch (type)
            {
                case "bool":
                    var rb = _modbus.ReadBool(addr);
                    ok = rb.IsSuccess; result = rb.Content.ToString();
                    break;
                case "short":
                    var rs = _modbus.ReadInt16(addr);
                    ok = rs.IsSuccess; result = rs.Content.ToString();
                    break;
                case "int":
                    var ri = _modbus.ReadInt32(addr);
                    ok = ri.IsSuccess; result = ri.Content.ToString();
                    break;
                case "float":
                    var rf = _modbus.ReadFloat(addr);
                    ok = rf.IsSuccess; result = rf.Content.ToString("F2");
                    break;
                case "double":
                    var rd = _modbus.ReadDouble(addr);
                    ok = rd.IsSuccess; result = rd.Content.ToString("F2");
                    break;
                case "string":
                    var rstr = _modbus.ReadString(addr, 10);
                    ok = rstr.IsSuccess; result = rstr.Content;
                    break;
            }

            if (ok)
            {
                txtValue.Text = result;
                Log("回读成功  [" + addr + "]  " + type + " = " + result);
            }
            else
            {
                Log("读取失败");
            }
        }

        // ── 工具 ───────────────────────────────────────────────────
        private bool CheckConnected()
        {
            if (_modbus != null) return true;
            Log("请先连接 PLC");
            return false;
        }

        private void SetStatus(bool connected, string overrideText = null)
        {
            if (connected)
            {
                lblStatus.Text       = "● 已连接  " + txtIp.Text + ":" + txtPort.Text;
                lblStatus.Foreground = Brushes.Green;
                btnConnect.Content   = "断开连接";
            }
            else
            {
                lblStatus.Text       = "● " + (overrideText ?? "未连接");
                lblStatus.Foreground = Brushes.Red;
                btnConnect.Content   = "连接 PLC";
            }
        }

        private void Log(string msg)
        {
            Paragraph para = new Paragraph(new Run("[" + DateTime.Now.ToString("HH:mm:ss") + "]  " + msg))
            {
                Margin = new Thickness(0)
            };
            rtbLog.Document.Blocks.Add(para);
            rtbLog.ScrollToEnd();
        }

        protected override void OnClosed(EventArgs e)
        {
            if (_modbus != null)
            {
                try { _modbus.ConnectClose(); } catch { }
            }
            base.OnClosed(e);
        }
    }
}
