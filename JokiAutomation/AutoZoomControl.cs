using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;
using System.Drawing;
using System.Runtime.InteropServices;

namespace JokiAutomation
{
    class AutoZoomControl
    {

        // auto zoom configuration data type
        public struct AZ_CONFIG
        {
            public ushort ServoMiddle;    // 16 bit value servo middle position
            public ushort ServoReference; // 16 bit value servo reference move position
            public ushort ServoControl;   // 16 bit value servo control move position
            public ushort ServoControlN;  // 16 bit value control offset
            public uint CalibrationTime;  // 32 bit value calibration time
        }

        //initialize components of infrared interface
        public void initAZ(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
            _AZForm = winForm;
            _AZ_Config.ServoControl = 10;         // servo control position in microseconds
            _AZ_Config.ServoMiddle = 1500;        // servo middle position in microseconds
            _AZ_Config.ServoReference = 200;      // servo reference move position in microseconds
            _AZ_Config.CalibrationTime = 1000000; // default calibration time 1s

          //  _AZForm.displayZoomConfig();          // write zoom configuration into display
        }

        // open zoom input dialog
        public void openDialog(Form1 winForm)
        {
            Autozoom zoomForm = new Autozoom(winForm);
            _rasPi.initRasPi(winForm);
            int width = Screen.PrimaryScreen.WorkingArea.Width - zoomForm.Width;
            int height = Screen.PrimaryScreen.WorkingArea.Height - zoomForm.Height;
            zoomForm.TopLevel = true;
            zoomForm.Location = new Point(width, height);
            readZoomValues();
            zoomForm.zoomValue.Text = Convert.ToString(_AZ_ZoomValue[20], 10);
            zoomForm.ShowDialog();
        }

        // move zoom camcorder
        public void moveToPos()
        {
            _rasPi.rasPiExecute(AZ_MOVE, _AZ_ZoomValue[20]);
        }

        // set zoom value
        public void setZoomValue(byte value)
        {
            _AZ_ZoomValue[20] = value;
        }

        // converts configuration struct into byte array for config binary file
        public byte[] StructureToByteArray<T>(T obj)
        {
            int len = Marshal.SizeOf(obj);
            byte[] arr = new byte[len];
            IntPtr ptr = Marshal.AllocHGlobal(len);
            Marshal.StructureToPtr(obj, ptr, false);
            Marshal.Copy(ptr, arr, 0, len);
            Marshal.FreeHGlobal(ptr);
            return arr;
        }

        // converts byte array from loaded configuration binary into config struct
        public void ByteArrayToStructure<T>(byte[] bytearray, ref T obj)
        {
            int len = Marshal.SizeOf((T)obj);
            IntPtr i = Marshal.AllocHGlobal(len);
            Marshal.Copy(bytearray, 0, i, len);
            obj = (T)Marshal.PtrToStructure(i, typeof(T));
            Marshal.FreeHGlobal(i);
        }

        // write zoom configuration to raspberry pi
        public void writeZoomConfiguration()
        {
            Byte[] cofigData = StructureToByteArray(_AZ_Config);
            _rasPi.UploadBinary(cofigData, "zoomconfig.bin");
        }

        // read zoom configuration from raspberry pi
        public void readZoomConfiguration()
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(AZ_CONFIG));
            Byte[] cofigData = _rasPi.DownloadBinary("zoomconfig.bin",size );
            ByteArrayToStructure(cofigData, ref _AZ_Config);
        }

        // write zoom values to raspberry pi
        public void writeZoomValues()
        {
            _rasPi.UploadBinary(_AZ_ZoomValue, "zoomValues.bin");
        }

        // read zoom values from raspberry pi
        public void readZoomValues()
        {
            _AZ_ZoomValue = _rasPi.DownloadBinary("zoomValues.bin", 21);
        }

        // calibrate zoom values
        public void calibrate()
        {
            _rasPi.rasPiExecute(AZ_CALIB, 0);
        }

        // move autozoom to first five positions in a loop
        public void test()
        {
            _rasPi.rasPiExecute(AZ_TEST, 0);
        }

        // move to servo position depending last focus
        public void servoMove()
        {
            if ( (_AZLastServoPosition == (byte)AZ_SERVOPOS.AZ_CON_RIGHT)
               ||(_AZLastServoPosition == (byte)AZ_SERVOPOS.AZ_REF_RIGHT))
            {
                _AZLastServoPosition++;
            }
            else 
            {
                _AZLastServoPosition--;
            }
            _rasPi.rasPiExecute(AZ_SERVO_MOVE, (int)_AZLastServoPosition);
        }

        // move servo to middle position
        public void servoMiddle()
        {
            _rasPi.rasPiExecute(AZ_SERVO_MOVE, (int)AZ_SERVOPOS.AZ_MIDDLE);
        }

        public AZ_CONFIG _AZ_Config = new AZ_CONFIG();
        public byte[] _AZ_ZoomValue = new byte[] { 100, 70, 40, 10, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };
        public enum AZ_SERVOPOS
        {
            AZ_MIDDLE     = 0,
            AZ_REF_RIGHT  = 1,
            AZ_REF_LEFT   = 2,
            AZ_CON_RIGHT  = 3,
            AZ_CON_LEFT   = 4
        }
        private const int AZ_MOVE = 60;            // autozoom move to position
        private const int AZ_CALIB = 61;           // calibrate autozoom
        private const int AZ_TEST = 62;            // autozoom test positions
        private const int AZ_SERVO_MOVE = 63;      // autozoom move servo to position
        public byte _AZLastServoPosition = (byte)AZ_SERVOPOS.AZ_CON_RIGHT;
        private static RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
        private static Form1 _AZForm;
    }
}
