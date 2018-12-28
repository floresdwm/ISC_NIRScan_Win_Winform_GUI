using DLP_NIR_Win_SDK_CS;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Text;
using System.Windows.Forms;
using System.Reflection;
using System.Diagnostics;
using System.IO;
using System.Xml;
using LiveCharts;
using LiveCharts.Defaults;
using LiveCharts.Wpf;
using LiveCharts.Configurations;
using System.Threading;


namespace DLP_NIR_Win_SDK_WinForm_App_CS
{
    public partial class MainWindow : Form
    {
        // For Configuration
        private const Int32 MIN_WAVELENGTH = 900;
        private const Int32 MAX_WAVELENGTH = 1700;
        private const Int32 MAX_CFG_SECTION = 5;
        private bool IsOldTivaFW = false;

        private List<ScanConfig.SlewScanConfig> LocalConfig = new List<ScanConfig.SlewScanConfig>();
        private List<ComboBox> ComboBox_CfgScanType = new List<ComboBox>();
        private List<ComboBox> ComboBox_CfgWidth = new List<ComboBox>();
        private List<ComboBox> ComboBox_CfgExposure = new List<ComboBox>();
        private List<TextBox> TextBox_CfgRangeStart = new List<TextBox>();
        private List<TextBox> TextBox_CfgRangeEnd = new List<TextBox>();
        private List<TextBox> TextBox_CfgDigRes = new List<TextBox>();
        private List<Label> Label_CfgDigRes = new List<Label>();
       
        private Int32 TargetCfg_SelIndex = -1;      // Rocord device selected config
        Boolean NewConfig = false;                   // Record new config or existed config
        Boolean EditConfig = false;
        private Int32 DevCurCfg_Index = -1;         // Record current config which set to device

        private BackgroundWorker bwDLPCUpdate;
        private BackgroundWorker bwTivaUpdate;

        private String Display_Dir = String.Empty;
        private String Scan_Dir = String.Empty;
        private String ConfigDir = String.Empty;
        private Int32 ScanFile_Formats = 0;

       // Saved Scans
        List<Label> Label_SavedScanType = new List<Label>();
        List<Label> Label_SavedRangeStart = new List<Label>();
        List<Label> Label_SavedRangeEnd = new List<Label>();
        List<Label> Label_SavedWidth = new List<Label>();
        List<Label> Label_SavedDigRes = new List<Label>();
        List<Label> Label_SavedExposure = new List<Label>();

        public bool IsActivated { get { if (Device.GetActivationResult() == 1) return true; else return false; } }
        private ConfigurationData scan_Params;

        private BackgroundWorker bwRefScanProgress;
        private String Tiva_FWDir = String.Empty;
        private String DLPC_FWDir = String.Empty;

        // For Scan
        private DateTime TimeScanStart = new DateTime();
        private DateTime TimeScanEnd = new DateTime();
        private static UInt32 LampStableTime = 625;
        private Scan.SCAN_REF_TYPE ReferenceSelect = Scan.SCAN_REF_TYPE.SCAN_REF_NEW;
        private BackgroundWorker bwScan;
    

        public static class GlobalData
        {
            public static int RepeatedScanCountDown = 0;
            public static int ScannedCounts = 0;
            public static int TargetScanNumber = 0;
            public static bool UserCancelRepeatedScan = false;
        }

        public MainWindow()
        {
            InitializeComponent();
            this.FormClosing += Form1_FormClosing;
            CheckForIllegalCrossThreadCalls = false;//solve across thread is invalid
            MainWindow_Loaded();
            initBackgroundWorker();
            initChart();
            this.Text = string.Format("ISC NIRScan GUI SDK WinForm v{0}", Assembly.GetExecutingAssembly().GetName().Version.ToString().Substring(0, 5));
        }
        private void Form1_FormClosing(object sender, FormClosingEventArgs e)
        {
            // do something
            String HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;
            if ((HWRev != "B" && HWRev != String.Empty && Device.ChkBleExist() == 1) || HWRev == "B" || HWRev == String.Empty)
                Device.SetBluetooth(true);
        }
        private void initBackgroundWorker()
        {
            bwDLPCUpdate = new BackgroundWorker();
            bwTivaUpdate = new BackgroundWorker();
            bwDLPCUpdate.WorkerReportsProgress = true;
            bwTivaUpdate.WorkerReportsProgress = true;
            bwDLPCUpdate.WorkerSupportsCancellation = true;
            bwTivaUpdate.WorkerSupportsCancellation = true;
            bwDLPCUpdate.DoWork += new DoWorkEventHandler(bwDLPCUpdate_DoWork);
            bwTivaUpdate.DoWork += new DoWorkEventHandler(bwTivaUpdate_DoWork);
            bwDLPCUpdate.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwDLPCUpdate_DoWorkCompleted);
            bwTivaUpdate.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwTivaUpdate_DoSacnCompleted);
            bwDLPCUpdate.ProgressChanged += new ProgressChangedEventHandler(bwDLPCUpdate_ProgressChanged);
            bwTivaUpdate.ProgressChanged += new ProgressChangedEventHandler(bwTivaUpdate_ProgressChanged);

            bwScan = new BackgroundWorker
            {
                WorkerReportsProgress = false,
                WorkerSupportsCancellation = true
            };
            bwScan.DoWork += new DoWorkEventHandler(bwScan_DoScan);
            bwScan.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwScan_DoSacnCompleted);
        }
        private void MainWindow_Loaded()
        {
            //init GUI item
            toolStripStatus_DeviceStatus.Image = Properties.Resources.Led_Gray;
            RadioButton_RefNew.Checked = true;
            ReferenceSelect = Scan.SCAN_REF_TYPE.SCAN_REF_NEW;
            Button_Scan.Text = "Reference Scan";
            ComboBox_PGAGain.SelectedItem = "64";
            ComboBox_PGAGain.Enabled = false;
            CheckBox_AutoGain.Checked = true;

            Device.Init();
            InitSavedScanCfgItems();

            SDK.OnDeviceConnectionLost += new Action<bool>(Device_Disconncted_Handler);
            SDK.OnDeviceConnected += new Action<string>(Device_Connected_Handler);
            SDK.OnDeviceFound += new Action(Device_Found_Handler);
            SDK.OnDeviceError += new Action<string>(Device_Error_Handler);
            SDK.OnErrorStatusFound += new Action(RefreshErrorStatus);
            SDK.OnBeginConnectingDevice += new Action(Connecting_Device);
            SDK.OnBeginScan += new Action(BeginScan);
            SDK.OnScanCompleted += new Action(ScanCompleted);

            if (!Device.IsConnected())
            {
                SDK.AutoSearch = true;
            }
           
            LoadConfigDir();
            LoadScanPageSetting();
            TextBox_SaveDirPath.Text = Scan_Dir;
            TextBox_DisplayDirPath.Text = Display_Dir;
            //load save scan 
            LoadSavedScanList();          
        }
        #region Error 
        private void RefreshErrorStatus()
        {
            String ErrMsg = String.Empty;

            if (Device.ReadErrorStatusAndCode() != 0)
                return;

            if ((Device.ErrStatus & 0x00000001) == 0x00000001)  // Scan Error
            {
                if (Device.ErrCode[0] == 0x00000001)
                    ErrMsg += "Scan Error: DLPC150 boot error detected.    ";
                else if (Device.ErrCode[0] == 0x00000002)
                    ErrMsg += "Scan Error: DLPC150 init error detected.    ";
                else if (Device.ErrCode[0] == 0x00000004)
                    ErrMsg += "Scan Error: DLPC150 lamp driver error detected.    ";
                else if (Device.ErrCode[0] == 0x00000008)
                    ErrMsg += "Scan Error: DLPC150 crop image failed.    ";
                else if (Device.ErrCode[0] == 0x00000010)
                    ErrMsg += "Scan Error: ADC data error.    ";
                else if (Device.ErrCode[0] == 0x00000020)
                    ErrMsg += "Scan Error: Scan config Invalid.    ";
                else if (Device.ErrCode[0] == 0x00000040)
                    ErrMsg += "Scan Error: Scan pattern streaming error.    ";
                else if (Device.ErrCode[0] == 0x00000080)
                    ErrMsg += "Scan Error: DLPC150 read error.    ";
            }

            if ((Device.ErrStatus & 0x00000002) == 0x00000002)  // ADC Error
            {
                if (Device.ErrCode[1] == 0x00000001)
                    ErrMsg += "ADC Error: ADC timeout error.    ";
                else if (Device.ErrCode[1] == 0x00000002)
                    ErrMsg += "ADC Error: ADC PowerDown error.    ";
                else if (Device.ErrCode[1] == 0x00000003)
                    ErrMsg += "ADC Error: ADC PowerUp error.    ";
                else if (Device.ErrCode[1] == 0x00000004)
                    ErrMsg += "ADC Error: ADC StandBy error.    ";
                else if (Device.ErrCode[1] == 0x00000005)
                    ErrMsg += "ADC Error: ADC WAKEUP error.    ";
                else if (Device.ErrCode[1] == 0x00000006)
                    ErrMsg += "ADC Error: ADC read register error.    ";
                else if (Device.ErrCode[1] == 0x00000007)
                    ErrMsg += "ADC Error: ADC write register error.    ";
                else if (Device.ErrCode[1] == 0x00000008)
                    ErrMsg += "ADC Error: ADC configure error.    ";
                else if (Device.ErrCode[1] == 0x00000009)
                    ErrMsg += "ADC Error: ADC set buffer error.    ";
                else if (Device.ErrCode[1] == 0x0000000A)
                    ErrMsg += "ADC Error: ADC command error.    ";
            }

            if ((Device.ErrStatus & 0x00000004) == 0x00000004)  // SD Card Error
            {
                ErrMsg += "SD Card Error.    ";
            }

            if ((Device.ErrStatus & 0x00000008) == 0x00000008)  // EEPROM Error
            {
                ErrMsg += "EEPROM Error.    ";
            }

            if ((Device.ErrStatus & 0x00000010) == 0x00000010)  // BLE Error
            {
                ErrMsg += "Bluetooth Error.    ";
            }

            if ((Device.ErrStatus & 0x00000020) == 0x00000020)  // Spectrum Library Error
            {
                ErrMsg += "Spectrum Library Error.    ";
            }

            if ((Device.ErrStatus & 0x00000040) == 0x00000040)  // Hardware Error
            {
                if (Device.ErrCode[6] == 0x00000001)
                    ErrMsg += "HW Error: DLPC150 Error.    ";
            }

            if ((Device.ErrStatus & 0x00000080) == 0x00000080)  // TMP Sensor Error
            {
                if (Device.ErrCode[7] == 0x00000001)
                    ErrMsg += "TMP Error: Invalid manufacturing id.    ";
                else if (Device.ErrCode[7] == 0x00000002)
                    ErrMsg += "TMP Error: Invalid device id.    ";
                else if (Device.ErrCode[7] == 0x00000003)
                    ErrMsg += "TMP Error: Reset error.    ";
                else if (Device.ErrCode[7] == 0x00000004)
                    ErrMsg += "TMP Error: Read register error.    ";
                else if (Device.ErrCode[7] == 0x00000005)
                    ErrMsg += "TMP Error: Write register error.    ";
                else if (Device.ErrCode[7] == 0x00000006)
                    ErrMsg += "TMP Error: Timeout error.    ";
                else if (Device.ErrCode[7] == 0x00000007)
                    ErrMsg += "TMP Error: I2C error.    ";
            }

            if ((Device.ErrStatus & 0x00000100) == 0x00000100)  // HDC1000 Sensor Error
            {
                if (Device.ErrCode[8] == 0x00000001)
                    ErrMsg += "HDC1000 Error: Invalid manufacturing id.    ";
                else if (Device.ErrCode[8] == 0x00000002)
                    ErrMsg += "HDC1000 Error: Invalid device id.    ";
                else if (Device.ErrCode[8] == 0x00000003)
                    ErrMsg += "HDC1000 Error: Reset error.    ";
                else if (Device.ErrCode[8] == 0x00000004)
                    ErrMsg += "HDC1000 Error: Read register error.    ";
                else if (Device.ErrCode[8] == 0x00000005)
                    ErrMsg += "HDC1000 Error: Write register error.    ";
                else if (Device.ErrCode[8] == 0x00000006)
                    ErrMsg += "HDC1000 Error: Timeout error.    ";
                else if (Device.ErrCode[8] == 0x00000007)
                    ErrMsg += "HDC1000 Error: I2C error.    ";
            }

            if ((Device.ErrStatus & 0x00000200) == 0x00000200)  // Battery Error
            {
                if (Device.ErrCode[9] == 0x00000001)
                    ErrMsg += "Battery Error: Battery low.    ";
            }

            if ((Device.ErrStatus & 0x00000400) == 0x00000400)  // Insufficient Memory Error
            {
                ErrMsg += "Not enough memory.    ";
            }

            if ((Device.ErrStatus & 0x00000800) == 0x00000800)  // UART Error
            {
                ErrMsg += "UART error.    ";
            }

            label_ErrorStatus.Text = ErrMsg;
        }

        private void Device_Error_Handler(string error)
        {
            tivaVerChk();
            if(IsOldTivaFW)
            {
              
            }
            else
            {
                ShowWarning(error);
            }
            
        }
        private void closeItem()
        {
            splitContainer1.Enabled = false;
            GroupBox_ModelName.Enabled = false;
            GroupBox_SerialNumber.Enabled = false;
            GroupBox_DateTime.Enabled = false;
            GroupBox_Sensors.Enabled = false;
            GroupBox_DLPC150FWUpdate.Enabled = false;
            GroupBox_CalibCoeffs.Enabled = false;
            groupBox_Device.Enabled = false;
        }

        private void openItem()
        {
            splitContainer1.Enabled = true;
            GroupBox_ModelName.Enabled = true;
            GroupBox_SerialNumber.Enabled = true;
            GroupBox_DateTime.Enabled = true;
            GroupBox_Sensors.Enabled = true;
            GroupBox_DLPC150FWUpdate.Enabled = true;
            GroupBox_CalibCoeffs.Enabled = true;
            groupBox_Device.Enabled = true;
        }

        public  void ShowWarning(String Text)
        {
            String text = Text;
            MessageBox.Show(text, "Warning");
        }
        #endregion

        #region connect device
        private void Device_Disconncted_Handler(bool error)
        {
            toolStripStatus_DeviceStatus.Image = Properties.Resources.Led_R;
            toolStripStatus_DeviceStatus.Text = "Device disconnect!";
            if (error)
            {
                DBG.WriteLine("Device disconnected abnormally !");
                SDK.AutoSearch = true;
            }
            else
                DBG.WriteLine("Device disconnected successfully !");
        }
        private void Device_Found_Handler()
        {
            SDK.AutoSearch = false;
            Enumerate_Devices();
        }
        private void Enumerate_Devices()
        {
            Device.Enumerate();
            Device.Open(null);
        }
        private void Connecting_Device()
        {
          
        }
        private void Device_Connected_Handler(String SerialNumber)
        {
            if (SerialNumber == null)
                DBG.WriteLine("Device connecting failed !");
            else
            {
                DBG.WriteLine("Device <{0}> connected successfullly !", SerialNumber);               
            }
            String HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;
            if ((HWRev == "D" && Device.ChkBleExist() == 1) || HWRev == "B" || HWRev == String.Empty)
                Device.SetBluetooth(false);
            tivaVerChk();
            if (IsOldTivaFW)
            {
                ShowWarning("The version is too old.\nPlease updte your TIVA FW.");
                closeItem();
            }
            else
            {
                openItem();
            }
            toolStripStatus_DeviceStatus.Image = Properties.Resources.Led_G;
            toolStripStatus_DeviceStatus.Text = "Device connected!";
            RefreshErrorStatus();
            GetDeviceInfo();
            // Scan Config
            PopulateCfgDetailItems();
            RefreshTargetCfgList();  // Only refresh UI because target config list has been loaded after device opened
            GetActivationKeyStatus();
            DisableCfgItem();

            //utility item
            Button_CalWriteCoeffs.Enabled = false;
            Button_CalWriteGenCoeffs.Enabled = false;
            Button_CalRestoreDefaultCoeffs.Enabled = false;

            tivaVerChk();
            if(IsOldTivaFW)
            {
                groupBox_ActivationKey.Enabled = false;
            }

            // Scan Plot Area
            Int32 ActiveIndex = ScanConfig.GetTargetActiveScanIndex();
            if (ActiveIndex >= 0)
            {
                SetScanConfig(ScanConfig.TargetConfig[ActiveIndex], ActiveIndex,true);
            }
            // Scan Setting
            scan_Params.ScanAvg = ScanConfig.TargetConfig[ActiveIndex].head.num_repeats;
            scan_Params.PGAGain = 64;
            scan_Params.RepeatedScanCountDown = 0;
            scan_Params.ScanInterval = 0;
            scan_Params.scanedCounts = 0;
            RadioButton_RefNew.Checked = true;
            TextBox_LampStableTime.Text = LampStableTime.ToString();
            CheckBox_AutoGain.Checked = true;
            CheckBox_AutoGain.Enabled = true;
            ComboBox_PGAGain.Enabled = false;
        }
        #endregion

        #region init
        private void InitSavedScanCfgItems()
        {
            Label_SavedScanType.Clear();
            Label_SavedScanType.Add(Label_SavedScanType1);
            Label_SavedScanType.Add(Label_SavedScanType2);
            Label_SavedScanType.Add(Label_SavedScanType3);
            Label_SavedScanType.Add(Label_SavedScanType4);
            Label_SavedScanType.Add(Label_SavedScanType5);
            Label_SavedRangeStart.Clear();
            Label_SavedRangeStart.Add(Label_SavedRangeStart1);
            Label_SavedRangeStart.Add(Label_SavedRangeStart2);
            Label_SavedRangeStart.Add(Label_SavedRangeStart3);
            Label_SavedRangeStart.Add(Label_SavedRangeStart4);
            Label_SavedRangeStart.Add(Label_SavedRangeStart5);
            Label_SavedRangeEnd.Clear();
            Label_SavedRangeEnd.Add(Label_SavedRangeEnd1);
            Label_SavedRangeEnd.Add(Label_SavedRangeEnd2);
            Label_SavedRangeEnd.Add(Label_SavedRangeEnd3);
            Label_SavedRangeEnd.Add(Label_SavedRangeEnd4);
            Label_SavedRangeEnd.Add(Label_SavedRangeEnd5);
            Label_SavedWidth.Clear();
            Label_SavedWidth.Add(Label_SavedWidth1);
            Label_SavedWidth.Add(Label_SavedWidth2);
            Label_SavedWidth.Add(Label_SavedWidth3);
            Label_SavedWidth.Add(Label_SavedWidth4);
            Label_SavedWidth.Add(Label_SavedWidth5);
            Label_SavedDigRes.Clear();
            Label_SavedDigRes.Add(Label_SavedDigRes1);
            Label_SavedDigRes.Add(Label_SavedDigRes2);
            Label_SavedDigRes.Add(Label_SavedDigRes3);
            Label_SavedDigRes.Add(Label_SavedDigRes4);
            Label_SavedDigRes.Add(Label_SavedDigRes5);
            Label_SavedExposure.Clear();
            Label_SavedExposure.Add(Label_SavedExposure1);
            Label_SavedExposure.Add(Label_SavedExposure2);
            Label_SavedExposure.Add(Label_SavedExposure3);
            Label_SavedExposure.Add(Label_SavedExposure4);
            Label_SavedExposure.Add(Label_SavedExposure5);

            ClearSavedScanCfgItems();
        }

        private void PopulateCfgDetailItems()
        {
            ComboBox_CfgScanType.Clear();
            ComboBox_CfgScanType.Add(ComboBox_CfgScanType1);
            ComboBox_CfgScanType.Add(ComboBox_CfgScanType2);
            ComboBox_CfgScanType.Add(ComboBox_CfgScanType3);
            ComboBox_CfgScanType.Add(ComboBox_CfgScanType4);
            ComboBox_CfgScanType.Add(ComboBox_CfgScanType5);
            ComboBox_CfgWidth.Clear();
            ComboBox_CfgWidth.Add(ComboBox_CfgWidth1);
            ComboBox_CfgWidth.Add(ComboBox_CfgWidth2);
            ComboBox_CfgWidth.Add(ComboBox_CfgWidth3);
            ComboBox_CfgWidth.Add(ComboBox_CfgWidth4);
            ComboBox_CfgWidth.Add(ComboBox_CfgWidth5);
            ComboBox_CfgExposure.Clear();
            ComboBox_CfgExposure.Add(ComboBox_CfgExposure1);
            ComboBox_CfgExposure.Add(ComboBox_CfgExposure2);
            ComboBox_CfgExposure.Add(ComboBox_CfgExposure3);
            ComboBox_CfgExposure.Add(ComboBox_CfgExposure4);
            ComboBox_CfgExposure.Add(ComboBox_CfgExposure5);
            TextBox_CfgRangeStart.Clear();
            TextBox_CfgRangeStart.Add(TextBox_CfgRangeStart1);
            TextBox_CfgRangeStart.Add(TextBox_CfgRangeStart2);
            TextBox_CfgRangeStart.Add(TextBox_CfgRangeStart3);
            TextBox_CfgRangeStart.Add(TextBox_CfgRangeStart4);
            TextBox_CfgRangeStart.Add(TextBox_CfgRangeStart5);
            TextBox_CfgRangeEnd.Clear();
            TextBox_CfgRangeEnd.Add(TextBox_CfgRangeEnd1);
            TextBox_CfgRangeEnd.Add(TextBox_CfgRangeEnd2);
            TextBox_CfgRangeEnd.Add(TextBox_CfgRangeEnd3);
            TextBox_CfgRangeEnd.Add(TextBox_CfgRangeEnd4);
            TextBox_CfgRangeEnd.Add(TextBox_CfgRangeEnd5);
            TextBox_CfgDigRes.Clear();
            TextBox_CfgDigRes.Add(TextBox_CfgDigRes1);
            TextBox_CfgDigRes.Add(TextBox_CfgDigRes2);
            TextBox_CfgDigRes.Add(TextBox_CfgDigRes3);
            TextBox_CfgDigRes.Add(TextBox_CfgDigRes4);
            TextBox_CfgDigRes.Add(TextBox_CfgDigRes5);
            Label_CfgDigRes.Clear();
            Label_CfgDigRes.Add(Label_CfgDigRes1);
            Label_CfgDigRes.Add(Label_CfgDigRes2);
            Label_CfgDigRes.Add(Label_CfgDigRes3);
            Label_CfgDigRes.Add(Label_CfgDigRes4);
            Label_CfgDigRes.Add(Label_CfgDigRes5);


            for (Int32 i = 0; i < MAX_CFG_SECTION; i++)
            {
                // Initialize combobox items
                for (Int32 j = 0; j < 2; j++)
                {
                    String Type = Helper.ScanTypeIndexToMode(j).Substring(0, 3);
                    ComboBox_CfgScanType[i].Items.Add(Type);
                }
                for (Int32 j = 0; j < Helper.CfgWidthItemsCount(); j++)
                {
                    Double WidthNM = Helper.CfgWidthIndexToNM(j);
                    ComboBox_CfgWidth[i].Items.Add(Math.Round(WidthNM, 2));
                }
                for (Int32 j = 0; j < Helper.CfgExpItemsCount(); j++)
                {
                    Double ExpTime = Helper.CfgExpIndexToTime(j);
                    ComboBox_CfgExposure[i].Items.Add(ExpTime);
                }
            }
        }


        private void DisableCfgItem()
        {
            TextBox_CfgName.Enabled = false;
            TextBox_CfgAvg.Enabled = false;
            comboBox_cfgNumSec.Enabled = false;
            for (int i = 0; i < 5; i++)
            {
                ComboBox_CfgScanType[i].Enabled = false;
                TextBox_CfgRangeStart[i].Enabled = false;
                TextBox_CfgRangeEnd[i].Enabled = false;
                ComboBox_CfgWidth[i].Enabled = false;
                TextBox_CfgDigRes[i].Enabled = false;
                ComboBox_CfgExposure[i].Enabled = false;
            }
        }

        private void EnableCfgItem()
        {
            TextBox_CfgName.Enabled = true;
            TextBox_CfgAvg.Enabled = true;
            comboBox_cfgNumSec.Enabled = true;
            for (int i = 0; i < 5; i++)
            {
                ComboBox_CfgScanType[i].Enabled = true;
                TextBox_CfgRangeStart[i].Enabled = true;
                TextBox_CfgRangeEnd[i].Enabled = true;
                ComboBox_CfgWidth[i].Enabled = true;
                TextBox_CfgDigRes[i].Enabled = true;
                ComboBox_CfgExposure[i].Enabled = true;
            }
        }

        private void InitCfgDetailsContent()
        {
            TextBox_CfgName.Clear();
            TextBox_CfgAvg.Clear();
            comboBox_cfgNumSec.SelectedIndex = 0;

            for (Int32 i = 0; i < MAX_CFG_SECTION; i++)
            {
                ComboBox_CfgScanType[i].SelectedIndex = 0;
                TextBox_CfgRangeStart[i].Clear();
                TextBox_CfgRangeEnd[i].Clear();
                ComboBox_CfgWidth[i].SelectedIndex = 5;
                ComboBox_CfgExposure[i].SelectedIndex = 0;
                TextBox_CfgDigRes[i].Clear();
                Label_CfgDigRes[i].Text = String.Empty;
            }
        }
        private void LoadConfigDir()
        {
            // Config Directory
            String path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
            ConfigDir = Path.Combine(path, "InnoSpectra\\Config Data");

            if (Directory.Exists(ConfigDir) == false)
            {
                Directory.CreateDirectory(ConfigDir);
                DBG.WriteLine("The directory {0} was created.", ConfigDir);
            }
        }
        private void LoadScanPageSetting()
        {
            String FilePath = Path.Combine(ConfigDir, "ScanPageSettings.xml");
            if (!File.Exists(FilePath))
            {
                String path = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                Scan_Dir = Path.Combine(path, "InnoSpectra\\Scan Results");
                Display_Dir = Scan_Dir;
                ScanFile_Formats = 0x81;

                if (Directory.Exists(Scan_Dir) == false)
                {
                    Directory.CreateDirectory(Scan_Dir);
                    DBG.WriteLine("The directory {0} was created.", Scan_Dir);
                }
            }
            else
            {
                XmlDocument XmlDoc = new XmlDocument();
                XmlDoc.Load(FilePath);

                XmlNode ScanDir = XmlDoc.SelectSingleNode("/Settings/ScanDir");
                if (ScanDir.InnerText == String.Empty)
                    Scan_Dir = Path.Combine(Directory.GetCurrentDirectory(), "Scan Results");
                else
                    Scan_Dir = ScanDir.InnerText;

                XmlNode DisplayDir = XmlDoc.SelectSingleNode("/Settings/DisplayDir");
                if (DisplayDir.InnerText == String.Empty)
                    Display_Dir = Scan_Dir;
                else
                    Display_Dir = DisplayDir.InnerText;

                XmlNode FileFormats = XmlDoc.SelectSingleNode("/Settings/FileFormats");
                if (FileFormats.InnerText == String.Empty)
                    ScanFile_Formats = 0x81;
                else
                    ScanFile_Formats = Int32.Parse(FileFormats.InnerText);
            }

            CheckBox_SaveCombCSV.Checked = ((ScanFile_Formats & 0x01) >> 0 == 1) ? true : false;
            CheckBox_SaveICSV.Checked = ((ScanFile_Formats & 0x02) >> 1 == 1) ? true : false;
            CheckBox_SaveACSV.Checked = ((ScanFile_Formats & 0x04) >> 2 == 1) ? true : false;
            CheckBox_SaveRCSV.Checked = ((ScanFile_Formats & 0x08) >> 3 == 1) ? true : false;
            CheckBox_SaveDAT.Checked = ((ScanFile_Formats & 0x80) >> 7 == 1) ? true : false;
        }
        #endregion

        #region Scan
        private void BeginScan()
        {
            /*try { ProgressWindowCompleted(); } catch { }
            ProgressWindowStart("Start scan", false);*/
            /* String text = "Start Scan!";
             pbws = new ProgressBar(text) { Owner = this };
             pbws.ShowDialog();*/
        }
        private void ScanCompleted()
        {
            //  ProgressWindowCompleted();
            //pbws.Close();
        }
        private void RadioButton_RefNew_CheckedChanged(object sender, EventArgs e)
        {
            if(RadioButton_RefNew.Checked == true)
            {
                Button_Scan.Text = "Reference Scan";
                ReferenceSelect = Scan.SCAN_REF_TYPE.SCAN_REF_NEW;
            }
        }


        private void RadioButton_RefPre_CheckedChanged(object sender, EventArgs e)
        {
            if (RadioButton_RefPre.Checked == true)
            {
                Button_Scan.Text = "Scan";
                ReferenceSelect = Scan.SCAN_REF_TYPE.SCAN_REF_PREV;
            }
        }


        private void RadioButton_RefFac_CheckedChanged(object sender, EventArgs e)
        {
            if (RadioButton_RefFac.Checked == true)
            {
                Button_Scan.Text = "Scan";
                ReferenceSelect = Scan.SCAN_REF_TYPE.SCAN_REF_BUILT_IN;
            }
        }

        #endregion
        #region Scan Config
        private void Button_SetActive_Click(object sender, EventArgs e)
        {
            if (TargetCfg_SelIndex < 0)
            {
                String text = "No item selected.";
                MessageBox.Show(text, "Warning");
                return;
            }
            ScanConfig.SetTargetActiveScanIndex(TargetCfg_SelIndex);
            SetScanConfig(ScanConfig.TargetConfig[TargetCfg_SelIndex], TargetCfg_SelIndex,true);
            RefreshTargetCfgList();
        }
        private void SetScanConfig(ScanConfig.SlewScanConfig Config, Int32 index, Boolean setActive)
        {
            if (ScanConfig.SetScanConfig(Config) == SDK.FAIL)
            {
                String text = "Device config (" + Config.head.config_name + ") is not correct, please check it again!";
                MessageBox.Show(text, "Error");
            }
            else
            {
                DevCurCfg_Index = index;

                Label_CurrentConfig.Text =  Config.head.config_name;
                if(setActive)
                {
                    label_ActiveConfig.Text = Config.head.config_name;
                }
                textBox_ScanAvg.Text = Config.head.num_repeats.ToString();
                Double ScanTime = Scan.GetEstimatedScanTime();
                if (ScanTime > 0)
                    Label_EstimatedScanTime.Text = "Est. Device Scan Time: " + String.Format("{0:0.000}", ScanTime) + " secs.";

                Button_Scan.Enabled = true;

            }
        }
        private void ListBox_DeviceScanConfig_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (NewConfig == true || EditConfig == true)
            {
                EditConfig = false;
                NewConfig = false;
                Button_CfgCancel_Click(this, e);
            }
            TargetCfg_SelIndex = ListBox_DeviceScanConfig.SelectedIndex;
            if (TargetCfg_SelIndex < 0 || ScanConfig.TargetConfig.Count == 0)
                return;
            FillCfgDetailsContent();
        }
        private void ListBox_DeviceScanConfig_MouseDoubleClick(object sender, EventArgs e)
        {
            SetScanConfig(ScanConfig.TargetConfig[TargetCfg_SelIndex], TargetCfg_SelIndex, false);
            TargetCfg_SelIndex = ListBox_DeviceScanConfig.SelectedIndex;    
        }

        private void FillCfgDetailsContent()
        {
            Int32 i, NumSection = 0;
            ScanConfig.SlewScanConfig CurConfig = new ScanConfig.SlewScanConfig
            {
                section = new ScanConfig.SlewScanSection[5]
            };

            InitCfgDetailsContent();

            CurConfig = ScanConfig.TargetConfig[TargetCfg_SelIndex];

            NumSection = CurConfig.head.num_sections;

            TextBox_CfgName.Text = CurConfig.head.config_name;
            TextBox_CfgAvg.Text = CurConfig.head.num_repeats.ToString();
            comboBox_cfgNumSec.SelectedItem = NumSection.ToString();

            for (i = 0; i < NumSection; i++)
            {
                ComboBox_CfgScanType[i].SelectedIndex = CurConfig.section[i].section_scan_type;
                TextBox_CfgRangeStart[i].Text = CurConfig.section[i].wavelength_start_nm.ToString();
                TextBox_CfgRangeEnd[i].Text = CurConfig.section[i].wavelength_end_nm.ToString();
                ComboBox_CfgWidth[i].SelectedIndex = (Helper.CfgWidthPixelToIndex(CurConfig.section[i].width_px) > -1) ? (Helper.CfgWidthPixelToIndex(CurConfig.section[i].width_px)) : (5);
                TextBox_CfgDigRes[i].Text = CurConfig.section[i].num_patterns.ToString();
                ComboBox_CfgExposure[i].SelectedIndex = CurConfig.section[i].exposure_time;
            }
            DisableCfgItem();
        }
        private void RefreshTargetCfgList()
        {
            TargetCfg_SelIndex = -1;
            Int32 ActiveIndex = ScanConfig.GetTargetActiveScanIndex();

            ListBox_DeviceScanConfig.Items.Clear();
            if (ScanConfig.TargetConfig.Count > 0)
            {
                for (Int32 i = 0; i < ScanConfig.TargetConfig.Count; i++)
                {
                    ListBox_DeviceScanConfig.Items.Add(ScanConfig.TargetConfig[i].head.config_name);
                }
                label_ActiveConfig.Text = ScanConfig.TargetConfig[ActiveIndex].head.config_name;
            }
        }

        private void Button_CfgNew_Click(object sender, EventArgs e)
        {
            if (ScanConfig.TargetConfig.Count < 20)
            {
                ListBox_DeviceScanConfig.ClearSelected();
                InitCfgDetailsContent();
                EnableCfgItem();
                NewConfig = true;
                comboBox_cfgNumSec.SelectedItem = "1";
                Button_CfgEdit.Enabled = false;
                Button_CfgDelete.Enabled = false;
            }
            else
            {
                String text = "Configuration counts exceeds 20, can't add anymore!";
                MessageBox.Show(text, "Error");
            }

        }

        private void Button_CfgCancel_Click(object sender, EventArgs e)
        {
            int bufindex = TargetCfg_SelIndex;
            InitCfgDetailsContent();
            DisableCfgItem();
           
            if (EditConfig == true)
            {
                ListBox_DeviceScanConfig.ClearSelected();
                ListBox_DeviceScanConfig.SelectedIndex = bufindex;
            }

            Button_CfgEdit.Enabled = true;
            Button_CfgDelete.Enabled = true;
            Button_CfgNew.Enabled = true;
            NewConfig = false;
            EditConfig = false;
        }

        private void Button_CfgSave_Click(object sender, EventArgs e)
        {
            DisableCfgItem();
            if (IsCfgLegal() == SDK.FAIL)
            {
                String text = "Error configuration data can't be saved!";
                MessageBox.Show(text, "Error");
                EnableCfgItem();
                return;
            }
            if(NewConfig == true)
            {
                SaveCfgToList(true);
                NewConfig = false;
            }
            else if(EditConfig == true)
            {
                SaveCfgToList(false);
                EditConfig = false;
            }
            Button_CfgEdit.Enabled = true;
            Button_CfgDelete.Enabled = true;
            Button_CfgNew.Enabled = true;
        }

        private Int32 SaveCfgToList( Boolean IsNew)
        {
            ScanConfig.SlewScanConfig CurConfig = new ScanConfig.SlewScanConfig
            {
                section = new ScanConfig.SlewScanSection[5]
            };

            CurConfig.head.config_name = Helper.CheckRegex(TextBox_CfgName.Text);
            CurConfig.head.scan_type = 2;
            CurConfig.head.num_sections = Byte.Parse(comboBox_cfgNumSec.SelectedItem.ToString());
            CurConfig.head.num_repeats = UInt16.Parse(TextBox_CfgAvg.Text);

            for (Int32 i = 0; i < CurConfig.head.num_sections; i++)
            {
                CurConfig.section[i].wavelength_start_nm = UInt16.Parse(TextBox_CfgRangeStart[i].Text);
                CurConfig.section[i].wavelength_end_nm = UInt16.Parse(TextBox_CfgRangeEnd[i].Text);
                CurConfig.section[i].num_patterns = UInt16.Parse(TextBox_CfgDigRes[i].Text);
                CurConfig.section[i].section_scan_type = (Byte)(ComboBox_CfgScanType[i].SelectedIndex);
                CurConfig.section[i].width_px = (Byte)Helper.CfgWidthIndexToPixel(ComboBox_CfgWidth[i].SelectedIndex);
                CurConfig.section[i].exposure_time = (UInt16)ComboBox_CfgExposure[i].SelectedIndex;
            }

            if ( IsNew == true)
            {
                ScanConfig.TargetConfig.Add(CurConfig);
            }
            else
            {
                ScanConfig.TargetConfig.RemoveAt(TargetCfg_SelIndex);
                ScanConfig.TargetConfig.Insert(TargetCfg_SelIndex, CurConfig);
            }

            RefreshTargetCfgList();

            return SaveCfgToLocalOrDevice();
        }

        private Int32 SaveCfgToLocalOrDevice()
        {
            Int32 ret = SDK.FAIL;
            if ((ret = ScanConfig.SetConfigList()) == 0)
            {
                String text = "Device Config List Update Success!";
                MessageBox.Show(text, "Success");
            }            
            else
            {
                String text = "Device Config List Update Failed!";
                MessageBox.Show(text, "Warning");
            }
             
            return ret;
        }

        private void Button_CfgDelete_Click(object sender, EventArgs e)
        {
            if (ScanConfig.TargetConfig.Count > 1)
            {
                Int32 ActiveIndex = ScanConfig.GetTargetActiveScanIndex();
                ScanConfig.TargetConfig.RemoveAt(TargetCfg_SelIndex);
                if (TargetCfg_SelIndex == ActiveIndex)
                {
                    ActiveIndex = 0;
                    ScanConfig.SetTargetActiveScanIndex(ActiveIndex);
                }
                else if (TargetCfg_SelIndex < ActiveIndex)
                {
                    ActiveIndex--;
                    ScanConfig.SetTargetActiveScanIndex(ActiveIndex);
                }
                if (TargetCfg_SelIndex == DevCurCfg_Index)
                {
                    SetScanConfig(ScanConfig.TargetConfig[ActiveIndex], ActiveIndex, false);
                }
                RefreshTargetCfgList();
                SaveCfgToLocalOrDevice();
                DBG.WriteLine("Delete this Device configuration");
            }
            else
            {
                String text = "At least one configuration needed in the device!";
                MessageBox.Show(text, "Error");
            }
        }

        private void Button_CfgEdit_Click(object sender, EventArgs e)
        {
            if (TargetCfg_SelIndex < 0)
            {
                String text = "No item selected.";
                MessageBox.Show(text, "Warning");
                return;
            }
            EnableCfgItem();
            NewConfig = false;
            EditConfig = true;
            Button_CfgNew.Enabled = false;
            Button_CfgDelete.Enabled = false;
        }

        private void CfgDetails_TextChanged(object sender, EventArgs e)
        {
            IsCfgLegal();
        }
        private void CfgDetails_SelectionChanged(object sender, EventArgs e)
        {
            IsCfgLegal();
        }

        private void CfgSection_SelectionChanged(object sender, EventArgs e)
        {
            GetSectionItem();
        }

        private void GetSectionItem()
        {
            int section = int.Parse(comboBox_cfgNumSec.SelectedItem.ToString());
            for(int i=0;i<5;i++)
            {
                if(i < section)
                {
                    ComboBox_CfgScanType[i].Visible = true;
                    TextBox_CfgRangeStart[i].Visible = true;
                    TextBox_CfgRangeEnd[i].Visible = true;
                    ComboBox_CfgWidth[i].Visible = true;
                    TextBox_CfgDigRes[i].Visible = true;
                    ComboBox_CfgExposure[i].Visible = true;
                    Label_CfgDigRes[i].Visible = true;
                }
                else
                {
                    ComboBox_CfgScanType[i].Visible = false;
                    TextBox_CfgRangeStart[i].Visible = false;
                    TextBox_CfgRangeEnd[i].Visible = false;
                    ComboBox_CfgWidth[i].Visible = false;
                    TextBox_CfgDigRes[i].Visible = false;
                    ComboBox_CfgExposure[i].Visible = false;
                    Label_CfgDigRes[i].Visible = false;
                }
            }
        }

        private Int32 IsCfgLegal()
        {
            Int32 ret = SDK.PASS;
            Int32 TotalPatterns = 0;
            ScanConfig.SlewScanConfig CurConfig = new ScanConfig.SlewScanConfig
            {
                section = new ScanConfig.SlewScanSection[5]
            };
            CurConfig.head.scan_type = 2;

            // Config Name
            if (TextBox_CfgName.Text == String.Empty)
            {
                ret = SDK.FAIL;
            }
            else
            {
                CurConfig.head.config_name = Helper.CheckRegex(TextBox_CfgName.Text);
            }

            // Num Scans to Average
            if (UInt16.TryParse(TextBox_CfgAvg.Text, out CurConfig.head.num_repeats) == false || CurConfig.head.num_repeats == 0)
            {
                ret = SDK.FAIL;
            }
            CurConfig.head.num_sections = byte.Parse(comboBox_cfgNumSec.SelectedItem.ToString());
            for (Byte i = 0; i < CurConfig.head.num_sections; i++)
            {
                CurConfig.section[i].section_scan_type = (Byte)(ComboBox_CfgScanType[i].SelectedIndex);
                CurConfig.section[i].width_px = (Byte)Helper.CfgWidthIndexToPixel(ComboBox_CfgWidth[i].SelectedIndex);
                CurConfig.section[i].exposure_time = (UInt16)ComboBox_CfgExposure[i].SelectedIndex;

                // Start nm
                if (UInt16.TryParse(TextBox_CfgRangeStart[i].Text, out CurConfig.section[i].wavelength_start_nm) == false ||
                    CurConfig.section[i].wavelength_start_nm < MIN_WAVELENGTH)
                {
                    ret = SDK.FAIL;
                }

                // End nm
                if (UInt16.TryParse(TextBox_CfgRangeEnd[i].Text, out CurConfig.section[i].wavelength_end_nm) == false ||
                    CurConfig.section[i].wavelength_end_nm > MAX_WAVELENGTH)
                {
                    ret = SDK.FAIL;
                }

                if (CurConfig.section[i].wavelength_start_nm >= CurConfig.section[i].wavelength_end_nm)
                {
                    ret = SDK.FAIL;
                }

                // Check Max Patterns
                Int32 MaxPattern = ScanConfig.GetMaxPatterns(CurConfig, i);
                if ((UInt16.TryParse(TextBox_CfgDigRes[i].Text, out CurConfig.section[i].num_patterns) == false) ||
                    (CurConfig.section[i].section_scan_type == 0 && CurConfig.section[i].num_patterns < 2) ||  // Column Mode
                    (CurConfig.section[i].section_scan_type == 1 && CurConfig.section[i].num_patterns < 3) ||  // Hadamard Mode
                    (CurConfig.section[i].num_patterns > MaxPattern) ||
                    (MaxPattern <= 0))
                {
                    if (MaxPattern < 0) MaxPattern = 0;
                    ret = SDK.FAIL;
                }

                Label_CfgDigRes[i].Text = CurConfig.section[i].num_patterns.ToString() + "/" + MaxPattern.ToString();
                TotalPatterns += CurConfig.section[i].num_patterns;
            }

            Label_CfgNumPatterns.Text = "Total Ptn. Used: " + TotalPatterns.ToString() + "/624";
            if (TotalPatterns > 624)
            {
                String text = "Total number of patterns " + TotalPatterns.ToString() + " exceeds 624!";
                MessageBox.Show(text, "Warning");
                ret = SDK.FAIL;
            }

            return ret;
        }
        #endregion
        #region Save scan
        private void LoadSavedScanList()
        {
            List<String> ListFiles = EnumerateFiles("*.dat");
            listBox_SavedData.Items.Clear();
            foreach (String FileName in ListFiles)
            {
                listBox_SavedData.Items.Add(FileName);
            }
        }

        private List<String> EnumerateFiles(String SearchPattern)
        {
            List<String> ListFiles = new List<String>();

            try
            {
                String DirPath = TextBox_DisplayDirPath.Text;

                foreach (String Files in Directory.EnumerateFiles(DirPath, SearchPattern))
                {
                    String FileName = Files.Substring(Files.LastIndexOf("\\") + 1);
                    ListFiles.Add(FileName);
                }
            }
            catch (UnauthorizedAccessException UAEx) { DBG.WriteLine(UAEx.Message); }
            catch (PathTooLongException PathEx) { DBG.WriteLine(PathEx.Message); }

            return ListFiles;
        }
        //--------------------------------------------------------------------------------
        private void Button_DisplayDirChange_Click(object sender, EventArgs e)
        {
            System.Windows.Forms.FolderBrowserDialog dlg = new System.Windows.Forms.FolderBrowserDialog();

            dlg.SelectedPath = TextBox_DisplayDirPath.Text;
            if (dlg.ShowDialog() == System.Windows.Forms.DialogResult.OK)
            {
                Display_Dir = dlg.SelectedPath;
                TextBox_DisplayDirPath.Text = dlg.SelectedPath;

                LoadSavedScanList();
                ClearSavedScanCfgItems();
            }
        }
        private void ClearSavedScanCfgItems()
        {
            for (Int32 i = 0; i < MAX_CFG_SECTION; i++)
            {
                Label_SavedScanType[i].Text = String.Empty;
                Label_SavedRangeStart[i].Text = String.Empty;
                Label_SavedRangeEnd[i].Text = String.Empty;
                Label_SavedWidth[i].Text = String.Empty;
                Label_SavedDigRes[i].Text = String.Empty;
                Label_SavedExposure[i].Text = String.Empty;
            }
            Label_SavedAvg.Text = String.Empty;
        }
        private void listBox_SavedData_SelectedIndexChanged(object sender, EventArgs e)
        {
            if (listBox_SavedData.SelectedIndex < 0)
                return;

            String item = listBox_SavedData.SelectedItem.ToString();
            String FileName = Path.Combine(TextBox_DisplayDirPath.Text, item);

            // Read scan result and populate to the buffer
            if (Scan.ReadScanResultFromBinFile(FileName) == SDK.FAIL)
            {
                DBG.WriteLine("Read file failed!");
                //MainWindow.ShowError("Read file failed!\nThis file may not match the format!");
                String text = "Read file failed!\nThis file may not match the format!";
                MessageBox.Show(text, "Warning");
                return;
            }

            Scan.GetScanResult();

            // Draw the scan result
            SpectrumPlot();

            // Populate config data
            ClearSavedScanCfgItems();

            for (Int32 i = 0; i < Scan.ScanConfigData.head.num_sections; i++)
            {
                Label_SavedScanType[i].Text = Helper.ScanTypeIndexToMode(Scan.ScanConfigData.section[i].section_scan_type).Substring(0, 3);
                Label_SavedRangeStart[i].Text = Scan.ScanConfigData.section[i].wavelength_start_nm.ToString();
                Label_SavedRangeEnd[i].Text = Scan.ScanConfigData.section[i].wavelength_end_nm.ToString();
                Label_SavedWidth[i].Text = Math.Round(Helper.CfgWidthPixelToNM(Scan.ScanConfigData.section[i].width_px), 2).ToString();
                Label_SavedDigRes[i].Text = Scan.ScanConfigData.section[i].num_patterns.ToString();
                Label_SavedExposure[i].Text = Helper.CfgExpIndexToTime(Scan.ScanConfigData.section[i].exposure_time).ToString();
            }
            Label_SavedAvg.Text = Scan.ScanConfigData.head.num_repeats.ToString();
        }
        #endregion
        #region Chart
        private void initChart()
        {
            scan_Params = new ConfigurationData();
            // Initial Chart
            scan_Params.SeriesCollection = new SeriesCollection(Mappers.Xy<ObservablePoint>()
                .X(point => point.X)
                .Y(point => point.Y));
            RadioButton_Intensity.Checked = true; // Avoid the y-axis long number at initail stage
                                                  /* MyAxisY.Title = "Intensity";
                                                   MyAxisY.FontSize = 14;
                                                   MyAxisX.FontSize = 14;
                                                   MyAxisY.LabelFormatter = value => Math.Round(value, 3).ToString(); //set this to limit the label digits of axis
                                                   MyChart.ChartLegend = null;
                                                   MyChart.DataTooltip = null;
                                                   MyChart.Zoom = ZoomingOptions.None;*/
            scan_Params.ZoomButtonTitle = " Zoom & Pan Disabled ";
            scan_Params.ZoomButtonBackground = "#7FCCCCCC";
            scan_Params.DataTooltipButtonTitle = " Data Tooltip Disabled ";
            scan_Params.DataTooltipButtonBackground = "#7FCCCCCC";
        }
        private void SpectrumPlot()
        {
            if(MyChart.Series.Count>0)
            {
                int seriesCount = MyChart.Series.Count;
                for (int i = 0; i < seriesCount; i++)
                    MyChart.Series.RemoveAt(0);
            }

            double[] valY = new double[Scan.ScanDataLen];
            double[] valX = new double[Scan.ScanDataLen];
            int dataCount = 0;
            string label = "";
            valX = Scan.WaveLength.ToArray();

            if ((bool)RadioButton_Intensity.Checked == true)
            {
                List<double> doubleList = Scan.Intensity.ConvertAll(x => (double)x);
                valY = doubleList.ToArray();
                label = "Intensity";
            }
            else if ((bool)RadioButton_Absorbance.Checked == true)
            {
                valY = Scan.Absorbance.ToArray();
                label = "Absorbance";
            }
            else if ((bool)RadioButton_Reflectance.Checked == true)
            {
                valY = Scan.Reflectance.ToArray();
                label = "Reflectance";
            }
            else if ((bool)RadioButton_Reference.Checked == true)
            {
                List<double> doubleList = Scan.ReferenceIntensity.ConvertAll(x => (double)x);
                valY = doubleList.ToArray();
                label = "Reference";
            }

            for (int i = 0; i < Scan.ScanDataLen; i++)
            {
                if (Double.IsNaN(valY[i]) || Double.IsInfinity(valY[i]))
                    valY[i] = 0;
            }
            for (int i = 0; i < Scan.ScanConfigData.head.num_sections; i++)
            {
                var chartValues = new ChartValues<ObservablePoint>();
                for (int j = 0; j < Scan.ScanConfigData.section[i].num_patterns; j++)
                    chartValues.Add(new ObservablePoint(valX[j + dataCount], valY[j + dataCount]));

                dataCount += Scan.ScanConfigData.section[i].num_patterns;
                MyChart.Series.Add(new LineSeries
                {
                    Values = chartValues,
                    Title = Scan.ScanConfigData.head.num_sections == 1 ? string.Format("{0}", label) : string.Format("{0}\nsection[{1}]", label, i),
                    StrokeThickness = 1,
                    Fill = null,
                    LineSmoothness = 0,
                    PointGeometry = null,
                    PointGeometrySize = 0,
                });
            }

            // For initial the chart to avoid the crazy axis numbers
            if (Scan.ScanConfigData.head.num_sections == 0)
            {
                MyChart.Series.Add(new LineSeries
                {
                    Values = new ChartValues<ObservablePoint>(),
                    Title = "Intensity",
                    PointGeometry = null,
                    StrokeThickness = 1
                });
            }
        }
        private void RadioButton_Reflectance_CheckedChanged(object sender, EventArgs e)
        {
            SpectrumPlot();
        }

        private void RadioButton_Absorbance_CheckedChanged(object sender, EventArgs e)
        {
            SpectrumPlot();
        }

        private void RadioButton_Intensity_CheckedChanged(object sender, EventArgs e)
        {
            SpectrumPlot();
        }

        private void RadioButton_Reference_CheckedChanged(object sender, EventArgs e)
        {
            SpectrumPlot();
        }
        #endregion
        #region scan item set
        private void CheckBox_AutoGain_CheckedChanged(object sender, EventArgs e)
        {
            if (CheckBox_AutoGain.Checked == true)
            {
                ComboBox_PGAGain.Enabled = false;
            }
            else
            {
                ComboBox_PGAGain.Enabled = true;
            }
        }
        private void CheckBox_LampOn_CheckedChanged(object sender, EventArgs e)
        {
            if (CheckBox_LampOn.Checked == true)
                RadioButton_LampOn_CheckedChanged(sender, e);
            else
                RadioButton_LampStableTime_CheckedChanged(sender, e);
        }
        private void RadioButton_LampOn_CheckedChanged(object sender, EventArgs e)
        {
            String HWRev = String.Empty;
            if (Device.IsConnected())
                HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;

            tivaVerChk();
            //GetActivationKeyStatus();
            if (IsOldTivaFW || label_ActivateStatus.Text.Equals("Activated!") == false || HWRev == "B" || HWRev == String.Empty)
            {
                CheckBox_AutoGain.Checked = false;
                CheckBox_AutoGain.Enabled = false;
                CheckBox_AutoGain_CheckedChanged(sender, e);
            }
            RadioButton_Absorbance.Enabled = true;
            RadioButton_Reflectance.Enabled = true;
            TextBox_LampStableTime.Enabled = false;
            Scan.SetLamp(Scan.LAMP_CONTROL.ON);

            Double ScanTime = Scan.GetEstimatedScanTime();
            if (ScanTime > 0)
                Label_EstimatedScanTime.Text = "Est. Device Scan Time: " + String.Format("{0:0.000}", ScanTime) + " secs.";
        }
        private void tivaVerChk()
        {
            int lastVer, curVer;
            Byte[] latetestVerCode = { Convert.ToByte(2), Convert.ToByte(1), Convert.ToByte(0), Convert.ToByte(42) };
            lastVer = BitConverter.ToInt32(latetestVerCode, 0);
            curVer = BitConverter.ToInt32(Device.DevInfo.TivaRev, 0);

            if (curVer < lastVer)
                IsOldTivaFW = true;
        }

        private void RadioButton_LampOff_CheckedChanged(object sender, EventArgs e)
        {
            String HWRev = String.Empty;
            if (Device.IsConnected())
                HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;

            tivaVerChk();
            if (IsOldTivaFW || label_ActivateStatus.Text.Equals("Activated!") == false || HWRev == "B" || HWRev == String.Empty)
            {
                CheckBox_AutoGain.Checked = false;
                CheckBox_AutoGain.Enabled = false;
                CheckBox_AutoGain_CheckedChanged(sender, e);
            }
            RadioButton_Absorbance.Enabled = false;
            RadioButton_Reflectance.Enabled = false;
            TextBox_LampStableTime.Enabled = false;
            Scan.SetLamp(Scan.LAMP_CONTROL.OFF);

            Double ScanTime = Scan.GetEstimatedScanTime();
            if (ScanTime > 0)
                Label_EstimatedScanTime.Text = "Est. Device Scan Time: " + String.Format("{0:0.000}", ScanTime) + " secs.";
        }

        private void RadioButton_LampStableTime_CheckedChanged(object sender, EventArgs e)
        {
            String HWRev = String.Empty;
            if (Device.IsConnected())
                HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;

            if (label_ActivateStatus.Text.Equals("Activated!") == true)
                TextBox_LampStableTime.Enabled = true;
            CheckBox_AutoGain.Enabled = true;
            CheckBox_AutoGain_CheckedChanged(sender, e);
            RadioButton_Absorbance.Enabled = true;
            RadioButton_Reflectance.Enabled = true;
            Scan.SetLamp(Scan.LAMP_CONTROL.AUTO);

            Double ScanTime = Scan.GetEstimatedScanTime();
            if (ScanTime > 0)
                Label_EstimatedScanTime.Text = "Est. Device Scan Time: " + String.Format("{0:0.000}", ScanTime) + " secs.";
        }

        private void ScanAvg_TextChanged(object sender, EventArgs e)
        {
             try
             {
                 ushort value = ushort.Parse(textBox_ScanAvg.Text.ToString());
                 Scan.SetScanNumRepeats(value);
                 Double ScanTime = Scan.GetEstimatedScanTime();
                 if (ScanTime > 0)
                     Label_EstimatedScanTime.Text = "Est. Device Scan Time: " + String.Format("{0:0.000}", ScanTime) + " secs.";
                 }
             catch (Exception e1)
             {
                 Console.WriteLine("Exception caught: {0}", e1);
             }
        }

        private void TextBox_LampStableTime_TextChanged(object sender, EventArgs e)
        {
            if (UInt32.TryParse(TextBox_LampStableTime.Text, out LampStableTime) == false)
            {
                String text = "Lamp Stable Time must be numeric!";
                MessageBox.Show(text, "Warning");
                TextBox_LampStableTime.Text = "625";
                return;
            }

            Scan.SetLampDelay(LampStableTime);
            Double ScanTime = Scan.GetEstimatedScanTime();
            if (ScanTime > 0)
                Label_EstimatedScanTime.Text = "Est. Device Scan Time: " + String.Format("{0:0.000}", ScanTime) + " secs.";
        }

        public enum GUI_State
        {
            DEVICE_ON,
            DEVICE_ON_SCANTAB_SELECT,
            DEVICE_OFF,
            DEVICE_OFF_SCANTAB_SELECT,
            SCAN,
            SCAN_FINISHED,
            FW_UPDATE,
            FW_UPDATE_FINISHED,
            REFERENCE_DATA_UPDATE,
            REFERENCE_DATA_UPDATE_FINISHED,
            KEY_ACTIVATE,
            KEY_NOT_ACTIVATE,
        };
        private void GUI_Handler(int state)
        {
            switch (state)
            {
                case (int)MainWindow.GUI_State.KEY_ACTIVATE:
                {
                        GroupBox_LampUsage.Enabled = true;

                        CheckBox_LampOn.Visible =false;
                        RadioButton_LampOn.Visible =true;

                        RadioButton_LampOff.Visible = true;
                        RadioButton_LampStableTime.Visible = true;
                        TextBox_LampStableTime.Visible = true;

                        RadioButton_LampOff.Enabled = true;
                        RadioButton_LampStableTime.Enabled = true;
                        TextBox_LampStableTime.Enabled = true;

                        RadioButton_LampStableTime.Checked = true;
                        break;
                }
                case (int)MainWindow.GUI_State.KEY_NOT_ACTIVATE:
                    {
                        GroupBox_LampUsage.Enabled = false;
                        String HWRev = String.Empty;
                        if (Device.IsConnected())
                            HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;

                        CheckBox_LampOn.Visible = true;
                        RadioButton_LampOn.Visible = false;

                        RadioButton_LampOff.Visible = false;
                        RadioButton_LampStableTime.Visible = false;
                        TextBox_LampStableTime.Visible = false;

                        RadioButton_LampStableTime.Checked = false;

                        CheckBox_LampOn.Checked = false;
                        RadioButton_LampOn.Checked = false;
                        RadioButton_LampOff.Checked = false;
                        break;
                    }
                default:
                    break;
            }
        }
        #endregion
        #region save scan to file
        private void SaveToFiles()
        {
            String FileName = String.Empty;
            FileName = Path.Combine(Scan_Dir, Scan.ScanConfigData.head.config_name + "_" + TimeScanStart.ToString("yyyyMMdd_HHmmss"));
            SaveToCSV(FileName + ".csv");

            if (CheckBox_SaveDAT.Checked == true)
                Scan.SaveScanResultToBinFile(FileName + ".dat");  // For populating saved scan
            LoadSavedScanList();
        }

        private void SaveToCSV(String FileName)
        {
            if (CheckBox_SaveCombCSV.Checked == true)
            {
                FileStream fs = new FileStream(FileName, FileMode.Create);
                SaveHeader_CSV(in fs, out StreamWriter sw);

                sw.WriteLine("Wavelength (nm),Absorbance (AU),Reference Signal (unitless),Sample Signal (unitless)");
                for (Int32 i = 0; i < Scan.ScanDataLen; i++)
                {
                    sw.WriteLine(Scan.WaveLength[i] + "," + Scan.Absorbance[i] + "," + Scan.ReferenceIntensity[i] + "," + Scan.Intensity[i]);
                }

                sw.Flush();  // Clear buffer
                sw.Close();  // Close file stream
            }

            if (CheckBox_SaveICSV.Checked == true)
            {
                String FileName_i = FileName.Insert(FileName.LastIndexOf(".csv"), "_i");
                FileStream fs = new FileStream(FileName_i, FileMode.Create);
                SaveHeader_CSV(in fs, out StreamWriter sw);

                sw.WriteLine("Wavelength (nm),Sample Signal (unitless)");
                for (Int32 i = 0; i < Scan.ScanDataLen; i++)
                {
                    sw.WriteLine(Scan.WaveLength[i] + "," + Scan.Intensity[i]);
                }

                sw.Flush();  // Clear buffer
                sw.Close();  // Close file stream
            }

            if (CheckBox_SaveACSV.Checked == true)
            {
                String FileName_a = FileName.Insert(FileName.LastIndexOf(".csv"), "_a");
                FileStream fs = new FileStream(FileName_a, FileMode.Create);
                SaveHeader_CSV(in fs, out StreamWriter sw);

                sw.WriteLine("Wavelength (nm),Absorbance (AU)");
                for (Int32 i = 0; i < Scan.ScanDataLen; i++)
                {
                    sw.WriteLine(Scan.WaveLength[i] + "," + Scan.Absorbance[i]);
                }

                sw.Flush();  // Clear buffer
                sw.Close();  // Close file stream
            }

            if (CheckBox_SaveRCSV.Checked == true)
            {
                String FileName_r = FileName.Insert(FileName.LastIndexOf(".csv"), "_r");
                FileStream fs = new FileStream(FileName_r, FileMode.Create);
                SaveHeader_CSV(in fs, out StreamWriter sw);

                sw.WriteLine("Wavelength (nm),Reflectance (AU)");
                for (Int32 i = 0; i < Scan.ScanDataLen; i++)
                {
                    sw.WriteLine(Scan.WaveLength[i] + "," + Scan.Reflectance[i]);
                }

                sw.Flush();  // Clear buffer
                sw.Close();  // Close file stream
            }
        }

        private void SaveHeader_CSV(in FileStream fs, out StreamWriter sw)
        {
            sw = new StreamWriter(fs, System.Text.Encoding.UTF8);
            String ModelName = Device.DevInfo.ModelName;
            String TivaRev = Device.DevInfo.TivaRev[0].ToString() + "."
                           + Device.DevInfo.TivaRev[1].ToString() + "."
                           + Device.DevInfo.TivaRev[2].ToString() + "."
                           + Device.DevInfo.TivaRev[3].ToString();
            String DLPCRev = Device.DevInfo.DLPCRev[0].ToString() + "."
                           + Device.DevInfo.DLPCRev[1].ToString() + "."
                           + Device.DevInfo.DLPCRev[2].ToString();
            String SpecLibRev = Device.DevInfo.SpecLibRev[0].ToString() + "."
                           + Device.DevInfo.SpecLibRev[1].ToString() + "."
                           + Device.DevInfo.SpecLibRev[2].ToString();
            String UUID = BitConverter.ToString(Device.DevInfo.DeviceUUID).Replace("-", ":");
            String HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;
            String Detector_Board_HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(2, 1) : String.Empty;
            String Manufacturing_SerNum = Device.DevInfo.Manufacturing_SerialNumber;
            //--------------------------------------------------------
            String Data_Date_Time = String.Empty, Ref_Config_Name = String.Empty, Ref_Data_Date_Time = String.Empty;
            String Section_Config_Type = String.Empty, Ref_Section_Config_Type = String.Empty;
            String Pattern_Width = String.Empty, Ref_Pattern_Width = String.Empty;
            String Exposure = String.Empty, Ref_Exposure = String.Empty;
            //----------------------------------------------

            String[,] CSV = new String[29, 15];
            for (int i = 0; i < 29; i++)
                for (int j = 0; j < 15; j++)
                    CSV[i, j] = ",";

            // Section information field names
            CSV[0, 0] = "***Scan Config Information***,";
            CSV[0, 7] = "***Reference Scan Information***";
            CSV[17, 0] = "***General Information***,";
            CSV[17, 7] = "***Calibration Coefficients***";
            CSV[28, 0] = "***Scan Data***";
            // Config field names & values
            for (int i = 0; i < 2; i++)
            {
                CSV[1, i * 7] = "Scan Config Name:,";
                CSV[2, i * 7] = "Scan Config Type:,";
                CSV[2, i * 7 + 2] = "Num Section:,";
                CSV[3, i * 7] = "Section Config Type:,";
                CSV[4, i * 7] = "Start Wavelength (nm):,";
                CSV[5, i * 7] = "End Wavelength (nm):,";
                CSV[6, i * 7] = "Pattern Width (nm):,";
                CSV[7, i * 7] = "Exposure (ms):,";
                CSV[8, i * 7] = "Digital Resolution:,";
                CSV[9, i * 7] = "Num Repeats:,";
                CSV[10, i * 7] = "PGA Gain:,";
                CSV[11, i * 7] = "System Temp (C):,";
                CSV[12, i * 7] = "Humidity (%):,";
                CSV[13, i * 7] = "Lamp PT:,";
                CSV[14, i * 7] = "Data Date-Time:,";
            }
            for (int i = 0; i < Scan.ScanConfigData.head.num_sections; i++)
            {
                if (i == 0)
                {
                    // Scan config values
                    CSV[1, 1] = Scan.ScanConfigData.head.config_name + ",";
                    CSV[2, 1] = "Slew,";
                    CSV[2, 3] = Scan.ScanConfigData.head.num_sections + ",";
                    CSV[9, 1] = Scan.ScanConfigData.head.num_repeats + ",";
                    CSV[10, 1] = Scan.PGA + ",";
                    CSV[11, 1] = Scan.SensorData[0] + ",";
                    CSV[12, 1] = Scan.SensorData[2] + ",";
                    CSV[13, 1] = Scan.SensorData[3] + ",";

                    Data_Date_Time = Scan.ScanDateTime[2] + "/" + Scan.ScanDateTime[1] + "/" + Scan.ScanDateTime[0] + " @ " +
                                 Scan.ScanDateTime[3] + ":" + Scan.ScanDateTime[4] + ":" + Scan.ScanDateTime[5];
                    CSV[14, 1] = Data_Date_Time + ",";

                    //Reference config values
                    Ref_Config_Name = (RadioButton_RefFac.Checked == true) ? "Built-In Reference" : "User Reference";
                    if (RadioButton_RefFac.Checked == true)
                    {
                        if (Scan.ReferenceScanConfigData.head.config_name == "SystemTest")
                        {
                            Ref_Config_Name = "Built -in Factory Reference,";
                        }
                        else
                        {
                            Ref_Config_Name = "Built-in User Reference,";
                        }
                    }
                    else
                    {
                        Ref_Config_Name = "Local New Reference,";
                    }
                    CSV[1, 8] = Ref_Config_Name + ",";
                    CSV[2, 8] = "Slew,";
                    CSV[2, 10] = Scan.ReferenceScanConfigData.head.num_sections + ",";
                    CSV[9, 8] = Scan.ReferenceScanConfigData.head.num_repeats + ",";
                    CSV[10, 8] = Scan.ReferencePGA + ",";
                    CSV[11, 8] = Scan.ReferenceSensorData[0] + ",";
                    CSV[12, 8] = Scan.ReferenceSensorData[2] + ",";
                    CSV[13, 8] = Scan.ReferenceSensorData[3] + ",";

                    Ref_Data_Date_Time = Scan.ReferenceScanDateTime[2] + "/" + Scan.ReferenceScanDateTime[1] + "/" + Scan.ReferenceScanDateTime[0] + " @ " +
                           Scan.ReferenceScanDateTime[3] + ":" + Scan.ReferenceScanDateTime[4] + ":" + Scan.ReferenceScanDateTime[5];
                    CSV[14, 8] = Ref_Data_Date_Time + ",";
                }
                // Scan config section values
                Section_Config_Type = Helper.ScanTypeIndexToMode(Scan.ScanConfigData.section[i].section_scan_type);
                CSV[3, i + 1] = Section_Config_Type + ",";
                CSV[4, i + 1] = Scan.ScanConfigData.section[i].wavelength_start_nm.ToString() + ",";
                CSV[5, i + 1] = Scan.ScanConfigData.section[i].wavelength_end_nm.ToString() + ",";

                Pattern_Width = Math.Round(Helper.CfgWidthPixelToNM(Scan.ScanConfigData.section[i].width_px), 2).ToString();
                CSV[6, i + 1] = Pattern_Width + ",";

                Exposure = Helper.CfgExpIndexToTime(Scan.ScanConfigData.section[i].exposure_time).ToString();
                CSV[7, i + 1] = Exposure + ",";
                CSV[8, i + 1] = Scan.ScanConfigData.section[i].num_patterns.ToString() + ",";

                // Reference config section values
                if (i < Scan.ReferenceScanConfigData.head.num_sections)
                {
                    Ref_Section_Config_Type = Helper.ScanTypeIndexToMode(Scan.ReferenceScanConfigData.section[i].section_scan_type);
                    CSV[3, i + 8] = Ref_Section_Config_Type + ",";

                    CSV[4, i + 8] = Scan.ReferenceScanConfigData.section[i].wavelength_start_nm.ToString() + ",";
                    CSV[5, i + 8] = Scan.ReferenceScanConfigData.section[i].wavelength_end_nm.ToString() + ",";

                    Ref_Pattern_Width = (Scan.ReferenceScanConfigData.head.num_sections > 1 || i == 0) ? Math.Round(Helper.CfgWidthPixelToNM(Scan.ReferenceScanConfigData.section[i].width_px), 2).ToString() : String.Empty;
                    CSV[6, i + 8] = Ref_Pattern_Width + ",";

                    Ref_Exposure = (Scan.ReferenceScanConfigData.head.num_sections > 1 || i == 0) ? Helper.CfgExpIndexToTime(Scan.ReferenceScanConfigData.section[i].exposure_time).ToString() : String.Empty;
                    CSV[7, i + 8] = Ref_Exposure + ",";
                    CSV[8, i + 8] = Scan.ReferenceScanConfigData.section[i].num_patterns.ToString() + ",";
                }
            }

            // Measure Time field name & value
            CSV[15, 0] = "Total Measurement Time in sec:,";
            TimeSpan ts = new TimeSpan(TimeScanEnd.Ticks - TimeScanStart.Ticks);
            CSV[15, 1] = ts.TotalSeconds.ToString() + ",";

            // Coefficients filed names & valus
            CSV[18, 7] = "Shift Vector Coefficients:,";
            CSV[18, 8] = Device.Calib_Coeffs.ShiftVectorCoeffs[0].ToString() + ",";
            CSV[18, 9] = Device.Calib_Coeffs.ShiftVectorCoeffs[1].ToString() + ",";
            CSV[18, 10] = Device.Calib_Coeffs.ShiftVectorCoeffs[2].ToString() + ",";
            CSV[19, 7] = "Pixel to Wavelength Coefficients:,";
            CSV[19, 8] = Device.Calib_Coeffs.PixelToWavelengthCoeffs[0].ToString() + ",";
            CSV[19, 9] = Device.Calib_Coeffs.PixelToWavelengthCoeffs[1].ToString() + ",";
            CSV[19, 10] = Device.Calib_Coeffs.PixelToWavelengthCoeffs[2].ToString() + ",";

            // General information field names & values
            CSV[18, 0] = "Model Name:,";
            CSV[18, 1] = ModelName + ",";
            CSV[19, 0] = "Serial Number:,";
            CSV[19, 1] = Scan.ScanConfigData.head.ScanConfig_serial_number + ",";
            CSV[19, 2] = "(" + Manufacturing_SerNum + "),";
            CSV[20, 0] = "GUI Version:,";
            CSV[20, 1] = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version.ToString() + ",";
            CSV[21, 0] = "TIVA Version:,";
            CSV[21, 1] = TivaRev + ",";
            CSV[22, 0] = "DLPC Version:,";
            CSV[22, 1] = DLPCRev + ",";
            CSV[23, 0] = "DLPSPECLIB Version:,";
            CSV[23, 1] = SpecLibRev + ",";
            CSV[24, 0] = "UUID:,";
            CSV[24, 1] = UUID + ",";
            CSV[25, 0] = "Main Board Version:,";
            CSV[26, 0] = "Detector Board Version:,";
            CSV[25, 1] = HWRev + ",";
            CSV[26, 1] = Detector_Board_HWRev + ",";

            string buf = "";
            for (int i = 0; i < 29; i++)
            {
                for (int j = 0; j < 15; j++)
                {
                    buf += CSV[i, j];
                    if (j == 14)
                    {
                        sw.WriteLine(buf);
                    }
                }
                buf = "";
            }
        }
        #endregion
        #region startScan
        private void Button_Scan_Click(object sender, EventArgs e)
        {
            Button_CfgCancel_Click(this, e);
            if (Device.IsConnected())
            {
                scan_Params.scanedCounts = 0;
                scan_Params.scanCountsTarget = scan_Params.RepeatedScanCountDown;
                
                Scan.SetScanNumRepeats(ushort.Parse(textBox_ScanAvg.Text.ToString()));

                tivaVerChk();
                scan_Params.PGAGain = GetPGA();
                if (CheckBox_AutoGain.Checked == false)
                {                    
                    if (IsOldTivaFW && CheckBox_LampOn.Checked == true)
                    {
                        Scan.SetPGAGain(scan_Params.PGAGain);
                    }
                    else
                    {
                        Scan.SetFixedPGAGain(true, scan_Params.PGAGain);
                    }
                }

                else
                {
                    if (IsOldTivaFW)
                        Scan.SetFixedPGAGain(false, scan_Params.PGAGain);
                    else
                        Scan.SetFixedPGAGain(true, 0); // This is set to auto PGA
                }

                if (scan_Params.RepeatedScanCountDown == 0)
                    scan_Params.RepeatedScanCountDown++;

                if (bwScan.IsBusy != true)
                    bwScan.RunWorkerAsync();
                else
                {
                    String text = "Scanning in progress...\n\nPlease wait!";
                    MessageBox.Show(text, "Wait");
                }
            }
            else
            {
                String text = "Please connect a device before performing scan!";
                MessageBox.Show(text, "Warning");
            }
        }
        private byte GetPGA()
        {
            byte pga = 64;
            if(ComboBox_PGAGain.Text.ToString().Equals("64"))
            {
                pga = 64;
            }
            else if(ComboBox_PGAGain.Text.ToString().Equals("32"))
            {
                pga = 32;
            }
            else if (ComboBox_PGAGain.Text.ToString().Equals("16"))
            {
                pga = 16;
            }
            else if (ComboBox_PGAGain.Text.ToString().Equals("8"))
            {
                pga = 8;
            }
            else if (ComboBox_PGAGain.Text.ToString().Equals("4"))
            {
                pga = 4;
            }
            else if (ComboBox_PGAGain.Text.ToString().Equals("2"))
            {
                pga = 2;
            }
            else if (ComboBox_PGAGain.Text.ToString().Equals("1"))
            {
                pga = 1;
            }
            return pga;
        }

        private void bwScan_DoScan(object sender, DoWorkEventArgs e)
        {
            DBG.WriteLine("Performing scan... Remained scans: {0}", scan_Params.RepeatedScanCountDown - 1);
            TimeScanStart = DateTime.Now;
            List<object> arguments = new List<object>();

            if (Scan.PerformScan(ReferenceSelect) == 0)
            {
                DBG.WriteLine("Scan completed!");
                TimeScanEnd = DateTime.Now;
                Byte pga = (Byte)Scan.GetPGAGain();
                TimeSpan ts = new TimeSpan(TimeScanEnd.Ticks - TimeScanStart.Ticks);

                arguments.Add("pass");
                arguments.Add(ts);
                arguments.Add(pga);
                e.Result = arguments;
            }
            else
            {
                arguments.Add("failed");
                arguments.Add(TimeSpan.Zero);
                arguments.Add(Convert.ToByte(0));
                e.Result = arguments;
            }
        }

        private void bwScan_DoSacnCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            List<object> arguments = e.Result as List<object>;
            string result = (string)arguments[0];
            TimeSpan ts = (TimeSpan)arguments[1];
            Byte pga = Scan.PGA;
            if (result != "failed")
            {
                SpectrumPlot();

                ++scan_Params.scanedCounts;

                scan_Params.PGAGain = pga;  // If PGA is auto, it can only read the current value after scanning.
                ComboBox_PGAGain.SelectedItem=pga.ToString();
                Label_ScanStatus.Text = "Total Scan Time: " + String.Format("{0:0.000}", ts.TotalSeconds) + " secs.";

                if (ReferenceSelect != Scan.SCAN_REF_TYPE.SCAN_REF_NEW)  // Save scan results except new reference selection.
                    SaveToFiles();
                else if (Scan.IsLocalRefExist)
                {
                    RadioButton_RefPre.Checked = true;
                    RadioButton_RefNew.Checked = false;
                }
                if (GlobalData.UserCancelRepeatedScan == true)
                {
                    GlobalData.UserCancelRepeatedScan = false;
                    scan_Params.RepeatedScanCountDown = 0;
                }
                scan_Params.ScanInterval = 0;
            }
            else
            {
                scan_Params.RepeatedScanCountDown = 0;
                String text = "Scan Failed!";
                MessageBox.Show(text, "Error");
            }
        }
        #endregion
        //------------------------------------------------------------------------------------
        #region Utility
        #region Model Name
        private void Button_ModelNameGet_Click(object sender, EventArgs e)
        {
            StringBuilder pOutBuf = new StringBuilder(128);

            if (Device.ReadModelName(pOutBuf) == 0)
                TextBox_ModelName.Text = pOutBuf.ToString();
            else
                TextBox_ModelName.Text = "Read Failed!";

            pOutBuf.Clear();
        }

        private void Button_ModelNameSet_Click(object sender, EventArgs e)
        {
            if (Device.SetModelName(Helper.CheckRegex(TextBox_ModelName.Text)) == 0)
            {
                if (Device.Information() != 0)
                    DBG.WriteLine("Device Information read failed!");
                GetDeviceInfo();
                if (!String.IsNullOrEmpty(Device.DevInfo.ModelName))
                    TextBox_ModelName.Text = Device.DevInfo.ModelName;
                else
                    TextBox_ModelName.Text = "Read Failed!";
            }
            else
                TextBox_ModelName.Text = "Write Failed!";
        }
        #endregion
        #region Serial Number
        //Serial Number
        private void Button_SerialNumberSet_Click(object sender, EventArgs e)
        {
            String OldSerNum = Device.DevInfo.SerialNumber;

            if (Device.SetSerialNumber(Helper.CheckRegex(TextBox_SerialNumber.Text)) == 0)
            {
                if (Device.Information() != 0)
                    DBG.WriteLine("Device Information read failed!");
                GetDeviceInfo();
                if (!String.IsNullOrEmpty(Device.DevInfo.SerialNumber))
                    TextBox_SerialNumber.Text = Device.DevInfo.SerialNumber;
                else
                    TextBox_SerialNumber.Text = "Read Failed!";
            }
            else
                TextBox_SerialNumber.Text = "Write Failed!";
        }

        private void Button_SerialNumberGet_Click(object sender, EventArgs e)
        {
            StringBuilder pOutBuf = new StringBuilder(128);

            if (Device.GetSerialNumber(pOutBuf) == 0)
                TextBox_SerialNumber.Text = pOutBuf.ToString();
            else
                TextBox_SerialNumber.Text = "Read Failed!";

            pOutBuf.Clear();
        }
        #endregion
        #region Date and Time
        //Date and Time
        private void Button_DateTimeSync_Click(object sender, EventArgs e)
        {
            Device.DeviceDateTime DevDateTime = new Device.DeviceDateTime();
            DateTime Current = DateTime.Now;

            DevDateTime.Year = Current.Year;
            DevDateTime.Month = Current.Month;
            DevDateTime.Day = Current.Day;
            DevDateTime.DayOfWeek = (Int32)Current.DayOfWeek;
            DevDateTime.Hour = Current.Hour;
            DevDateTime.Minute = Current.Minute;
            DevDateTime.Second = Current.Second;

            if (Device.SetDateTime(DevDateTime) == 0)
                TextBox_DateTime.Text = Current.ToString("yyyy/M/d  H:m:s");
            else
                TextBox_DateTime.Text = "Sync Failed!";
        }

        private void Button_DateTimeGet_Click(object sender, EventArgs e)
        {
            if (Device.GetDateTime() == 0)
            {
                TextBox_DateTime.Text = Device.DevDateTime.Year + "/"
                                      + Device.DevDateTime.Month + "/"
                                      + Device.DevDateTime.Day + "  "
                                      + Device.DevDateTime.Hour + ":"
                                      + Device.DevDateTime.Minute + ":"
                                      + Device.DevDateTime.Second;
            }
            else
                TextBox_DateTime.Text = "Get Failed!";
        }
        #endregion
        #region Lamp Usage
        //Lamp Usage
        private String GetLampUsage()
        {
            String lampusage = "";
            UInt64 buf = Device.LampUsage / 1000;

            if (buf / 86400 != 0)
            {
                lampusage += buf / 86400 + "day ";
                buf -= 86400 * (buf / 86400);
            }
            if (buf / 3600 != 0)
            {
                lampusage += buf / 3600 + "hr ";
                buf -= 3600 * (buf / 3600);
            }
            if (buf / 60 != 0)
            {
                lampusage += buf / 60 + "min ";
                buf -= 60 * (buf / 60);
            }
            lampusage += buf + "sec ";
            return lampusage;
        }

        private void Button_LampUsageSet_Click(object sender, EventArgs e)
        {
            if (Double.TryParse(TextBox_LampUsage.Text, out Double LampUsage) == false)
            {
                TextBox_LampUsage.Text = "Not Numeric!";
                return;
            }

            if (Device.WriteLampUsage((UInt64)(LampUsage * 3600000)) == 0)  // hour to milliseconds
                Button_LampUsageGet_Click(sender, e);
            else
                TextBox_LampUsage.Text = "Write Failed!";
            GetDeviceInfo();
        }

        private void Button_LampUsageGet_Click(object sender, EventArgs e)
        {
            if (Device.ReadLampUsage() == 0)
                TextBox_LampUsage.Text = ((Double)Device.LampUsage / 3600000).ToString();  // milliseconds to hour
            else
                TextBox_LampUsage.Text = "Read Failed!";
        }
        #endregion
        #region Sensors
        //Sensors
        private void Button_SensorRead_Click(object sender, EventArgs e)
        {
            if (Device.ReadSensorsData() == 0)
            {
                Label_SensorBattStatus.Text = Device.DevSensors.BattStatus;
                Label_SensorBattCapacity.Text = (Device.DevSensors.BattCapicity != -1) ? (Device.DevSensors.BattCapicity.ToString() + " %") : ("Read Failed!");
                Label_SensorHumidity.Text = (Device.DevSensors.Humidity != -1) ? (Device.DevSensors.Humidity.ToString() + " %") : ("Read Failed!");
                Label_SensorSysTemp.Text = (Device.DevSensors.HDCTemp != -1) ? (Device.DevSensors.HDCTemp.ToString() + " C") : ("Read Failed!");
                Label_SensorTivaTemp.Text = (Device.DevSensors.TivaTemp != -1) ? (Device.DevSensors.TivaTemp.ToString() + " C") : ("Read Failed!");
                Label_SensorLampIntensity.Text = (Device.DevSensors.PhotoDetector != -1) ? (Device.DevSensors.PhotoDetector.ToString()) : ("Read Failed!");
            }
            else
            {
                Label_SensorBattStatus.Text = "Read Failed!";
                Label_SensorBattCapacity.Text = "Read Failed!";
                Label_SensorHumidity.Text = "Read Failed!";
                Label_SensorSysTemp.Text = "Read Failed!";
                Label_SensorTivaTemp.Text = "Read Failed!";
                Label_SensorLampIntensity.Text = "Read Failed!";
            }
        }
        #endregion
        #region Tiva FW update
        //Tiva FW update
        private void Button_TivaFWBrowse_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                InitialDirectory = (Tiva_FWDir == String.Empty) ? (Directory.GetCurrentDirectory()) : (Tiva_FWDir),
                FileName = "",                  // Default file name
                DefaultExt = ".bin",            // Default file extension
                Filter = "Binary File|*.bin"    // Filter files by extension
            };

            // Show open file dialog box
            dlg.ShowDialog();
            // Process open file dialog box results
            if (dlg.FileName != "")
            {
                TextBox_TivaFWPath.Text = dlg.FileName;
                Tiva_FWDir = dlg.FileName.Substring(0, dlg.FileName.LastIndexOf("\\"));
            }
        }
        private void Button_TivaFWUpdate_Click(object sender, EventArgs e)
        {
            SDK.AutoSearch = false;
            SDK.IsEnableNotify = false;

            String filePath = (String)TextBox_TivaFWPath.Text;

            if ((Device.IsConnected() || CheckBox_TivaFlashEmpty.Checked == true) && filePath != "")
            {
                SDK.AutoSearch = false;
                SDK.IsEnableNotify = false;
                if (CheckBox_TivaFlashEmpty.Checked != true)
                    Device.Set_Tiva_To_Bootloader();

                ProgressBar_TivaFWUpdateStatus.Value = 10;

                List<object> arguments = new List<object> { filePath };
                bwTivaUpdate.RunWorkerAsync(arguments);
            }
            else if(filePath == "")
            {
                String text = "Image file was not found !";
                MessageBox.Show(text, "Error");
            }
            else
            {
                String text = "Device dose not exist !";
                MessageBox.Show(text, "Error");
            }
        }
        private void bwTivaUpdate_DoWork(object sender, DoWorkEventArgs e)
        {
            Thread.Sleep(100); // Wait for DFU device appeared
            List<object> arguments = e.Argument as List<object>;
            String filePath = (String)arguments[0];
            bwTivaUpdate.ReportProgress(30);

            int ret = Device.Tiva_FW_Update(filePath);
            if (ret == 0)
                e.Result = 0;
            else
                e.Result = ret;
        }

        private void bwTivaUpdate_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            int percentage = e.ProgressPercentage;
            Thread.Sleep(2000);
            ProgressBar_TivaFWUpdateStatus.Value = percentage;
        }

        private void bwTivaUpdate_DoSacnCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            int ret = (int)e.Result;
            ProgressBar_TivaFWUpdateStatus.Value = 100;

            if (ret == 0)
            {
                String text = "Tiva FW updated successfully!";
                MessageBox.Show(text, "Success");
                if(Device.IsConnected())
                {
                    Device.ResetTiva(true);  // Only reconnect the device
                }
                else
                {
                    Device.ResetTiva(null);
                }
               
            }
            else
            {
                switch (ret)
                {
                    case -1:
                        String text = "The driver, lmdfu.dll, for the USB Device Firmware Upgrade device cannot be found!";
                        MessageBox.Show(text, "Error");
                        break;
                    case -2:
                        text = "The driver for the USB Device Firmware Upgrade device was found but appears to be a version which this program does not support!";
                        MessageBox.Show(text, "Error");
                        break;
                    case -3:
                        text = "An error was reported while attempting to load the device driver for the USB Device Firmware Upgrade device!";
                        MessageBox.Show(text, "Error");
                        break;
                    case -4:
                        text = "Unable to open binary file.Copy binary file to a folder with Admin / read / write permission and try again.";
                        MessageBox.Show(text, "Error");
                        break;
                    case -5:
                        text = "Memory alloc for file read failed!";
                        MessageBox.Show(text, "Error");
                        break;
                    case -6:
                        text = "This image does not appear to be valid for the target device.";
                        MessageBox.Show(text, "Error");
                        break;
                    case -7:
                        text = "This image is not valid NIRNANO FW Image!";
                        MessageBox.Show(text, "Error");
                        break;
                    case -8:
                        text = "Error reported during file download!";
                        MessageBox.Show(text, "Error");
                        break;
                    default:
                        text = "Unknown error occured!";
                        MessageBox.Show(text, "Error");
                        break;
                }
            }
            SDK.AutoSearch = true;
            SDK.IsEnableNotify = true;
            ProgressBar_TivaFWUpdateStatus.Value = 0;
        }
        #endregion
        #region DLPC150 FW Update
        //DLPC150 FW Update
        private void Button_DLPC150FWBrowse_Click(object sender, EventArgs e)
        {
            OpenFileDialog dlg = new OpenFileDialog
            {
                InitialDirectory = (DLPC_FWDir == String.Empty) ? (Directory.GetCurrentDirectory()) : (DLPC_FWDir),
                FileName = "",              // Default file name
                DefaultExt = ".img",        // Default file extension
                Filter = "Image File|*.img" // Filter files by extension
            };

            dlg.ShowDialog();
            if (dlg.FileName != "")
            {
                TextBox_DLPC150FWPath.Text = dlg.FileName;
                DLPC_FWDir = dlg.FileName.Substring(0, dlg.FileName.LastIndexOf("\\"));
            }
        }
        private void Button_DLPC150FWUpdate_Click(object sender, EventArgs e)
        {
            if (Device.IsConnected() && TextBox_DLPC150FWPath.Text != "")
            {
                bwDLPCUpdate.RunWorkerAsync(TextBox_DLPC150FWPath.Text);
            }
            else
            {
                String text = "Device dose not exist or image file path error!";
                MessageBox.Show(text, "Error");
            }
        }

        private void bwDLPCUpdate_DoWork(object sender, DoWorkEventArgs e)
        {
            int expectedChecksum = 0, chksum = 0, ret = 0;
            String fileName = (String)e.Argument;
            byte[] imgByteBuff = File.ReadAllBytes(fileName);
            e.Result = false;

            int dataLen = imgByteBuff.Length;

            if (!Device.DLPC_CheckSignature(imgByteBuff))
            {
                DBG.WriteLine("Invalid DLPC150 image file!");
                return;
            }

            ret = Device.DLPC_SetImageSize(dataLen);
            if (ret < 0)
            {
                DBG.WriteLine("Set DLPC150 image size failed! (error: {0})", ret);
                return;
            }

            for (int i = 0; i < dataLen; i++)
            {
                expectedChecksum += imgByteBuff[i];
            }

            Thread.Sleep(1000);

            int bytesToSend = dataLen, bytesSent = 0;
            while (bytesToSend > 0)
            {
                byte[] byteArrayToSent = new byte[bytesToSend];
                Buffer.BlockCopy(imgByteBuff, dataLen - bytesToSend, byteArrayToSent, 0, bytesToSend);

                bytesSent = Device.DLPC_FW_Update_WriteData(byteArrayToSent, bytesToSend);

                if (bytesSent < 0)
                {
                    DBG.WriteLine("DLPC150 update: Data send Failed!");
                    break;
                }

                bytesToSend -= bytesSent;

                // Report the FW update status
                float updateProgress;
                updateProgress = ((float)(dataLen - bytesToSend) / dataLen) * 100;
                bwDLPCUpdate.ReportProgress((int)updateProgress);
            }

            chksum = Device.DLPC_Get_Checksum();

            if (chksum < 0)
                DBG.WriteLine("Error Reading DLPC150 Flash Checksum! (error: {0})", chksum);
            else if (chksum != expectedChecksum)
                DBG.WriteLine("Checksum mismatched: (Expected: {0}, DLPC Flash: {1})", expectedChecksum, chksum);
            else
            {
                DBG.WriteLine("DLPC150 updated successfully!");
                e.Result = true;
            }
        }

        private void bwDLPCUpdate_ProgressChanged(object sender, ProgressChangedEventArgs e)
        {
            int percentage = e.ProgressPercentage;
            ProgressBar_DLPC150FWUpdateStatus.Value = e.ProgressPercentage;
        }

        private void bwDLPCUpdate_DoWorkCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            if ((bool)e.Result)
            {
                String text = "DLPC150 FW updated successfully!";
                MessageBox.Show(text, "Success");
            }
            else
            {
                String text = "DLPC150 FW update failed!";
                MessageBox.Show(text, "Error");
            }

            ProgressBar_DLPC150FWUpdateStatus.Value = 0;
        }
        #endregion
        #region Calibration Coefficients
        //Calibration Coefficients
        private void Button_CalWriteGenCoeffs_Click(object sender, EventArgs e)
        {
            if (Device.SetGenericCalibStruct() == SDK.PASS)
                Button_CalReadCoeffs_Click(sender, e);
            else
            {
                TextBox_P2WCoeff0.Text = "Write Failed!";
                TextBox_P2WCoeff1.Text = "Write Failed!";
                TextBox_P2WCoeff2.Text = "Write Failed!";
                TextBox_ShiftVectCoeff0.Text = "Write Failed!";
                TextBox_ShiftVectCoeff1.Text = "Write Failed!";
                TextBox_ShiftVectCoeff2.Text = "Write Failed!";
            }
        }

        private void Button_CalReadCoeffs_Click(object sender, EventArgs e)
        {
            if (Device.GetCalibStruct() == SDK.PASS)
            {
                Label_CalCoeffVer.Text = Device.DevInfo.CalRev.ToString();
                Label_RefCalVer.Text = Device.DevInfo.RefCalRev.ToString();
                Label_ScanCfgVer.Text = Device.DevInfo.CfgRev.ToString();
                TextBox_P2WCoeff0.Text = Device.Calib_Coeffs.PixelToWavelengthCoeffs[0].ToString();
                TextBox_P2WCoeff1.Text = Device.Calib_Coeffs.PixelToWavelengthCoeffs[1].ToString();
                TextBox_P2WCoeff2.Text = Device.Calib_Coeffs.PixelToWavelengthCoeffs[2].ToString();
                TextBox_ShiftVectCoeff0.Text = Device.Calib_Coeffs.ShiftVectorCoeffs[0].ToString();
                TextBox_ShiftVectCoeff1.Text = Device.Calib_Coeffs.ShiftVectorCoeffs[1].ToString();
                TextBox_ShiftVectCoeff2.Text = Device.Calib_Coeffs.ShiftVectorCoeffs[2].ToString();
            }
            else
            {
                Label_CalCoeffVer.Text = "0";
                Label_RefCalVer.Text = "0";
                Label_ScanCfgVer.Text = "0";
                TextBox_P2WCoeff0.Text = "Read Failed!";
                TextBox_P2WCoeff1.Text = "Read Failed!";
                TextBox_P2WCoeff2.Text = "Read Failed!";
                TextBox_ShiftVectCoeff0.Text = "Read Failed!";
                TextBox_ShiftVectCoeff1.Text = "Read Failed!";
                TextBox_ShiftVectCoeff2.Text = "Read Failed!";
            }
        }

        private void Button_CalRestoreDefaultCoeffs_Click(object sender, EventArgs e)
        {
            if (Device.RestoreDefaultCalibStruct() == 0)
                Button_CalReadCoeffs_Click(sender, e);
            else
            {
                TextBox_P2WCoeff0.Text = "Restore Failed!";
                TextBox_P2WCoeff1.Text = "Restore Failed!";
                TextBox_P2WCoeff2.Text = "Restore Failed!";
                TextBox_ShiftVectCoeff0.Text = "Restore Failed!";
                TextBox_ShiftVectCoeff1.Text = "Restore Failed!";
                TextBox_ShiftVectCoeff2.Text = "Restore Failed!";
            }
        }

        private void Button_CalWriteCoeffs_Click(object sender, EventArgs e)
        {
            Device.CalibCoeffs Calib_Coeffs = new Device.CalibCoeffs
            {
                PixelToWavelengthCoeffs = new Double[3],
                ShiftVectorCoeffs = new Double[3]
            };

            if ((Double.TryParse(TextBox_P2WCoeff0.Text, out Calib_Coeffs.PixelToWavelengthCoeffs[0]) == false) ||
                (Double.TryParse(TextBox_P2WCoeff1.Text, out Calib_Coeffs.PixelToWavelengthCoeffs[1]) == false) ||
                (Double.TryParse(TextBox_P2WCoeff2.Text, out Calib_Coeffs.PixelToWavelengthCoeffs[2]) == false) ||
                (Double.TryParse(TextBox_ShiftVectCoeff0.Text, out Calib_Coeffs.ShiftVectorCoeffs[0]) == false) ||
                (Double.TryParse(TextBox_ShiftVectCoeff1.Text, out Calib_Coeffs.ShiftVectorCoeffs[1]) == false) ||
                (Double.TryParse(TextBox_ShiftVectCoeff2.Text, out Calib_Coeffs.ShiftVectorCoeffs[2]) == false))
            {
                TextBox_P2WCoeff0.Text = "Not Numeric!";
                TextBox_P2WCoeff1.Text = "Not Numeric!";
                TextBox_P2WCoeff2.Text = "Not Numeric!";
                TextBox_ShiftVectCoeff0.Text = "Not Numeric!";
                TextBox_ShiftVectCoeff1.Text = "Not Numeric!";
                TextBox_ShiftVectCoeff2.Text = "Not Numeric!";
                return;
            }

            if (Device.SendCalibStruct(Calib_Coeffs) == SDK.PASS)
                Button_CalReadCoeffs_Click(sender, e);
            else
            {
                TextBox_P2WCoeff0.Text = "Write Failed!";
                TextBox_P2WCoeff1.Text = "Write Failed!";
                TextBox_P2WCoeff2.Text = "Write Failed!";
                TextBox_ShiftVectCoeff0.Text = "Write Failed!";
                TextBox_ShiftVectCoeff1.Text = "Write Failed!";
                TextBox_ShiftVectCoeff2.Text = "Write Failed!";
            }
        }

        private void CheckBox_CalWriteEnable_CheckedChanged(object sender, EventArgs e)
        {
            if (CheckBox_CalWriteEnable.Checked == true)
            {
                Button_CalWriteCoeffs.Enabled = true;
                Button_CalWriteGenCoeffs.Enabled = true;
                if (IsActivated)
                    Button_CalRestoreDefaultCoeffs.Enabled = true;
                else
                    Button_CalRestoreDefaultCoeffs.Enabled = false;
            }
            else
            {
                Button_CalWriteCoeffs.Enabled = false;
                Button_CalWriteGenCoeffs.Enabled = false;
                Button_CalRestoreDefaultCoeffs.Enabled = false;
            }
        }
        #endregion
        #region Device Information
        //Device Information
        private void GetDeviceInfo()
        {
            if (!Device.IsConnected())
                return;

            String UUID = BitConverter.ToString(Device.DevInfo.DeviceUUID).Replace("-", ":");
            String HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(0, 1) : String.Empty;
            String TivaRev = Device.DevInfo.TivaRev[0].ToString() + "."
                           + Device.DevInfo.TivaRev[1].ToString() + "."
                           + Device.DevInfo.TivaRev[2].ToString() + "."
                           + Device.DevInfo.TivaRev[3].ToString();
            String DLPCRev = Device.DevInfo.DLPCRev[0].ToString() + "."
                           + Device.DevInfo.DLPCRev[1].ToString() + "."
                           + Device.DevInfo.DLPCRev[2].ToString();
            String SpecLibRev = Device.DevInfo.SpecLibRev[0].ToString() + "."
                              + Device.DevInfo.SpecLibRev[1].ToString() + "."
                              + Device.DevInfo.SpecLibRev[2].ToString() + "."
                              + Device.DevInfo.SpecLibRev[3].ToString();
            String GUIRev = Assembly.GetExecutingAssembly().GetName().Version.ToString();
            if(GUIRev.Length!=0)
            {
                GUIRev = GUIRev.Substring(0, 5);
            }
            String Detector_Board_HWRev = (!String.IsNullOrEmpty(Device.DevInfo.HardwareRev)) ? Device.DevInfo.HardwareRev.Substring(2, 1) : String.Empty;
            String Lamp_Usage = "";
            if (Device.ReadLampUsage() == 0)
            {
                Lamp_Usage = GetLampUsage();
            }
            else
                Lamp_Usage = "NA";
            //-------------------------------------------------------------
            label_DevInfoGUIVer.Text = GUIRev;
            label_DevInfoTivaSWVer.Text = TivaRev;
            label_DevInfoDLPCVer.Text = DLPCRev;
            label_DevInfoSpecLibVer.Text = SpecLibRev;
            label_DevInfoMainBoardVer.Text = HWRev;
            label_DevInfoDetectorBoardVer.Text = Detector_Board_HWRev;
            label_DevInfoModelName.Text = Device.DevInfo.ModelName;
            label_DevInfoDevSerNum.Text = Device.DevInfo.SerialNumber;
            label_DevInfoManfacSerNUm.Text = Device.DevInfo.Manufacturing_SerialNumber;
            label_DevInfoUUID.Text = UUID;
            label_DevInfoLampUsage.Text = Lamp_Usage;
        }
        #endregion
        #region Activation Key
        //Activation Key
        private void button_KeySet_Click(object sender, EventArgs e)
        {
            String[] StrKey = textBox_Key.Text.Split(new char[] { ' ', ':', ';', '-', '_' });
            Byte[] ByteKey = new Byte[12];

            for (int i = 0; i < StrKey.Length; i++)
            {
                try { ByteKey[i] = Convert.ToByte(StrKey[i], 16); }
                catch { ByteKey[i] = 0; }
            }

            Device.SetActivationKey(ByteKey);
           
            if (IsActivated)
            {
                label_ActivateStatus.Text = "Activated!";
                GUI_Handler((int)MainWindow.GUI_State.KEY_ACTIVATE);
            }
            else
            {
                label_ActivateStatus.Text = "Not activated!";
                GUI_Handler((int)MainWindow.GUI_State.KEY_NOT_ACTIVATE);
            }
        }
        private void GetActivationKeyStatus()
        {

            if (IsActivated)
            {
                label_ActivateStatus.Text = "Activated!";
                GUI_Handler((int)MainWindow.GUI_State.KEY_ACTIVATE);
            }
            else
            {
                label_ActivateStatus.Text = "Not activated!";
                GUI_Handler((int)MainWindow.GUI_State.KEY_NOT_ACTIVATE);
            }
        }

        private void button_KeyClear_Click(object sender, EventArgs e)
        {
            String buf = "000000000000";
            String[] StrKey = buf.Split(new char[] { ' ', ':', ';', '-', '_' });
            Byte[] ByteKey = new Byte[12];

            for (int i = 0; i < StrKey.Length; i++)
            {
                try { ByteKey[i] = Convert.ToByte(StrKey[i], 16); }
                catch { ByteKey[i] = 0; }
            }

            Device.SetActivationKey(ByteKey);

            if (IsActivated)
            {
                label_ActivateStatus.Text = "Activated!";
                GUI_Handler((int)MainWindow.GUI_State.KEY_ACTIVATE);
            }
            else
            {
                label_ActivateStatus.Text = "Not activated!";
                GUI_Handler((int)MainWindow.GUI_State.KEY_NOT_ACTIVATE);
            }
        }
        #endregion
        #region Reset Device
        //Device
        //Reset Device
        private void button_DeviceResetSys_Click(object sender, EventArgs e)
        {
            if (!Device.IsConnected())
                return;

            bwTivaReset = new BackgroundWorker
            {
                WorkerReportsProgress = false,
                WorkerSupportsCancellation = true
            };
            bwTivaReset.DoWork += new DoWorkEventHandler(bwTivaReset_DoWork);
            bwTivaReset.RunWorkerAsync();
        }
        private BackgroundWorker bwTivaReset;
        private static void bwTivaReset_DoWork(object sender, DoWorkEventArgs e)
        {
            Device.ResetTiva(false);
        }
        #endregion
        #region Update Reference Data
        //Update Reference Data
        private void button_DeviceUpdateRef_Click(object sender, EventArgs e)
        {
            if (Device.IsConnected())
            {
                bwRefScanProgress = new BackgroundWorker
                {
                    WorkerReportsProgress = false,
                    WorkerSupportsCancellation = true
                };
                bwRefScanProgress.DoWork += new DoWorkEventHandler(bwRefScanProgress_DoWork);
                bwRefScanProgress.RunWorkerCompleted += new RunWorkerCompletedEventHandler(bwRefScanProgress_DoWorkCompleted);
                bwRefScanProgress.RunWorkerAsync();
            }
            else
            {
                String text = "No device is connected!";
                MessageBox.Show(text, "Warning");
            }
        }

        public ScanConfig.SlewScanConfig tmpCfg;//backup current config before update reference
        private void bwRefScanProgress_DoWork(object sender, DoWorkEventArgs e)
        {
            tmpCfg = ScanConfig.GetCurrentConfig();
            ScanConfig.SlewScanConfig scanCfg = new ScanConfig.SlewScanConfig();
            scanCfg.head.config_name = "UserReference";
            scanCfg.head.scan_type = 2;
            scanCfg.head.num_sections = 1;
            scanCfg.head.num_repeats = 30;
            scanCfg.section = new ScanConfig.SlewScanSection[5];
            scanCfg.section[0].section_scan_type = 0;
            scanCfg.section[0].wavelength_start_nm = 900;
            scanCfg.section[0].wavelength_end_nm = 1700;
            scanCfg.section[0].width_px = 6;
            scanCfg.section[0].num_patterns = 228;
            scanCfg.section[0].exposure_time = 0;

            int ret = ScanConfig.SetScanConfig(scanCfg);
            if (ret != 0)
            {
                e.Result = -3;
                return;
            }

            Thread.Sleep(200);
            ret = Scan.PerformScan(Scan.SCAN_REF_TYPE.SCAN_REF_BUILT_IN);
            if (ret == 0)
            {
                ret = Scan.SaveReferenceScan();
                if (ret == 0)
                    e.Result = 0;
                else
                    e.Result = -2;
            }
            else
                e.Result = -1;
        }

        private void bwRefScanProgress_DoWorkCompleted(object sender, RunWorkerCompletedEventArgs e)
        {
            int ret = (int)e.Result;
            if (ret == 0)
            {
                String text = "Reference Scan Completed Seccessfully!\n\nPlease start a new scan to check the result.";
                MessageBox.Show(text, "Success");
            }
            else if (ret == -1)
            {
                String text = "Scan Failed!";
                MessageBox.Show(text, "Error");
            }
            else if (ret == -2)
            {
                String text = "Save Reference Sacn Failed!";
                MessageBox.Show(text, "Error");
            }
            else if (ret == -3)
            {
                String text = "Set Reference Sacn Configuration Failed!";
                MessageBox.Show(text, "Error");
            }
            else
            {
                String text = "Unknow Error Occured!";
                MessageBox.Show(text, "Error");
            }
            ScanConfig.SetScanConfig(tmpCfg);//set current config after update reference
        }
        #endregion
        #region Back up factory reference
        //Back up factory reference
        private void button_DeviceBackUpFacRef_Click(object sender, EventArgs e)
        {
            if (Device.IsConnected())
            {
                int ret;
                string serNum = Device.DevInfo.SerialNumber.ToString();
                ret = Device.Backup_Factory_Reference(serNum);
                if (ret < 0)
                {
                    switch (ret)
                    {
                        case -1:
                            String text = "Factory reference data backup FAILED!\n\nOut of memory.";
                            MessageBox.Show(text, "Error");
                            break;
                        case -2:
                            text = "Factory reference data backup FAILED!\n\nSystem I/O error";
                            MessageBox.Show(text, "Error");
                            break;
                        case -3:
                            text = "Factory reference data backup FAILED!\n\nDevice communcation error";
                            MessageBox.Show(text, "Error");
                            break;
                        case -4:
                            text = "Factory reference data backup FAILED!\n\nDevice does not have the original factory reference data";
                            MessageBox.Show(text, "Error");
                            break;
                    }
                }
                else
                {
                    String text = "Factory reference data has been saved in local storage successfully!";
                    MessageBox.Show(text, "Success");
                }
            }
            else
            {
                String text = "No device connected for backup factory reference!";
                MessageBox.Show(text, "Warning");
            }
        }
        private void button_DeviceRestoreFacRef_Click(object sender, EventArgs e)
        {
            if (Device.IsConnected())
            {
                int ret;
                string serNum = Device.DevInfo.SerialNumber.ToString();
                ret = Device.Restore_Factory_Reference(serNum);
                if (ret < 0)
                {
                    switch (ret)
                    {
                        case -1:
                            String text = "Factory reference data restore FAILED!\n\nOut of memory.";
                            MessageBox.Show(text, "Error");
                            break;
                        case -2:
                            text = "Factory reference data restore FAILED!\n\nBackup directory not found";
                            MessageBox.Show(text, "Error");
                            break;
                        case -3:
                            text = "Factory reference data restore FAILED!\n\nRead file error";
                            MessageBox.Show(text, "Error");
                            break;
                        case -4:
                            text = "Factory reference data restore FAILED!\n\nReference data currupted";
                            MessageBox.Show(text, "Error");
                            break;
                        case -5:
                            text = "Factory reference data restore FAILED!\n\nDevice communcation error";
                            MessageBox.Show(text, "Error");
                            break;
                        case -6:
                            text = "Factory reference data restore FAILED!\n\nData was NOT the original factory reference data";
                            MessageBox.Show(text, "Error");
                            break;
                    }
                }
                else
                {
                    String text = "Factory reference data has been restored successfully!\n\nPlease start a new scan to check the result.";
                    MessageBox.Show(text, "Success");
                    //ClearScanPlotsEvent();
                }
            }
            else
            {
                String text = "No device connected for restoring factory reference!";
                MessageBox.Show(text, "Warning");
            }
        }
        #endregion
        #region About
        //About
        private void button_AboutLicense_Click(object sender, EventArgs e)
        {
            LicenseWindow window = new LicenseWindow { Owner = this };
            window.ShowDialog();
        }

        private void button_About_Click(object sender, EventArgs e)
        {
            try
            {
                Process.Start("http://www.inno-spectra.com/");
            }
            catch { }
        }

        #endregion

        #endregion

        private void Button_SaveDirChange_Click(object sender, EventArgs e)
        {
            using (var fbd = new FolderBrowserDialog())
            {
                fbd.SelectedPath = Scan_Dir;
                DialogResult result = fbd.ShowDialog();

                if (result == DialogResult.OK && !string.IsNullOrWhiteSpace(fbd.SelectedPath))
                {
                    TextBox_SaveDirPath.Text = fbd.SelectedPath;
                    Scan_Dir = fbd.SelectedPath;
                }
            }
        }

        private void tabControl2_SelectedIndexChanged(object sender, EventArgs e)
        {
            Button_CfgCancel_Click(this, e);
        }

        private void tabControl1_SelectedIndexChanged(object sender, EventArgs e)
        {
            Button_CfgCancel_Click(this, e);
        }
    }
}
