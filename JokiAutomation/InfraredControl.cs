using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
//using System.Threading.Tasks;
using System.Windows.Forms;

namespace JokiAutomation
{
    /// <summary>
    /// Manages infrared control commands for AV equipment via Raspberry Pi
    /// Handles execution and teaching of IR sequences for various view modes
    /// </summary>
    internal class InfraredControl : IDisposable
    {
        /// <summary>
        /// Initialize components of infrared interface
        /// Sets up Raspberry Pi connection and test timer
        /// </summary>
        /// <param name="winForm">Parent form for logging and UI updates</param>
        public void InitIR(Form1 winForm)
        {
            _rasPi = new RasPi();
            _rasPi.initRasPi(winForm);
            _IRForm = winForm;
            this.IRTesttimer.Interval = TEST_INTERVAL_MS;
            this.IRTesttimer.Tick += new System.EventHandler(this.IRTesttimer_Elapsed);
        }

        /// <summary>
        /// Execute a predefined infrared command sequence
        /// Sends command to Raspberry Pi to trigger IR transmission
        /// </summary>
        /// <param name="ID">Command ID to execute (see IR_* constants)</param>
        /// <exception cref="InvalidOperationException">Thrown when infrared control is not initialized</exception>
        public void ExecuteIR(int ID) 
        {
            if (_rasPi == null)
            {
                throw new InvalidOperationException("Infrared control not initialized. Call InitIR() first.");
            }
            _rasPi.rasPiExecute(IR_EXECUTE, ID);
        }

        /// <summary>
        /// Teach/program a new infrared sequence
        /// Puts Raspberry Pi into learning mode to record new IR command
        /// </summary>
        /// <param name="ID">Sequence ID to teach/program (0-based index)</param>
        public void TeachIR(int ID)
        {
            _rasPi.rasPiExecute(IR_TEACH, ID);
        }

        /// <summary>
        /// Start or stop IR sequencer test mode
        /// Cycles through different view modes for testing purposes
        /// </summary>
        /// <param name="active">True to start test sequence, false to stop</param>
        public void IRTest(bool active)
        {
            if (active)
            {
                _IRtestActive = 1;
                IRTesttimer.Start();
            }
            else
            {
                IRTesttimer.Stop();
                _IRtestActive = 0;
            }
        }

        /// <summary>
        /// IR test timer elapsed event handler
        /// Cycles through different view modes in sequence for testing
        /// </summary>
        /// <param name="sender">Event sender</param>
        /// <param name="e">Event arguments</param>
        private void IRTesttimer_Elapsed(object sender, System.EventArgs e)
        {
            switch(_IRtestActive)
            {
                case 1:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_PPP_VIEW);
                    _IRtestActive++;
                break;
                case 2:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_GOPRO_VIEW);
                    _IRtestActive++;
                break;
                case 3:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_POSCAM_VIEW);
                    _IRtestActive++;
                break;
                case 4:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_PREACHER_VIEW);
                    _IRtestActive++;
                break;
                case 5:
                    _rasPi.rasPiExecute(IR_SEQUENCE, IR_PRAYER_VIEW);
                    _IRtestActive = 1;
                break;
            }
        }

        /// <summary>
        /// Release all resources used by the InfraredControl
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
                // RasPi does not implement IDisposable, so no disposal needed
                if (IRTesttimer != null)
                {
                    IRTesttimer.Tick -= IRTesttimer_Elapsed;
                    IRTesttimer.Dispose();
                    IRTesttimer = null;
                }
            }
        }

        private RasPi _rasPi;
        private static Form1 _IRForm;
        
        /// <summary>Test sequence timer (6 second intervals)</summary>
        private System.Windows.Forms.Timer IRTesttimer = new System.Windows.Forms.Timer();
        
        /// <summary>IR sequencer test step (0 = off, 1-5 = test steps)</summary>
        private int _IRtestActive = 0;
        
        // Private command constants
        private const int IR_EXECUTE = 10;  // Execute IR command
        private const int IR_TEACH = 11;    // Teach/program IR command
        
        // Public command constants
        /// <summary>IR sequence command type</summary>
        public const int IR_SEQUENCE = 50;
        
        /// <summary>Pause sequence command</summary>
        public const int IR_PAUSE = 1;
        
        /// <summary>Timer sequence command</summary>
        public const int IR_TIMER = 2;
        
        /// <summary>PowerPoint view with Band audio profile</summary>
        public const int IR_PPP_VIEW = 3;
        
        /// <summary>GoPro action cam view</summary>
        public const int IR_GOPRO_VIEW = 4;
        
        /// <summary>Camcorder with position control view</summary>
        public const int IR_POSCAM_VIEW = 5;
        
        /// <summary>Camcorder 2 (preacher) control view</summary>
        public const int IR_PREACHER_VIEW = 6;
        
        /// <summary>Combination: PowerPoint view with GoPro action cam and worship audio profile</summary>
        public const int IR_PRAYER_VIEW = 7;
        
        /// <summary>Combination: PowerPoint view with GoPro action cam and preaching audio profile</summary>
        public const int IR_READER_VIEW = 8;
        
        /// <summary>Combination: PowerPoint view with GoPro action cam and Band audio profile</summary>
        public const int IR_SONG_VIEW = 9;
        
        /// <summary>Reset audio to summary signal and IR to laptop view</summary>
        public const int IR_RESET = 11;
        
        /// <summary>Switch HDMI and audio to laptop</summary>
        public const int IR_LIVE_VIDEO = 12;
        
        /// <summary>Switch beamer to HDMI 2 (live stream view)</summary>
        public const int IR_BEAMER_TOGGLE = 13;
        
        /// <summary>Switch beamer to analog input (video from CD)</summary>
        public const int IR_BEAMER_ANALOG = 14;
        
        /// <summary>Shutdown sequence: turn off beamer, HDMI switch, backup recorder, and shut down Raspberry Pi</summary>
        public const int IR_SHUTDOWN = 15;
        
        /// <summary>Mute/unmute beamer</summary>
        public const int IR_BEAMER_MUTE = 16;
        
        /// <summary>Stop backup recorder</summary>
        public const int IR_STOP_BACKUP = 17;
        
        /// <summary>Start backup recorder</summary>
        public const int IR_START_BACKUP = 18;
        
        /// <summary>Switch on beamer</summary>
        public const int IR_BEAMER_ON = 19;
        
        /// <summary>PowerPoint view with worship audio profile</summary>
        public const int IR_TEXT_VIEW = 20;
        
        /// <summary>Toggle backup recorder on/off</summary>
        public const int IR_SWITCH_BACKUP = 21;
        
        /// <summary>Test interval in milliseconds (6 seconds)</summary>
        private const int TEST_INTERVAL_MS = 6000;
    }
}
