using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
//using System.Threading.Tasks;

namespace JokiAutomation
{
    /// <summary>
    /// Position control manager for motorized camcorder positioning
    /// Handles teaching, moving, and calibration of camcorder positions via Raspberry Pi
    /// </summary>
    class PositionControl : IDisposable
    {
        /// <summary>
        /// Initialize position control components and interface
        /// Reads position configuration from file and sets up Raspberry Pi connection
        /// </summary>
        /// <param name="winForm">Parent form for logging and UI updates</param>
        public void initPC(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
            _PCForm = winForm;
            readConfigFile();
        }

        /// <summary>
        /// Read all position descriptions from configuration file into listbox
        /// Loads PositionControl.cfg and populates the position listbox
        /// </summary>
        public void readConfigFile()
        {
            try
            { 
                if (File.Exists(_JokiAutomationPath + "PositionControl.cfg"))
                {
                    _PCForm.listBoxCamPosControl.Items.Clear();
                    string line;
                    var file = new System.IO.StreamReader(_JokiAutomationPath + "PositionControl.cfg");
                    while ((line = file.ReadLine()) != null)
                    {
                        _PCForm.listBoxCamPosControl.Items.Add(line);
                    }
                    file.Close();
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage(e.Message);
            }
        }

        /// <summary>
        /// Write all position descriptions from listbox to configuration file
        /// Saves current position names to PositionControl.cfg
        /// </summary>
        public void writeConfigFile()
        {
            try
            {
                using (StreamWriter sw = new StreamWriter(_JokiAutomationPath + "PositionControl.cfg"))
                {
                    foreach (var item in _PCForm.listBoxCamPosControl.Items)
                    {
                        sw.WriteLine(item);
                    }
                    sw.Close();
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage(e.Message);
            }
        }

        /// <summary>
        /// Move camcorder to specified position
        /// </summary>
        /// <param name="ID">Position ID to move to (0-based index)</param>
        public void movePC(int ID)
        {
            _rasPi.rasPiExecute(PC_MOVE, ID);
        }

        /// <summary>
        /// Teach/program a camcorder position
        /// Stores current camcorder position as specified ID
        /// </summary>
        /// <param name="ID">Position ID to teach/program (0-based index)</param>
        public void teachPC(int ID)
        {
            _rasPi.rasPiExecute(PC_TEACH, ID);
        }

        /// <summary>
        /// Calibrate magnetometer sensor
        /// Initiates calibration procedure for position sensing
        /// </summary>
        /// <param name="ID">Calibration mode ID (1=magnetometer, 2=gyroscope)</param>
        public void calibratePC(int ID)
        {
            _rasPi.PuttyRequestRasPi(PC_CALIBRATE, ID);
        }

        /// <summary>
        /// Teach camcorder position with user feedback
        /// Prompts user to adjust position description after teaching
        /// </summary>
        /// <param name="ID">Position ID to teach (0-20, where 20 is null position)</param>
        public void teachPos(int ID)
        {
            _rasPi.rasPiExecute(PC_TEACH, ID);
            if (ID < 20)
            {
                string positionText = _PCForm.listBoxCamPosControl.Items[ID].ToString();
                _PCForm._logDat.sendInfoMessage("Teach camcorder position, adjust description and confirm with <ENTER>\n" + positionText);
                _PCForm.richTextBox3.Select(_PCForm.richTextBox3.Text.Length, 0);
                _PCForm.richTextBox3.ScrollToCaret();
            }
        }

        /// <summary>
        /// Move to specified camcorder position
        /// </summary>
        /// <param name="ID">Position ID to move to (0-based index)</param>
        public void moveToPos(int ID)
        {
            _rasPi.rasPiExecute(PC_MOVE, ID);
        }

        /// <summary>
        /// Execute switching sequence for position control with camera view and audio profile
        /// Encodes camera view and audio profile into position ID for coordinated switching
        /// </summary>
        /// <param name="position">Position name to move to</param>
        /// <param name="cam_audio">Two-character code: first char=camera view (A/G/K/L), second char=audio profile (D/G/P/T/B)
        /// Camera views: A=Altar (main camcorder), G=GoPro, K=Kanzel (preacher), L=Laptop
        /// Audio profiles: D=Diashow (slideshow), G=Gottesdienst (worship), P=Predigt (sermon), T=Text, B=Band</param>
        /// <exception cref="ArgumentException">Thrown when position is not found or cam_audio format is invalid</exception>
        public void sequence(string position, string cam_audio)
        {
            try
            {
                int positionID = 0;
                bool positionfound = false;
                char[] cam_audioSet = cam_audio.ToArray();
                for (; positionID < _PCForm.listBoxCamPosControl.Items.Count; positionID++)
                {
                    if (position == _PCForm.listBoxCamPosControl.Items[positionID].ToString())
                    {
                        if (cam_audioSet.Length == 2)
                        {
                            switch (cam_audioSet[0])        // Code view during position movement 
                            {
                                case 'L':                  // Laptop PowerPoint view (0)
                                    positionID |= 0x0000;
                                break;
                                case 'G':                  // GoPro action cam view (1)
                                    positionID |= 0x0100;
                                break;
                                default:
                                case 'A':                  // Camcorder with position control view (2)
                                    positionID |= 0x0200;
                                break;
                                case 'K':                  // Camcorder preacher view (3)
                                    positionID |= 0x0300;
                                break;
                            }
                            switch (cam_audioSet[1])       // Code audio profile
                            {
                                case 'D':                  // Audio profile slideshow (0)
                                    positionID |= 0x0000;
                                break;
                                default:
                                case 'G':                  // Audio profile worship (1)
                                    positionID |= 0x1000;
                                    break;
                                case 'P':                  // Audio profile sermon (2)
                                    positionID |= 0x2000;
                                break;
                                case 'T':                  // Audio profile text (3)
                                    positionID |= 0x3000;
                                break;
                                case 'B':                  // Audio profile band (4)
                                    positionID |= 0x4000;
                                break;

                            }
                        }
                        _rasPi.rasPiExecute(PC_SEQUENCE, positionID); // Execute sequence command
                        positionfound = true;
                        break;
                    }

                }
                if (!positionfound)
                {
                    throw new System.ArgumentException(" ", "Position information invalid");
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage("JokiAutomation\nFormat error in command line call PositionControl\n" + e.Message);
            }
        }

        /// <summary>
        /// Handle move button press for manual position control
        /// Allows manual joystick-style movement control
        /// </summary>
        /// <param name="ID">Button ID (1=up, 2=down, 3=left, 4=right, 5=released)</param>
        public void moveButtonPressed(int ID)
        {
            _rasPi.rasPiExecute(PC_MOVE_BUTTON, ID);
        }

        /// <summary>
        /// Execute position control test program
        /// Moves camcorder through top five positions in list for testing
        /// </summary>
        /// <param name="ID">Test program ID (0=basic test, 1=advanced test with view switching)</param>
        public void testProgram(int ID)
        {
            _rasPi.rasPiExecute(PC_TEST_PROGRAM, ID);
        }

        /// <summary>
        /// Release all resources used by the PositionControl
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
        public virtual void Dispose(bool disposing)
        {
            if (!disposed)
            {
                if (disposing)
                {
                    // Dispose managed resources here
                    // RasPi does not implement IDisposable, so no disposal needed
                }

                // Free unmanaged resources here if any

                disposed = true;
            }
        }

        // Private command constants
        private const int PC_MOVE = 40;            // Position control move to position
        private const int PC_TEACH = 41;           // Position control teach position
        private const int PC_CALIBRATE = 42;       // Position control calibration
        private const int PC_MOVE_BUTTON = 43;     // Move button pressed (ID: 1=up, 2=down, 3=left, 4=right, 5=released)
        private const int PC_TEST_PROGRAM = 44;    // Position control test program (moves to top five positions)
        private const int PC_SEQUENCE = 52;        // Position control sequence with camera/audio switching

        // Public button constants
        /// <summary>Move up button pressed</summary>
        public const int PC_BUTTON_UP = 1;
        
        /// <summary>Move down button pressed</summary>
        public const int PC_BUTTON_DOWN = 2;
        
        /// <summary>Move left button pressed</summary>
        public const int PC_BUTTON_LEFT = 3;
        
        /// <summary>Move right button pressed</summary>
        public const int PC_BUTTON_RIGHT = 4;
        
        /// <summary>Move button released</summary>
        public const int PC_BUTTON_RELEASED = 5;

        private static RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
        private string _JokiAutomationPath = Environment.GetEnvironmentVariable("JokiAutomation");
        private static Form1 _PCForm;
        private bool disposed = false;
    }
}
