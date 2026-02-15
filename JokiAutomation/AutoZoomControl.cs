using System;
using System.Drawing;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace JokiAutomation
{
    /// <summary>
    /// Automatic zoom control for camcorder
    /// Manages zoom positions, calibration, and servo movements via Raspberry Pi
    /// </summary>
    class AutoZoomControl : IDisposable
    {
        /// <summary>
        /// Auto zoom configuration data structure
        /// Contains servo position and timing parameters
        /// </summary>
        public struct AZ_CONFIG
        {
            /// <summary>16-bit value for servo middle position in microseconds</summary>
            public ushort ServoMiddle;

            /// <summary>16-bit value for servo reference move position offset in microseconds</summary>
            public ushort ServoReference;

            /// <summary>16-bit value for servo control move position offset in microseconds</summary>
            public ushort ServoControl;

            /// <summary>16-bit value for control offset in microseconds</summary>
            public ushort ServoControlN;

            /// <summary>32-bit value for calibration time in microseconds</summary>
            public uint CalibrationTime;
        }

        /// <summary>
        /// Initialize auto zoom control components and interface
        /// Sets default servo positions and calibration parameters
        /// </summary>
        /// <param name="winForm">Parent form for logging and UI updates</param>
        public void initAZ(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
            _AZForm = winForm;
            _AZ_Config.ServoControl = 10;         // Servo control position in microseconds
            _AZ_Config.ServoMiddle = 1500;        // Servo middle position in microseconds
            _AZ_Config.ServoReference = 200;      // Servo reference move position in microseconds
            _AZ_Config.CalibrationTime = 1000000; // Default calibration time 1 second
        }

        /// <summary>
        /// Open zoom input dialog window
        /// Shows dialog for manual zoom value adjustment
        /// </summary>
        /// <param name="winForm">Parent form for dialog initialization</param>
        public void openDialog(Form1 winForm)
        {
            Autozoom zoomForm = new Autozoom(winForm);
            _rasPi.initRasPi(winForm);
            int width = Screen.PrimaryScreen.WorkingArea.Width - zoomForm.Width;
            int height = Screen.PrimaryScreen.WorkingArea.Height - zoomForm.Height;
            zoomForm.TopLevel = true;
            zoomForm.Location = new Point(width, height);
            readZoomValues();
            zoomForm.zoomValue.Text = Convert.ToString(_AZ_ZoomValue[TEMP_ZOOM_INDEX], 10);
            zoomForm.ShowDialog();
        }

        /// <summary>
        /// Move zoom camcorder to specified position
        /// Uses temporary zoom index for position value
        /// </summary>
        public void moveToPos()
        {
            _rasPi.rasPiExecute(AZ_MOVE, _AZ_ZoomValue[TEMP_ZOOM_INDEX]);
        }

        /// <summary>
        /// Set zoom value for temporary storage
        /// </summary>
        /// <param name="value">Zoom value (0-100)</param>
        public void setZoomValue(byte value)
        {
            _AZ_ZoomValue[TEMP_ZOOM_INDEX] = value;
        }

        /// <summary>
        /// Convert configuration structure to byte array for binary file storage
        /// Uses marshalling to convert struct to binary format
        /// </summary>
        /// <typeparam name="T">Type of structure to convert</typeparam>
        /// <param name="obj">Structure object to convert</param>
        /// <returns>Byte array representation of structure</returns>
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

        /// <summary>
        /// Convert byte array from loaded configuration binary file to configuration structure
        /// Uses marshalling to convert binary format back to struct
        /// </summary>
        /// <typeparam name="T">Type of structure to convert to</typeparam>
        /// <param name="bytearray">Byte array to convert</param>
        /// <param name="obj">Reference to structure object to populate</param>
        public void ByteArrayToStructure<T>(byte[] bytearray, ref T obj)
        {
            int len = Marshal.SizeOf((T)obj);
            IntPtr i = Marshal.AllocHGlobal(len);
            Marshal.Copy(bytearray, 0, i, len);
            obj = (T)Marshal.PtrToStructure(i, typeof(T));
            Marshal.FreeHGlobal(i);
        }

        /// <summary>
        /// Write zoom configuration to Raspberry Pi
        /// Uploads configuration to /home/pi/JokiAutomation/config/zoomconfig.bin
        /// </summary>
        public void writeZoomConfiguration()
        {
            Byte[] cofigData = StructureToByteArray(_AZ_Config);
            _rasPi.UploadBinary(cofigData, "/home/pi/JokiAutomation/config/zoomconfig.bin");
        }

        /// <summary>
        /// Read zoom configuration from Raspberry Pi
        /// Downloads configuration from /home/pi/JokiAutomation/config/zoomconfig.bin
        /// </summary>
        public void readZoomConfiguration()
        {
            int size = System.Runtime.InteropServices.Marshal.SizeOf(typeof(AZ_CONFIG));
            Byte[] cofigData = _rasPi.DownloadBinary("/home/pi/JokiAutomation/config/zoomconfig.bin", size);
            ByteArrayToStructure(cofigData, ref _AZ_Config);
        }

        /// <summary>
        /// Write zoom values array to Raspberry Pi
        /// Uploads zoom values to /home/pi/JokiAutomation/config/zoomValues.bin
        /// </summary>
        public void writeZoomValues()
        {
            _rasPi.UploadBinary(_AZ_ZoomValue, "/home/pi/JokiAutomation/config/zoomValues.bin");
        }

        /// <summary>
        /// Read zoom values array from Raspberry Pi
        /// Downloads 21 zoom values from /home/pi/JokiAutomation/config/zoomValues.bin
        /// </summary>
        public void readZoomValues()
        {
            _AZ_ZoomValue = _rasPi.DownloadBinary("/home/pi/JokiAutomation/config/zoomValues.bin", 21);
        }

        /// <summary>
        /// Start zoom calibration procedure
        /// Initiates automatic calibration routine on Raspberry Pi
        /// </summary>
        public void calibrate()
        {
            _rasPi.rasPiExecute(AZ_CALIB, 0);
        }

        /// <summary>
        /// Test autozoom by moving to first five positions in a loop
        /// Used for testing and verification of zoom positions
        /// </summary>
        public void test()
        {
            _rasPi.rasPiExecute(AZ_TEST, 0);
        }

        /// <summary>
        /// Move servo to next position based on last focus position
        /// Toggles between left and right control/reference positions
        /// </summary>
        public void servoMove()
        {
            if ((_AZLastServoPosition == (byte)AZ_SERVOPOS.AZ_CON_RIGHT)
               || (_AZLastServoPosition == (byte)AZ_SERVOPOS.AZ_REF_RIGHT))
            {
                _AZLastServoPosition++;
            }
            else
            {
                _AZLastServoPosition--;
            }
            _rasPi.rasPiExecute(AZ_SERVO_MOVE, (int)_AZLastServoPosition);
        }

        /// <summary>
        /// Move servo to middle (neutral) position
        /// Resets servo to default center position
        /// </summary>
        public void servoMiddle()
        {
            _rasPi.rasPiExecute(AZ_SERVO_MOVE, (int)AZ_SERVOPOS.AZ_MIDDLE);
        }

        /// <summary>
        /// Get zoom value at specified index with bounds checking
        /// </summary>
        /// <param name="index">Index of zoom value to retrieve (0-20)</param>
        /// <returns>Zoom value at specified index</returns>
        /// <exception cref="ArgumentOutOfRangeException">Thrown when index is out of valid range</exception>
        public byte GetZoomValue(int index)
        {
            if (index < 0 || index >= _AZ_ZoomValue.Length)
                throw new ArgumentOutOfRangeException(nameof(index));
            return _AZ_ZoomValue[index];
        }

        /// <summary>
        /// Release all resources used by the AutoZoomControl
        /// </summary>
        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        /// <summary>
        /// Release unmanaged resources and optionally release managed resources
        /// </summary>
        /// <param name="disposing">True to release both managed and unmanaged resources; false to release only unmanaged resources</param>
        protected virtual void Dispose(bool disposing)
        {
            if (disposing)
            {
                // AutoZoomControl uses RasPi which is managed externally
                // No own resources to clean up
            }
        }

        /// <summary>Auto zoom configuration structure instance</summary>
        public AZ_CONFIG _AZ_Config = new AZ_CONFIG();

        /// <summary>Array of 21 zoom values (positions 0-19 + temporary position 20)</summary>
        public byte[] _AZ_ZoomValue = new byte[] { 100, 70, 40, 10, 0, 30, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0, 0 };

        /// <summary>
        /// Servo position enumeration
        /// Defines possible servo positions for zoom control
        /// </summary>
        public enum AZ_SERVOPOS
        {
            /// <summary>Middle (neutral) position</summary>
            AZ_MIDDLE = 0,
            /// <summary>Reference position right</summary>
            AZ_REF_RIGHT = 1,
            /// <summary>Reference position left</summary>
            AZ_REF_LEFT = 2,
            /// <summary>Control position right</summary>
            AZ_CON_RIGHT = 3,
            /// <summary>Control position left</summary>
            AZ_CON_LEFT = 4
        }

        private const int AZ_MOVE = 60;            // Autozoom move to position command
        private const int AZ_CALIB = 61;           // Calibrate autozoom command
        private const int AZ_TEST = 62;            // Autozoom test positions command
        private const int AZ_SERVO_MOVE = 63;      // Autozoom move servo to position command
        private const int TEMP_ZOOM_INDEX = 20;    // Temporary zoom value storage index

        /// <summary>Last servo position used (for toggling movements)</summary>
        public byte _AZLastServoPosition = (byte)AZ_SERVOPOS.AZ_CON_RIGHT;

        private static RasPi _rasPi = new RasPi();
        private static Form1 _AZForm;
    }
}
