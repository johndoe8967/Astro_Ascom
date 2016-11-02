// This implements a console application that can be used to test an ASCOM driver
//

// This is used to define code in the template that is specific to one class implementation
// unused code can be deleted and this definition removed.

#define Telescope
// remove this to bypass the code that uses the chooser to select the driver
#define UseChooser

using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace ASCOM
{
    class Program
    {
        static ASCOM.DriverAccess.Telescope device;
        static System.Threading.Timer MyTimer;


        enum States { init, connect, connected, test, disconnect, disconnected };
        static States actState;

        static void CheckCondition(object state) {
            
            switch (actState) {
                case States.init:
            device.SiteLatitude = 48;
            device.SiteLongitude = 14.28;
                    device.Connected = true;
                    actState = States.connect;
                    Console.WriteLine("Init -> Connect");
                    break;
                case States.connect:
                    if (device.Declination != 0) {
                        actState = States.test;
                        Console.WriteLine("Connect->test");
                    }
                    break;
                case States.test:
                    if (device.RightAscension != 0) {
                        actState = States.disconnect;
                        Console.WriteLine("test->disconnect");
                    }
                    break;
                case States.disconnect:
                    device.Connected = false;
                    actState = States.disconnected;
                    Console.WriteLine("disconnect->disconnected");
                    break;
                case States.disconnected:
                    // stop
                    MyTimer.Dispose();
                    Console.WriteLine("dispose");
                    break;
                default:
                    Console.WriteLine("unknown");
                    break;
            }
        }

        static void Main(string[] args)
        {
            // Uncomment the code that's required
#if UseChooser
            // choose the device
            string id = ASCOM.DriverAccess.Telescope.Choose("");
            if (string.IsNullOrEmpty(id))
                return;
            // create this device
            device = new ASCOM.DriverAccess.Telescope(id);
#else
            // this can be replaced by this code, it avoids the chooser and creates the driver class directly.
            ASCOM.DriverAccess.Telescope device = new ASCOM.DriverAccess.Telescope("ASCOM.funky1.Telescope");
#endif
            // now run some tests, adding code to your driver so that the tests will pass.
            // these first tests are common to all drivers.
            Console.WriteLine("name " + device.Name);
            Console.WriteLine("description " + device.Description);
            Console.WriteLine("DriverInfo " + device.DriverInfo);
            Console.WriteLine("driverVersion " + device.DriverVersion);

            device.Connected = true;


            // TODO add more code to test the driver.
            //            MyTimer = new System.Threading.Timer(CheckCondition, null, 1000, 1000);

            Console.WriteLine("Press Enter to finish");
            Console.ReadLine();
        }
    }
}
