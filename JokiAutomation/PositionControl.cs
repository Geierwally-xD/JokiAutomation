using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows;
//using System.Threading.Tasks;

namespace JokiAutomation
{
    class PositionControl
    {
        //initialize components of infrared interface
        public void initPC(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
            _PCForm = winForm;
            readConfigFile();
        }

        // read all position descritions from config file into listbox
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
        // write all position descriptions from config file into listbox
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

        //move to position
        public void movePC(int ID)
        {
            _rasPi.rasPiExecute(PC_MOVE, ID);
        }

        //teach position
        public void teachPC(int ID)
        {
            _rasPi.rasPiExecute(PC_TEACH, ID);
        }

        //calibrate magnetoscope sensor
        public void calibratePC(int ID)
        {
            _rasPi.PuttyRequestRasPi(PC_CALIBRATE, ID);
        }

        //teach camcorder position
        public void teachPos(int ID)
        {
            _rasPi.rasPiExecute(PC_TEACH, ID);
            if (ID < 20)
            {
                string positionText = _PCForm.listBoxCamPosControl.Items[ID].ToString();
                _PCForm._logDat.sendInfoMessage("Teach Camcorderposition, Bezeichnung anpassen und mit <ENTER> bestätigen\n" + positionText);
                _PCForm.richTextBox3.Select(_PCForm.richTextBox3.Text.Length, 0);
                _PCForm.richTextBox3.ScrollToCaret();
            }
        }

        //move to camcorder position
        public void moveToPos(int ID)
        {
            _rasPi.rasPiExecute(PC_MOVE, ID);
        }

        /* switch sequence for position control
         * param camera contains uppercase letters A, G, K, L for camcorder view on movement Altar, GoPro, Kanzel, Laptop
         * param audio contains uppercase letters D, G, P, T, B for audio profile Diashow, Gottesdienst, Predigt, Text, Band
         */
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
                            switch (cam_audioSet[0])        // code view during position movement 
                            {
                                case 'L':                  // laptop PPP view  (0)
                                    positionID |= 0x0000;
                                break;
                                case 'G':                  // GoPro action cam view (1)
                                    positionID |= 0x0100;
                                break;
                                default:
                                case 'A':                  // camcorder with position control view (2)
                                    positionID |= 0x0200;
                                break;
                                case 'K':                  // camcorder preacher view (3)
                                    positionID |= 0x0300;
                                break;
                            }
                            switch (cam_audioSet[1])       // code audio profile
                            {
                                case 'D':                  // audio profile slider show (0)
                                    positionID |= 0x0000;
                                break;
                                default:
                                case 'G':                  // audio profile worship (1)
                                    positionID |= 0x1000;
                                    break;
                                case 'P':                  // audio profile preacher (2)
                                    positionID |= 0x2000;
                                break;
                                case 'T':                  // audio profile text (3)
                                    positionID |= 0x3000;
                                break;
                                case 'B':                  // audio profile band (4)
                                    positionID |= 0x4000;
                                break;

                            }
                        }
                        _rasPi.rasPiExecute(PC_SEQUENCE, positionID); // execute sequence command
                        positionfound = true;
                        break;
                    }

                }
                if (!positionfound)
                {
                    throw new System.ArgumentException(" ", "Positions - Information ungültig");
                }
            }
            catch(Exception e)
            {
                _PCForm._logDat.sendInfoMessage("JokiAutomation\nFormatfehler in Kommandozeilenaufruf PositionControl \n" + e.Message);
            }
        }


        //move button function for position controf
        public void moveButtonPressed(int ID)
        {
            _rasPi.rasPiExecute(PC_MOVE_BUTTON, ID);
        }

        // position control test program, moves to top five positions in list 
        public void testProgram()
        {
            _rasPi.rasPiExecute(PC_TEST_PROGRAM,0);
        }

        private const int PC_MOVE = 40;            // Position control move to position
        private const int PC_TEACH = 41;           // Position control teach position
        private const int PC_CALIBRATE = 42;       // Position control calibration
        private const int PC_MOVE_BUTTON = 43;     // move button pressed ID 1 up 2 down 3 left 4 right 5 released
        private const int PC_TEST_PROGRAM = 44;    // position control test program, moves to top five positions in list 
        private const int PC_SEQUENCE = 52;        // Position control sequence
        public const int PC_BUTTON_UP = 1;        // move up button pressed
        public const int PC_BUTTON_DOWN = 2;      // move down button pressed
        public const int PC_BUTTON_LEFT = 3;      // move left button pressed
        public const int PC_BUTTON_RIGHT = 4;     // move right button pressed
        public const int PC_BUTTON_RELEASED = 5;  // move button released
        private static RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
        private string _JokiAutomationPath = Environment.GetEnvironmentVariable("JokiAutomation");
        private static Form1 _PCForm;
    }
}
