using LiveCharts;
using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using static DLP_NIR_Win_SDK_WinForm_App_CS.MainWindow;

namespace DLP_NIR_Win_SDK_WinForm_App_CS
{
    public class ConfigurationData : INotifyPropertyChanged
    {
        private ushort _scanAvg;
        public ushort ScanAvg
        {
            get { return _scanAvg; }
            set { _scanAvg = value; OnPropertyChanged(nameof(ScanAvg)); }
        }

        private byte _pgaGain;
        public byte PGAGain
        {
            get { return _pgaGain; }
            set { _pgaGain = value; OnPropertyChanged(nameof(PGAGain)); }
        }

        private int _repeatedScanCountDown;
        public int RepeatedScanCountDown
        {
            get { return _repeatedScanCountDown; }
            set
            {
                GlobalData.RepeatedScanCountDown = value;
                _repeatedScanCountDown = value;
                OnPropertyChanged(nameof(RepeatedScanCountDown));
            }
        }

        private int _scanInterval;
        public int ScanInterval
        {
            get { return _scanInterval; }
            set { _scanInterval = value; OnPropertyChanged(nameof(ScanInterval)); }
        }

        private int _scanedCounts;
        public int scanedCounts
        {
            get { return _scanedCounts; }
            set
            {
                GlobalData.ScannedCounts = value;
                _scanedCounts = value;
            }
        }

        private int _scanCountsTarget;
        public int scanCountsTarget
        {
            get { return _scanCountsTarget; }
            set
            {
                GlobalData.TargetScanNumber = value;
                _scanCountsTarget = value;
            }
        }

        public event PropertyChangedEventHandler PropertyChanged;
        protected void OnPropertyChanged(string name)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(name));
        }

        // Chart 
        public SeriesCollection SeriesCollection { get; set; }
        private string _ZoomButtonTitle;
        public string ZoomButtonTitle
        {
            get { return _ZoomButtonTitle; }
            set { _ZoomButtonTitle = value; OnPropertyChanged("ZoomButtonTitle"); }
        }

        public string _DataTooltipButtonTitle;
        public string DataTooltipButtonTitle
        {
            get { return _DataTooltipButtonTitle; }
            set { _DataTooltipButtonTitle = value; OnPropertyChanged("DataTooltipButtonTitle"); }
        }

        private string _ZoomButtonBackground;
        public string ZoomButtonBackground
        {
            get { return _ZoomButtonBackground; }
            set { _ZoomButtonBackground = value; OnPropertyChanged("ZoomButtonBackground"); }
        }

        public string _DataTooltipButtonBackground;
        public string DataTooltipButtonBackground
        {
            get { return _DataTooltipButtonBackground; }
            set { _DataTooltipButtonBackground = value; OnPropertyChanged("DataTooltipButtonBackground"); }
        }
    }
}
