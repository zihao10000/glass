using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using MvCodeReaderSDKNet;

namespace GlassWarehouseSystem.Services
{
    /// 海康威视/海康机器人扫码器服务封装类
    /// 负责设备的枚举、连接、开启采集以及使用离线线程自动获取和解码图像内容。
    /// 请确保项目已添加对 Development\Modules\MvCodeReaderSDK\DotNet\win64\MvCodeReaderSDK.Net.dll 的引用。
    public class CameraScannerService : IDisposable
    {
        private MvCodeReader _device = null;
        private bool _isGrabbing = false;
        private Thread _receiveThread = null;

        /// <summary>
        /// 当识别到条码时引发此事件
        /// </summary>
        public event EventHandler<string> OnBarcodeScanned;

        public CameraScannerService()
        {
            _device = new MvCodeReader();
        }


        /// <returns>连接成功返回 true</returns>
        public bool ConnectCamera()
        {
            MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST stDeviceList = new MvCodeReader.MV_CODEREADER_DEVICE_INFO_LIST();
            int nRet = MvCodeReader.MV_CODEREADER_EnumDevices_NET(ref stDeviceList, MvCodeReader.MV_CODEREADER_GIGE_DEVICE);

            if (nRet != MvCodeReader.MV_CODEREADER_OK)
            {
                throw new Exception($"枚举设备失败! Error Code: {nRet:X}");
            }

            if (stDeviceList.nDeviceNum == 0)
            {
                throw new Exception("没有找到相机的设备！局域网内无海康扫码设备。");
            }

            MvCodeReader.MV_CODEREADER_DEVICE_INFO stDevInfo =
                (MvCodeReader.MV_CODEREADER_DEVICE_INFO)Marshal.PtrToStructure(
                    stDeviceList.pDeviceInfo[0],
                    typeof(MvCodeReader.MV_CODEREADER_DEVICE_INFO));

            nRet = _device.MV_CODEREADER_CreateHandle_NET(ref stDevInfo);
            if (nRet != MvCodeReader.MV_CODEREADER_OK)
            {
                throw new Exception($"创建设备句柄失败! Error Code: {nRet:X}");
            }

            nRet = _device.MV_CODEREADER_OpenDevice_NET();
            if (nRet != MvCodeReader.MV_CODEREADER_OK)
            {
                _device.MV_CODEREADER_DestroyHandle_NET();
                throw new Exception($"打开设备失败! Error Code: {nRet:X}");
            }

            // 尝试开启自动曝光和自动增益（部分相机支持 2=Continuous）
            _device.MV_CODEREADER_SetEnumValue_NET("ExposureAuto", 2);
            _device.MV_CODEREADER_SetEnumValue_NET("GainAuto", 2);

            // 设置为连续采集模式
            nRet = _device.MV_CODEREADER_SetEnumValue_NET("TriggerMode", (uint)MvCodeReader.MV_CODEREADER_TRIGGER_MODE.MV_CODEREADER_TRIGGER_MODE_OFF);
            if (nRet != MvCodeReader.MV_CODEREADER_OK)
            {
                throw new Exception($"配置触发模式失败! Error Code: {nRet:X}");
            }

            // 补充：尝试通过代码直接开启常见的条形码和二维码码制 (QR, DM, Code128, EAN13, Code39)
            try
            {
                int[] symbologies = new int[] 
                { 
                    (int)MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_QR,
                    (int)MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_TDCR_DM,
                    (int)MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE128,
                    (int)MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_EAN13,
                    (int)MvCodeReader.MV_CODEREADER_CODE_TYPE.MV_CODEREADER_BCR_CODE39
                };
                foreach (var sym in symbologies)
                {
                    // 1. 在参数树中选中该码制
                    _device.MV_CODEREADER_SetEnumValue_NET("Symbology", (uint)sym);
                    // 2. 将该码制使能开关设为 true
                    _device.MV_CODEREADER_SetBoolValue_NET("SymbologyEnable", true);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine("通过代码动态配置码制出现异常: " + ex.Message);
            }

            return true;
        }

        /// <summary>
        /// 触发一次自动对焦（相机必须支持电动变焦）
        /// </summary>
        public void TriggerAutofocus()
        {
            if (_device != null)
            {
                // 忽略返回值，如果不支持对焦就略过
                _device.MV_CODEREADER_SetCommandValue_NET("FocusSearch");
                _device.MV_CODEREADER_SetCommandValue_NET("Focus");
            }
        }

        /// <summary>
        /// 开始从相机连续获取图像并解析条码内容
        /// </summary>
        public void StartScanning()
        {
            if (_isGrabbing) return;

            _receiveThread = new Thread(ReceiveThreadProcess);
            _receiveThread.IsBackground = true; 
            _isGrabbing = true;
            _receiveThread.Start();

            // 开始向底层发送 StartGrabbing 指令
            int nRet = _device.MV_CODEREADER_StartGrabbing_NET();
            if (nRet != MvCodeReader.MV_CODEREADER_OK)
            {
                _isGrabbing = false;
                _receiveThread.Join();
                throw new Exception($"开启采集失败! Error Code: {nRet:X}");
            }
        }

        /// <summary>
        /// 接收并在后台解析相机的帧信息
        /// </summary>
        private void ReceiveThreadProcess()
        {
            int nRet = MvCodeReader.MV_CODEREADER_OK;
            IntPtr pData = IntPtr.Zero;
            
            MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2 stFrameInfoEx2 = new MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2();
            IntPtr pstFrameInfoEx2 = Marshal.AllocHGlobal(Marshal.SizeOf(typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)));
            Marshal.StructureToPtr(stFrameInfoEx2, pstFrameInfoEx2, false);

            try
            {
                while (_isGrabbing)
                {
                    try
                    {
                        // 获取一帧，超时时间设为1秒 (1000ms)，未获取到则继续等待
                        nRet = _device.MV_CODEREADER_GetOneFrameTimeoutEx2_NET(ref pData, pstFrameInfoEx2, 1000);
                        if (nRet != MvCodeReader.MV_CODEREADER_OK)
                            continue;

                        stFrameInfoEx2 = (MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2)Marshal.PtrToStructure(pstFrameInfoEx2, typeof(MvCodeReader.MV_CODEREADER_IMAGE_OUT_INFO_EX2));

                        if (stFrameInfoEx2.nFrameLen <= 0)
                            continue;

                        // 当帧内无条码识别结果时 pstCodeListEx2 可能为 IntPtr.Zero，
                        // 必须做空指针检查，否则 Marshal.PtrToStructure 会抛 ArgumentException
                        // 导致整个扫码线程终止。
                        if (stFrameInfoEx2.UnparsedBcrList.pstCodeListEx2 == IntPtr.Zero)
                            continue;

                        // 取出此帧中的条码识别结果结构
                        MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2 stBcrResultEx2 =
                            (MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2)Marshal.PtrToStructure(
                                stFrameInfoEx2.UnparsedBcrList.pstCodeListEx2,
                                typeof(MvCodeReader.MV_CODEREADER_RESULT_BCR_EX2));

                        // 遍历识别到的所有条码
                        for (int i = 0; i < stBcrResultEx2.nCodeNum; ++i)
                        {
                            String strCode = System.Text.Encoding.Default.GetString(stBcrResultEx2.stBcrInfoEx2[i].chCode);
                            strCode = strCode.TrimEnd('\0', '\r', '\n');

                            if (!string.IsNullOrEmpty(strCode))
                            {
                                OnBarcodeScanned?.Invoke(this, strCode);
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        // 单帧处理异常只记录日志，不中断扫码线程，继续下一帧
                        Debug.WriteLine($"[扫码] 单帧处理异常（已跳过）：{ex.Message}");
                    }
                }
            }
            finally
            {
                Marshal.FreeHGlobal(pstFrameInfoEx2);
            }
        }

        public void StopScanning()
        {
            if (!_isGrabbing) return;

            _isGrabbing = false;

            if (_device != null)
            {
                _device.MV_CODEREADER_StopGrabbing_NET();
            }

            if (_receiveThread != null && _receiveThread.IsAlive)
            {
                _receiveThread.Join(2000); 
            }
        }

        public void Disconnect()
        {
            StopScanning();
            
            if (_device != null)
            {
                _device.MV_CODEREADER_CloseDevice_NET();
                _device.MV_CODEREADER_DestroyHandle_NET();
            }
        }

        public void Dispose()
        {
            Disconnect();
        }
    }
}
