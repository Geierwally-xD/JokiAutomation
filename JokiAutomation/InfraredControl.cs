using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;


namespace JokiAutomation
{
    class InfraredControl
    {
        //initialize components of infrared interface
        public void initIR(Form1 winForm)
        {
            _rasPi.initRasPi(winForm);
        }

        //executes infrared sequence
        public void executeIR (int ID) 
        {
            _rasPi.rasPiExecute(IR_EXECUTE,ID);
        }

        //teach infrared sequence
        public void teachIR (int ID)
        {
            _rasPi.rasPiExecute(IR_TEACH, ID);
        }

        private RasPi _rasPi = new RasPi(); // Raspberry Pi functionality
        private const int IR_EXECUTE = 10;// IR control command
        private const int IR_TEACH = 11;  // IR teach command
    }
}
