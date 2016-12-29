//tabs=4
// --------------------------------------------------------------------------------
// TODO fill in this information for your driver, then remove this line!
//
// ASCOM Telescope driver for funky
//
// Description:	Lorem ipsum dolor sit amet, consetetur sadipscing elitr, sed diam 
//				nonumy eirmod tempor invidunt ut labore et dolore magna aliquyam 
//				erat, sed diam voluptua. At vero eos et accusam et justo duo 
//				dolores et ea rebum. Stet clita kasd gubergren, no sea takimata 
//				sanctus est Lorem ipsum dolor sit amet.
//
// Implements:	ASCOM Telescope interface version: <To be completed by driver developer>
// Author:		(XXX) Your N. Here <your@email.here>
//
// Edit Log:
//
// Date			Who	Vers	Description
// -----------	---	-----	-------------------------------------------------------
// dd-mmm-yyyy	XXX	6.0.0	Initial edit, created from ASCOM driver template
// --------------------------------------------------------------------------------
//


// This is used to define code in the template that is specific to one class implementation
// unused code canbe deleted and this definition removed.
#define Telescope

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using System.Runtime.InteropServices;

using ASCOM;
using ASCOM.Astrometry;
using ASCOM.Astrometry.AstroUtils;
using ASCOM.Utilities;
using ASCOM.DeviceInterface;
using System.Globalization;
using System.Collections;
using System.Net.WebSockets;
using System.Threading;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace ASCOM.funky {
    //
    // Your driver's DeviceID is ASCOM.funky.Telescope
    //
    // The Guid attribute sets the CLSID for ASCOM.funky.Telescope
    // The ClassInterface/None addribute prevents an empty interface called
    // _funky from being created and used as the [default] interface
    //
    // TODO Replace the not implemented exceptions with code to implement the function or
    // throw the appropriate ASCOM exception.
    //

    /// <summary>
    /// ASCOM Telescope Driver for funky.
    /// </summary>
    [Guid("9128cdd5-58e5-4c69-9614-f9dfd8fb8826")]
    [ClassInterface(ClassInterfaceType.None)]
    public class Telescope : ITelescopeV3 {
        /// <summary>
        /// ASCOM DeviceID (COM ProgID) for this driver.
        /// The DeviceID is used by ASCOM applications to load the driver at runtime.
        /// </summary>
        internal static string driverID = "ASCOM.funky.Telescope";
        // TODO Change the descriptive string for your driver then remove this line
        /// <summary>
        /// Driver description that displays in the ASCOM Chooser.
        /// </summary>
        private static string driverDescription = "ASCOM Telescope Driver for ASTRO_ESP.";

        internal static string hostnameProfileName = "Hostname"; // Constants used for Profile persistence
        internal static string hostnameDefault = "10.0.0.122";
        internal static string traceStateProfileName = "Trace Level";
        internal static string traceStateDefault = "false";

        internal static string hostname; // Variables to hold the currrent device configuration
        internal static bool traceState;

        /// <summary>
        /// Private variable to hold the connected state
        /// </summary>
        private bool connectedState;
        internal bool connectionEstablished = false;
        internal bool shouldConnect;

        private double rightAscension;
        private double declination;

        /// <summary>
        /// Private variable to hold an ASCOM Utilities object
        /// </summary>
        private Util utilities;

        /// <summary>
        /// Private variable to hold an ASCOM AstroUtilities object to provide the Range method
        /// </summary>
        private AstroUtils astroUtilities;

        /// <summary>
        /// Private variable to hold the trace logger object (creates a diagnostic log file with information that you specify)
        /// </summary>
        private TraceLogger tl;

        /// <summary>
        /// Private async Task to work the websocket
        /// </summary>

        public class AstroMsg {
            public string type { get; set; }
            public string msg { get; set; }
            public object value { get; set; }
        }

        public class worker {
            Telescope parent;
            ClientWebSocket ws;
            byte[] buffer;
            byte[] sendBuffer;
            ArraySegment<byte> segment;
            ArraySegment<byte> sendSegment;

            int count;
            double targetRightAscension, targetDeclination;
            bool sendModeSlew = true;
            bool sendModeSync = true;
            bool sendModeTrack = true;
            bool sendPending = false;

            public worker(Telescope newParent) {
                parent = newParent;
                ws = new ClientWebSocket();
                buffer = new byte[1024];
                sendBuffer = new byte[1024];
                segment = new ArraySegment<byte>(buffer);
                sendSegment = new ArraySegment<byte>(sendBuffer);
            }
            async void connect() {
                var uri = new Uri("ws://" + hostname + ":80/");
                var ts = new CancellationToken();
                await ws.ConnectAsync(uri, ts);
            }

            public void setTarget(double RightAscension, double Declination) {
                targetRightAscension = RightAscension;
                targetDeclination = Declination;
                sendModeSlew = true;
                sendPending = true;
            }
            public void syncTarget(double RightAscension, double Declination) {
                targetRightAscension = RightAscension;
                targetDeclination = Declination;
                sendModeSync = true;
                sendPending = true;
            }
            public void sendTracking() {
                sendModeTrack = true;
                sendPending = true;
            }


            enum MODE {GOTO=0, TRACK=1, REF=2, SYNC=4, SLEW=5 };

            /* 
             * {
            	"type": "ARRAY",
	                "msg": [{
		                "type": "JSON",
		                "msg": "MODE",
		                "value": "3"
	                }]
                }
             */

            async void send() {
                if (sendModeSlew || sendModeSync || sendModeTrack) {
                    AstroMsg data = new AstroMsg();
                    try {
                        data.type = "JSON";
                        data.msg = "mode";
                        if (sendModeSlew) {
                            data.value = (int)MODE.SLEW;
                        }
                        if (sendModeSync) {
                            data.value = (int)MODE.REF;
                        }
                        if (sendModeTrack) {
                            data.value = (int)MODE.TRACK;
                        }
                        var message = "{\"type\": \"ARRAY\",\"msg\":[";

                        message = message + JsonConvert.SerializeObject(data);
                        if (!sendModeTrack) {
                            message = message + ",";

                            data.msg = "target0";
                            data.value = targetDeclination * 2000 / 2 * 67 / 360;

                            message = message + JsonConvert.SerializeObject(data);
                            message = message + ",";

                            data.msg = "target1";
                            var positiontime = parent.SiderealTime - parent.rightAscension;
                            var temp = parent.SiderealTime - targetRightAscension;
                            if (sendModeSync) {
                                if (temp > 12) {
                                    temp = temp - 24;
                                }
                                //                            if (temp < -12) {
                                //                                temp = temp + 24;
                                //                            }
                            }
                            if (sendModeSlew) {
                                if (temp - positiontime >= 12) {
                                    temp = temp - 24;
                                }
                                //                            if (temp - positiontime <= -12) {
                                //                                temp = temp + 24;
                                //                            }
                            }

                            data.value = (temp) * (4 * 12) * 250 / 20 * 80 / 24;

                            message = message + JsonConvert.SerializeObject(data);
                        }
                        message = message + "]}";

                        sendBuffer = System.Text.Encoding.UTF8.GetBytes(message);

                    } catch {
                        Console.WriteLine("some exception?");
                    }
                    try {
                        sendSegment = new ArraySegment<byte>(sendBuffer, 0, sendBuffer.Length);
                        await ws.SendAsync(sendSegment, WebSocketMessageType.Text, true, CancellationToken.None);
                        if (sendModeSlew) {
                            sendModeSlew = false;
                        }
                        if (sendModeSync) {
                            sendModeSync = false;
                        }
                        if (sendModeTrack) {
                            sendModeTrack = false;
                        }
                    }
                    catch {
                        Console.WriteLine("some exception?");

                    }
                }
                sendPending = false;
            }
            Task<WebSocketReceiveResult> wsrestask;
            async void receive() {
                try {
                    if (wsrestask ==null || wsrestask.IsCompleted) {
                        wsrestask = ws.ReceiveAsync(segment, CancellationToken.None);
                        await wsrestask;

                        
                        if (wsrestask.Result.MessageType == WebSocketMessageType.Close) {
                            close();
                            return;
                        }

                        count = wsrestask.Result.Count;
                        while (!wsrestask.Result.EndOfMessage) {
                            if (count >= buffer.Length) {
                                await ws.CloseAsync(WebSocketCloseStatus.InvalidPayloadData, "That's too long", CancellationToken.None);
                                return;
                            }

                            segment = new ArraySegment<byte>(buffer, count, buffer.Length - count);
                            var result = await ws.ReceiveAsync(segment, CancellationToken.None);
                            count += result.Count;
                        }
                    } else {
                        Console.WriteLine("some exception?");
                    }

                } catch {
                    Console.WriteLine("some exception?");

                }
                return;
            }

            async void close() {
                try {
                    switch (ws.State) {
                        case WebSocketState.Aborted:
                            ws.Dispose();
                            ws = new ClientWebSocket();

                            break;
                        case WebSocketState.Open:
                        case WebSocketState.Connecting:
                            await ws.CloseOutputAsync(WebSocketCloseStatus.InvalidMessageType, "I don't do binary", CancellationToken.None);
                            break;
                        default:
                            break;
                    }
                } catch {
                    Console.WriteLine("some exception?");
                }
            }

            private void websocketworker() {
                if (sendPending) send();
                else {
                    receive();

                    var message = Encoding.UTF8.GetString(buffer, 0, count);
                    Console.WriteLine(">" + message);

                    try {
                        AstroMsg data = JsonConvert.DeserializeObject<AstroMsg>(message);
                        if (data != null) {
                            switch (data.msg) {
                                case "incr0":
                                    if (double.TryParse((string)data.value, out parent.declination)) {
                                        parent.declination = parent.declination / 2000 * 2 / 67 * 360;
                                        Console.WriteLine("Act: Declination" + parent.declination);
                                    } else
                                        Console.WriteLine("INCR0 String could not be parsed:" + data.value);
                                    break;
                                case "incr1":
                                    double temp = 0.0;
                                    if (double.TryParse((string)data.value, out temp)) {
                                        temp = temp / (4 * 12) / 250 * 20 / 80 * 24;
                                        parent.rightAscension = parent.SiderealTime - temp;
                                        if (parent.rightAscension > 24) {
                                            parent.rightAscension = parent.rightAscension - 24;
                                        }
                                        if (parent.rightAscension < 0) {
                                            parent.rightAscension = parent.rightAscension + 24;
                                            //parent.tl.LogMessage("Rightascension", "negative");
                                        }
                                    } else
                                        Console.WriteLine("INCR1 String could not be parsed:" + data.value);
                                    break;
                                case "mode":
                                    int value = 0;
                                    if (int.TryParse((string)data.value, out value)) {
                                        switch ((MODE)value) {
                                            case MODE.TRACK:
                                                parent.slew = false;
                                                parent.track = true;
                                                break;

                                            case MODE.SLEW:
                                            case MODE.GOTO:
                                                parent.slew = true;
                                                parent.track = false;
                                                break;

                                            default:
                                                parent.track = false;
                                                parent.slew = false;
                                                break;
                                        }
                                    } else
                                        Console.WriteLine("INCR1 String could not be parsed:" + data.value);
                                    break;
                                default:
                                    break;
                            }

                            if (parent.connectionEstablished == false) {
                                parent.tl.LogMessage("Telescope", "Connected:");
                                parent.connectionEstablished = true;
                            }

                        }
                    } catch {
                        Console.WriteLine("some exception?");
                    }
                }
            }

            public void Client() {
                while (true) {
                    if (parent.shouldConnect) {
                        if (ws.State == WebSocketState.Aborted) {
                            close();
                        }
                        if (ws.State == WebSocketState.Closed || ws.State == WebSocketState.None) {
                            connect();
                        }
                        if (ws.State == WebSocketState.Open) {
                            websocketworker();
                        }

                    } else {
                        if (parent.connectionEstablished) {
                            close();
                        } else {
                            close();
                        }
                    }
                    Thread.Sleep(10);
                }
            }
        }

        private Thread background;
        private worker clientTask1;

        /// <summary>
        /// Initializes a new instance of the <see cref="funky"/> class.
        /// Must be public for COM registration.
        /// </summary>
        public Telescope() {
            ReadProfile(); // Read device configuration from the ASCOM Profile store

            tl = new TraceLogger("", "Astro_ESP");
            tl.Enabled = traceState;
            tl.LogMessage("Telescope", "Starting initialisation");

            connectedState = false; // Initialise connected to false
            utilities = new Util(); //Initialise util object
            astroUtilities = new AstroUtils(); // Initialise astro utilities object
                                               //TODO: Implement your additional construction here
            clientTask1 = new worker(this);
            background = new Thread(new ThreadStart(clientTask1.Client));
            background.Start();
            while (!background.IsAlive) ;

            tl.LogMessage("Telescope", "Completed initialisation");
        }


        //
        // PUBLIC COM INTERFACE ITelescopeV3 IMPLEMENTATION
        //

        #region Common properties and methods.

        /// <summary>
        /// Displays the Setup Dialog form.
        /// If the user clicks the OK button to dismiss the form, then
        /// the new settings are saved, otherwise the old values are reloaded.
        /// THIS IS THE ONLY PLACE WHERE SHOWING USER INTERFACE IS ALLOWED!
        /// </summary>
        public void SetupDialog() {
            // consider only showing the setup dialog if not connected
            // or call a different dialog if connected
            if (IsConnected)
                System.Windows.Forms.MessageBox.Show("Already connected, just press OK");

            using (SetupDialogForm F = new SetupDialogForm()) {
                var result = F.ShowDialog();
                if (result == System.Windows.Forms.DialogResult.OK) {
                    WriteProfile(); // Persist device configuration values to the ASCOM Profile store
                }
            }
        }

        public ArrayList SupportedActions {
            get {
                tl.LogMessage("SupportedActions Get", "Returning empty arraylist");
                return new ArrayList();
            }
        }

        public string Action(string actionName, string actionParameters) {
            throw new ASCOM.ActionNotImplementedException("Action " + actionName + " is not implemented by this driver");
        }

        public void CommandBlind(string command, bool raw) {
            CheckConnected("CommandBlind");
            // Call CommandString and return as soon as it finishes
            this.CommandString(command, raw);
            // or
            throw new ASCOM.MethodNotImplementedException("CommandBlind");
            // DO NOT have both these sections!  One or the other
        }

        public bool CommandBool(string command, bool raw) {
            CheckConnected("CommandBool");
            string ret = CommandString(command, raw);
            // TODO decode the return string and return true or false
            // or
            throw new ASCOM.MethodNotImplementedException("CommandBool");
            // DO NOT have both these sections!  One or the other
        }

        public string CommandString(string command, bool raw) {
            CheckConnected("CommandString");
            // it's a good idea to put all the low level communication with the device here,
            // then all communication calls this function
            // you need something to ensure that only one command is in progress at a time

            throw new ASCOM.MethodNotImplementedException("CommandString");
        }

        public void Dispose() {
            // Clean up the tracelogger and util objects
            tl.Enabled = false;
            tl.Dispose();
            tl = null;
            utilities.Dispose();
            utilities = null;
            astroUtilities.Dispose();
            astroUtilities = null;
            background.Abort();
        }

        public bool Connected {
            get {
//                tl.LogMessage("Connected Get", IsConnected.ToString());
                return IsConnected;
            }
            set {
                tl.LogMessage("Connected Set", value.ToString());
                if (value == IsConnected)
                    return;

                if (value) {
                    tl.LogMessage("Connected Set", "Connecting to host " + hostname);

                    shouldConnect = true;

                    while (!connectionEstablished) {
                        Thread.Sleep(100);
                    }
                    connectedState = true;


                } else {
                    shouldConnect = false;
                    connectedState = false;
                    tl.LogMessage("Connected Set", "Disconnecting from host " + hostname);
                }
            }
        }

        public string Description {
            // TODO customise this device description
            get {
                tl.LogMessage("Description Get", driverDescription);
                return driverDescription;
            }
        }

        public string DriverInfo {
            get {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                // TODO customise this driver description
                string driverInfo = "Information about the driver itself. Version: " + String.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
                tl.LogMessage("DriverInfo Get", driverInfo);
                return driverInfo;
            }
        }

        public string DriverVersion {
            get {
                Version version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
                string driverVersion = String.Format(CultureInfo.InvariantCulture, "{0}.{1}", version.Major, version.Minor);
                tl.LogMessage("DriverVersion Get", driverVersion);
                return driverVersion;
            }
        }

        public short InterfaceVersion {
            // set by the driver wizard
            get {
                tl.LogMessage("InterfaceVersion Get", "3");
                return Convert.ToInt16("3");
            }
        }

        public string Name {
            get {
                string name = "Short driver name - please customise";
                tl.LogMessage("Name Get", name);
                return name;
            }
        }

        #endregion

        #region ITelescope Implementation
        public void AbortSlew() {
            tl.LogMessage("AbortSlew", "aborted");
            slew = false;
            clientTask1.sendTracking();
        }

        public AlignmentModes AlignmentMode {
            get {
                tl.LogMessage("AlignmentMode Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("AlignmentMode", false);
            }
        }

        public double Altitude {
            get {
                tl.LogMessage("Altitude", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("Altitude", false);
            }
        }

        public double ApertureArea {
            get {
                tl.LogMessage("ApertureArea Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("ApertureArea", false);
            }
        }

        public double ApertureDiameter {
            get {
                tl.LogMessage("ApertureDiameter Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("ApertureDiameter", false);
            }
        }

        public bool AtHome {
            get {
                tl.LogMessage("AtHome", "Get - " + false.ToString());
                return false;
            }
        }

        public bool AtPark {
            get {
                tl.LogMessage("AtPark", "Get - " + false.ToString());
                return false;
            }
        }

        public IAxisRates AxisRates(TelescopeAxes Axis) {           //TODO: EQAlign
            tl.LogMessage("AxisRates", "Get - " + Axis.ToString());
            return new AxisRates(Axis);
        }

        public double Azimuth {
            get {
                tl.LogMessage("Azimuth Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("Azimuth", false);
            }
        }

        public bool CanFindHome {
            get {
                tl.LogMessage("CanFindHome", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanMoveAxis(TelescopeAxes Axis) {               //TODO: EQAlign
            tl.LogMessage("CanMoveAxis", "Get - " + Axis.ToString());
            switch (Axis) {
                case TelescopeAxes.axisPrimary: return false;
                case TelescopeAxes.axisSecondary: return false;
                case TelescopeAxes.axisTertiary: return false;
                default: throw new InvalidValueException("CanMoveAxis", Axis.ToString(), "0 to 2");
            }
        }

        public bool CanPark {
            get {
                tl.LogMessage("CanPark", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanPulseGuide {         //TODO: EQAlign
            get {
                tl.LogMessage("CanPulseGuide", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSetDeclinationRate {
            get {
                tl.LogMessage("CanSetDeclinationRate", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSetGuideRates {      //TODO: EQAlign
            get {
                tl.LogMessage("CanSetGuideRates", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSetPark {
            get {
                tl.LogMessage("CanSetPark", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSetPierSide {
            get {
                tl.LogMessage("CanSetPierSide", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSetRightAscensionRate {
            get {
                tl.LogMessage("CanSetRightAscensionRate", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSetTracking {
            get {
                tl.LogMessage("CanSetTracking", "Get - " + true.ToString());
                return true;
            }
        }

        public bool CanSlew {
            get {
                tl.LogMessage("CanSlew", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSlewAltAz {
            get {
                tl.LogMessage("CanSlewAltAz", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSlewAltAzAsync {
            get {
                tl.LogMessage("CanSlewAltAzAsync", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanSlewAsync {
            get {
                tl.LogMessage("CanSlewAsync", "Get - " + true.ToString());
                return true;
            }
        }

        public bool CanSync {
            get {
                tl.LogMessage("CanSync", "Get - " + true.ToString());
                return true;
            }
        }

        public bool CanSyncAltAz {
            get {
                tl.LogMessage("CanSyncAltAz", "Get - " + false.ToString());
                return false;
            }
        }

        public bool CanUnpark {
            get {
                tl.LogMessage("CanUnpark", "Get - " + false.ToString());
                return false;
            }
        }

        public double Declination {
            get {
                //double declination = 0.0;
                tl.LogMessage("Declination", "Get - " + utilities.DegreesToDMS(declination, ":", ":"));
                return declination;
            }
        }

        public double DeclinationRate {
            get {
                double declination = 0.0;
                tl.LogMessage("DeclinationRate", "Get - " + declination.ToString());
                return declination;
            }
            set {
                tl.LogMessage("DeclinationRate Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("DeclinationRate", true);
            }
        }

        public PierSide DestinationSideOfPier(double RightAscension, double Declination) {
            tl.LogMessage("DestinationSideOfPier Get", "Not implemented");
            throw new ASCOM.PropertyNotImplementedException("DestinationSideOfPier", false);
        }

        public bool DoesRefraction {
            get {
                tl.LogMessage("DoesRefraction Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("DoesRefraction", false);
            }
            set {
                tl.LogMessage("DoesRefraction Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("DoesRefraction", true);
            }
        }

        public EquatorialCoordinateType EquatorialSystem {
            get {
                EquatorialCoordinateType equatorialSystem = EquatorialCoordinateType.equLocalTopocentric;
                tl.LogMessage("DeclinationRate", "Get - " + equatorialSystem.ToString());
                return equatorialSystem;
            }
        }

        public void FindHome() {
            tl.LogMessage("FindHome", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("FindHome");
        }

        public double FocalLength {
            get {
                tl.LogMessage("FocalLength Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("FocalLength", false);
            }
        }

        public double GuideRateDeclination {        //TODO: EQAlign
            get {
                tl.LogMessage("GuideRateDeclination Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("GuideRateDeclination", false);
            }
            set {
                tl.LogMessage("GuideRateDeclination Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("GuideRateDeclination", true);
            }
        }

        public double GuideRateRightAscension {     //TODO: EQAlign
            get {
                tl.LogMessage("GuideRateRightAscension Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("GuideRateRightAscension", false);
            }
            set {
                tl.LogMessage("GuideRateRightAscension Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("GuideRateRightAscension", true);
            }
        }

        public bool IsPulseGuiding {
            get {
                tl.LogMessage("IsPulseGuiding Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("IsPulseGuiding", false);
            }
        }

        public void MoveAxis(TelescopeAxes Axis, double Rate) {             //TODO: EQAlign
            tl.LogMessage("MoveAxis", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("MoveAxis");
        }

        public void Park() {
            tl.LogMessage("Park", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("Park");
        }

        public void PulseGuide(GuideDirections Direction, int Duration) {       //TODO: EQAlign
            tl.LogMessage("PulseGuide", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("PulseGuide");
        }

        public double RightAscension {
            get {
                //double rightAscension = 0.0;
                if (rightAscension < 0) {
                    rightAscension = 0.0;
                }
                tl.LogMessage("RightAscension", "Get - " + utilities.HoursToHMS(rightAscension));
                return rightAscension;
            }
        }

        public double RightAscensionRate {
            get {
                double rightAscensionRate = 0.0;
                tl.LogMessage("RightAscensionRate", "Get - " + rightAscensionRate.ToString());
                return rightAscensionRate;
            }
            set {
                tl.LogMessage("RightAscensionRate Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("RightAscensionRate", true);
            }
        }

        public void SetPark() {
            tl.LogMessage("SetPark", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("SetPark");
        }

        public PierSide SideOfPier {
            get {
                tl.LogMessage("SideOfPier Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("SideOfPier", false);
            }
            set {
                tl.LogMessage("SideOfPier Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("SideOfPier", true);
            }
        }

        public double SiderealTime {
            get {
                // get greenwich sidereal time: https://en.wikipedia.org/wiki/Sidereal_time
                //double siderealTime = (18.697374558 + 24.065709824419081 * (utilities.DateUTCToJulian(DateTime.UtcNow) - 2451545.0));

                // alternative using NOVAS 3.1
                double siderealTime = 0.0;
                using (var novas = new ASCOM.Astrometry.NOVAS.NOVAS31()) {
                    var jd = utilities.DateUTCToJulian(DateTime.UtcNow);
                    novas.SiderealTime(jd, 0, novas.DeltaT(jd),
                        ASCOM.Astrometry.GstType.GreenwichApparentSiderealTime,
                        ASCOM.Astrometry.Method.EquinoxBased,
                        ASCOM.Astrometry.Accuracy.Reduced, ref siderealTime);
                }
                // allow for the longitude
                siderealTime += SiteLongitude / 360.0 * 24.0;
                // reduce to the range 0 to 24 hours
                siderealTime = siderealTime % 24.0;
//                tl.LogMessage("SiderealTime", "Get - " + siderealTime.ToString());
                return siderealTime;
            }
        }

        public double SiteElevation {
            get {
                tl.LogMessage("SiteElevation Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("SiteElevation", false);
            }
            set {
                tl.LogMessage("SiteElevation Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("SiteElevation", true);
            }
        }


        private double latitude = 48;
        public double SiteLatitude {
            get {
                //                tl.LogMessage("SiteLatitude Get", "");
                //throw new ASCOM.PropertyNotImplementedException("SiteLatitude", false);
                return latitude;
            }
            set {
                tl.LogMessage("SiteLatitude Set", "");
                if ((value < -180) || (value > 180)) {
                    throw new ASCOM.InvalidValueException();
                }
                latitude = value;
            }
        }

        private double longitude = 14.28;
        public double SiteLongitude {
            get {
                //                tl.LogMessage("SiteLongitude Get", "");
                //throw new ASCOM.PropertyNotImplementedException("SiteLongitude", false);
                return longitude;
            }
            set {
                tl.LogMessage("SiteLongitude Set", "");
                //throw new ASCOM.PropertyNotImplementedException("SiteLongitude", true);
                if ((value < -180) || (value > 180)) {
                    throw new ASCOM.InvalidValueException();
                }
                longitude = value;
            }
        }

        public short SlewSettleTime {
            get {
                tl.LogMessage("SlewSettleTime Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("SlewSettleTime", false);
            }
            set {
                tl.LogMessage("SlewSettleTime Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("SlewSettleTime", true);
            }
        }

        public void SlewToAltAz(double Azimuth, double Altitude) {
            tl.LogMessage("SlewToAltAz", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("SlewToAltAz");
        }

        public void SlewToAltAzAsync(double Azimuth, double Altitude) {
            tl.LogMessage("SlewToAltAzAsync", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("SlewToAltAzAsync");
        }

        public void SlewToCoordinates(double RightAscension, double Declination) {
            tl.LogMessage("SlewToCoordinates", "RA:" + utilities.HoursToHMS(RightAscension) + " Dec:" + Declination.ToString());
            clientTask1.setTarget(RightAscension, Declination);
            //throw new ASCOM.MethodNotImplementedException("SlewToCoordinates");
        }

        public void SlewToCoordinatesAsync(double RightAscension, double Declination) {
            tl.LogMessage("SlewToCoordinatesAsync", "RA:" + utilities.HoursToHMS(RightAscension) + " Dec:" + Declination.ToString());
            clientTask1.setTarget(RightAscension, Declination);

        }

        public void SlewToTarget() {
            tl.LogMessage("SlewToTarget", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("SlewToTarget");
        }

        public void SlewToTargetAsync() {
            tl.LogMessage("SlewToTargetAsync", "RA:" + utilities.HoursToHMS(targetRightAscension) + " Dec:" + targetDeclination.ToString());
            clientTask1.setTarget(targetRightAscension, targetDeclination);
        }

        private bool slew = false;
        public bool Slewing {
            get {
                //                tl.LogMessage("Slewing Get", "Not implemented");
                //                throw new ASCOM.PropertyNotImplementedException("Slewing", false);
                return slew;
            }
        }

        public void SyncToAltAz(double Azimuth, double Altitude) {
            tl.LogMessage("SyncToAltAz", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("SyncToAltAz");
        }

        public void SyncToCoordinates(double RightAscension, double Declination) {
            tl.LogMessage("SyncToCoordinates", "RA:" + utilities.HoursToHMS(RightAscension) + " Dec:" + Declination.ToString());
            clientTask1.syncTarget(RightAscension, Declination);
            //            throw new ASCOM.MethodNotImplementedException("SyncToCoordinates");
        }

        public void SyncToTarget() {
            tl.LogMessage("SyncToTarget", "RA:" + utilities.HoursToHMS(targetRightAscension) + " Dec:" + targetDeclination.ToString());
            clientTask1.syncTarget(targetRightAscension, targetDeclination);
            //              throw new ASCOM.MethodNotImplementedException("SyncToTarget");
        }

        private double targetDeclination;
        public double TargetDeclination {
            get {
                tl.LogMessage("TargetDeclination Get", "Dec:" + targetDeclination.ToString());
                return targetDeclination;
            }
            set {
                targetDeclination = value;
                tl.LogMessage("TargetDeclination Set", "Dec:" + targetDeclination.ToString());
            }
        }

        private double targetRightAscension;
        public double TargetRightAscension {           
            get {
                tl.LogMessage("TargetRightAscension Get", "RA:" + utilities.HoursToHMS(targetRightAscension));
                return targetRightAscension;
            }
            set {
                targetRightAscension = value;
                tl.LogMessage("TargetRightAscension Set", "RA:" + utilities.HoursToHMS(targetRightAscension));
            }
        }

        private bool track = false;
        public bool Tracking {
            get {
                tl.LogMessage("Tracking", "Get - " + track.ToString());
                return track;
            }
            set {
                tl.LogMessage("Tracking Set", "Not implemented");
                clientTask1.sendTracking();
            }
        }

        public DriveRates TrackingRate {
            get {
                tl.LogMessage("TrackingRate Get", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("TrackingRate", false);
            }
            set {
                tl.LogMessage("TrackingRate Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("TrackingRate", true);
            }
        }

        public ITrackingRates TrackingRates {
            get {
                ITrackingRates trackingRates = new TrackingRates();
                tl.LogMessage("TrackingRates", "Get - ");
                foreach (DriveRates driveRate in trackingRates) {
                    tl.LogMessage("TrackingRates", "Get - " + driveRate.ToString());
                }
                return trackingRates;
            }
        }

        public DateTime UTCDate {
            get {
                DateTime utcDate = DateTime.UtcNow;
                tl.LogMessage("TrackingRates", "Get - " + String.Format("MM/dd/yy HH:mm:ss", utcDate));
                return utcDate;
            }
            set {
                tl.LogMessage("UTCDate Set", "Not implemented");
                throw new ASCOM.PropertyNotImplementedException("UTCDate", true);
            }
        }

        public void Unpark() {
            tl.LogMessage("Unpark", "Not implemented");
            throw new ASCOM.MethodNotImplementedException("Unpark");
        }

        #endregion

        #region Private properties and methods
        // here are some useful properties and methods that can be used as required
        // to help with driver development

        #region ASCOM Registration

        // Register or unregister driver for ASCOM. This is harmless if already
        // registered or unregistered. 
        //
        /// <summary>
        /// Register or unregister the driver with the ASCOM Platform.
        /// This is harmless if the driver is already registered/unregistered.
        /// </summary>
        /// <param name="bRegister">If <c>true</c>, registers the driver, otherwise unregisters it.</param>
        private static void RegUnregASCOM(bool bRegister) {
            using (var P = new ASCOM.Utilities.Profile()) {
                P.DeviceType = "Telescope";
                if (bRegister) {
                    P.Register(driverID, driverDescription);
                } else {
                    P.Unregister(driverID);
                }
            }
        }

        /// <summary>
        /// This function registers the driver with the ASCOM Chooser and
        /// is called automatically whenever this class is registered for COM Interop.
        /// </summary>
        /// <param name="t">Type of the class being registered, not used.</param>
        /// <remarks>
        /// This method typically runs in two distinct situations:
        /// <list type="numbered">
        /// <item>
        /// In Visual Studio, when the project is successfully built.
        /// For this to work correctly, the option <c>Register for COM Interop</c>
        /// must be enabled in the project settings.
        /// </item>
        /// <item>During setup, when the installer registers the assembly for COM Interop.</item>
        /// </list>
        /// This technique should mean that it is never necessary to manually register a driver with ASCOM.
        /// </remarks>
        [ComRegisterFunction]
        public static void RegisterASCOM(Type t) {
            RegUnregASCOM(true);
        }

        /// <summary>
        /// This function unregisters the driver from the ASCOM Chooser and
        /// is called automatically whenever this class is unregistered from COM Interop.
        /// </summary>
        /// <param name="t">Type of the class being registered, not used.</param>
        /// <remarks>
        /// This method typically runs in two distinct situations:
        /// <list type="numbered">
        /// <item>
        /// In Visual Studio, when the project is cleaned or prior to rebuilding.
        /// For this to work correctly, the option <c>Register for COM Interop</c>
        /// must be enabled in the project settings.
        /// </item>
        /// <item>During uninstall, when the installer unregisters the assembly from COM Interop.</item>
        /// </list>
        /// This technique should mean that it is never necessary to manually unregister a driver from ASCOM.
        /// </remarks>
        [ComUnregisterFunction]
        public static void UnregisterASCOM(Type t) {
            RegUnregASCOM(false);
        }

        #endregion

        /// <summary>
        /// Returns true if there is a valid connection to the driver hardware
        /// </summary>
        private bool IsConnected {
            get {
                // TODO check that the driver hardware connection exists and is connected to the hardware
                return connectedState;
            }
        }

        /// <summary>
        /// Use this function to throw an exception if we aren't connected to the hardware
        /// </summary>
        /// <param name="message"></param>
        private void CheckConnected(string message) {
            if (!IsConnected) {
                throw new ASCOM.NotConnectedException(message);
            }
        }

        /// <summary>
        /// Read the device configuration from the ASCOM Profile store
        /// </summary>
        internal void ReadProfile() {
            using (Profile driverProfile = new Profile()) {
                driverProfile.DeviceType = "Telescope";
                traceState = Convert.ToBoolean(driverProfile.GetValue(driverID, traceStateProfileName, string.Empty, traceStateDefault));
                hostname = driverProfile.GetValue(driverID, hostnameProfileName, string.Empty, hostnameDefault);
            }
        }

        /// <summary>
        /// Write the device configuration to the  ASCOM  Profile store
        /// </summary>
        internal void WriteProfile() {
            using (Profile driverProfile = new Profile()) {
                driverProfile.DeviceType = "Telescope";
                driverProfile.WriteValue(driverID, traceStateProfileName, traceState.ToString());
                driverProfile.WriteValue(driverID, hostnameProfileName, hostname.ToString());
            }
        }

        #endregion

    }
}
 