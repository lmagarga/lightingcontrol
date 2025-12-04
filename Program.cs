using System;
using System.Collections.Generic;
using System.Collections.Concurrent;

using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Net;
using System.Net.Sockets;

using dss = System.Collections.Generic.Dictionary<string, string>;

namespace lightingControlProcess
{
    public class App
    {
        #region Constants
        // Philadelphia, PA
        private static readonly double latitude = 40.0490;
        private static readonly double lw = -74.9806;
        private static readonly double lngHour = lw / 15.0;
        private static readonly double zenith = 90.0 + 50.0/60.0;

        private static readonly double pi180 = (Math.PI / 180.0);

        ConcurrentDictionary<string, dss> beaconItems = new ConcurrentDictionary<string, dss>();
        List<string> possibleKeys = new List<string> { "UUID", "Config-URL" };

        private bool OK = true;
        #endregion Constants
        #region Accessors
        private bool dst( DateTime d)
        {
            // Find the second Sunday in March
            var ssm = new DateTime(d.Year, 3, 1);
            while (ssm.DayOfWeek != DayOfWeek.Sunday) ssm = ssm.AddDays(1);
            ssm = ssm.AddDays(7);

            // And the first sunday in November
            var fsn = new DateTime(d.Year, 11, 1);
            while (fsn.DayOfWeek != DayOfWeek.Sunday) fsn = fsn.AddDays(1);

            if ((d >= ssm) && (d < fsn))
                return true;

            return false;
        }

        private double localOffset(DateTime d)
        {
            return dst(d) ? -4 : -5 ;
        }

        private Socket comm_socket = null;
        private Socket commSocket
        {
            get
            {
                if (null == comm_socket)
                {
                    // GC port communicate over
                    var comm_address = new System.Net.IPAddress(new byte[] { 192, 168, 10, 36 });
                    var comm_port = 5000;
                    var comm_endpoint = new System.Net.IPEndPoint(comm_address, comm_port);

                    comm_socket = new Socket(AddressFamily.InterNetwork, SocketType.Stream, ProtocolType.Tcp);

		    Console.WriteLine(@"Trying to connect to {comm_socket}");
                    comm_socket.Connect(comm_endpoint);
    		    Console.WriteLine(@"Connected!");
                }
                return comm_socket;
            }
        }

        private dss commands = new dss() {
            {  "on", ">N15,16,17,18ON,UP\x0D" },
            { "off", ">N15,16,17,18OFF,UP\x0D" },
            {  "xmason", ">N22,23,24,25ON,UP\x0D" },
            { "xmasoff", ">N22,23,24,25OFF,UP\x0D" }

        };
        #endregion Accessors

        public static void Main(string[] args)
        {
            var a = new App(args);
        }

        public App(string[] args)
        {
            var test = solarTime(DateTime.Today, false);

	    Console.WriteLine( $"Running  with args {String.Join(",", args)}" );
            if (args.Length > 0)
            {
                var cmd = args[0].ToLower();
                if (cmd.Equals("monitor"))
                    monitorAndWait();
                else if (cmd.Equals("schedule"))
                    schedule();
                else
                    SendCmd(getCmd(args[0]));
            }
        }

        // Read a schedule file and spawn a command line interface
        private void schedule()
        {

        }

        private void monitorAndWait()
        {
            var t = monitor();
            t.Join(); // Hangs until OK toggled
        }

        private System.Threading.Thread monitor()
        {
            var t = new System.Threading.Thread(new System.Threading.ThreadStart(listen));
            t.Name = "LightingControl BCast thread";
            t.Start();
            return t;
        }

        public void shutdown()
        {
            OK = false;
        }

        private string getCmd(string k)
        {
            var kl = k.ToLower();

            if (commands.ContainsKey(kl))
                return commands[kl];

            return null;
        }

        public void SendCmd(string cmd)
        {
            if (null != cmd)
            {
                Console.WriteLine("Trying to send command: " + cmd);
                commSocket.Send(Encoding.ASCII.GetBytes(cmd));
                Console.WriteLine("Sent command: " + cmd);
            }
        }

        #region SolarTime
        private double CosDeg(double d)
        {
            return Math.Cos(d * pi180);
        }

        private double acos_deg(double n)
        {
            return (180.0 / Math.PI) * Math.Acos(n);
        }

        private double SinDeg(double d)
        {
            return Math.Sin(d * pi180);
        }

        private double asin_deg(double n)
        {
            return (180.0 / Math.PI) * Math.Asin(n);
        }

        private double atan_deg(double n)
        {
            return (180.0 / Math.PI) * Math.Atan(n);
        }

        private double tan_deg(double n)
        {
            return Math.Tan(n * pi180);
        }

        private double N(DateTime d)
        {
            var tmp = d.DayOfYear;

            var N1 = Math.Floor(275.0 * d.Month / 9.0);
            var N2 = Math.Floor((d.Month + 9.0) / 12.0);
            var N3 = (1 + Math.Floor((d.Year - 4 * Math.Floor(d.Year / 4.0) + 2) / 3.0));
            var N = N1 - (N2 * N3) + d.Day - 30;

            return N;
        }

        private double t_rise(DateTime d)
        {
            return N(d) + ((6.0 - lngHour) / 24.0);
        }

        private double t_set(DateTime d)
        {
            return N(d) + ((18.0 - lngHour) / 24.0);
        }

        private double adjust(double v, double m)
        {
            if (v > m)
                return v - m;
            else if (v < 0)
                return v + m;
            else
                return v;
        }

        private double H_set(double cosH)
        {
            return acos_deg(cosH) / 15.0;
        }

        private double H_rise(double cosH)
        {
            return (360.0 - acos_deg(cosH)) / 15.0;
        }

        private DateTime solarTime(DateTime d, bool sunrise)
        {
            var t = (sunrise) ? t_rise(d) : t_set(d);

            var M = (0.9856 * t) - 3.289;

            var L = M + (1.916 * SinDeg(M)) + (0.020 * SinDeg(2 * M)) + 282.634;
            L = adjust(L, 360.0);

            var RA = atan_deg(0.91746 * tan_deg(L));
            RA = adjust(RA, 360.0);

            var Lquadrant = (Math.Floor(L / 90.0)) * 90.0;
            var Rquadrant = (Math.Floor(RA / 90)) * 90.0;
            RA = RA + (Lquadrant - Rquadrant);

            RA = RA / 15.0;

            var sinDec = 0.39782 * SinDeg(L);
            var cosDec = CosDeg(asin_deg(sinDec));

            var cosH = (CosDeg(zenith) - (sinDec * SinDeg(latitude))) / (cosDec * CosDeg(latitude));
            if (cosH > 1.0) // sun never rises on this date for this location
                return DateTime.MinValue;
            else if (cosH < -1.0) // sun never sets on this date for this location
                return DateTime.MinValue;

            var H = (sunrise) ? H_rise(cosH) : H_set(cosH);

            var T = H + RA - (0.06571 * t) - 6.622;

            var UT = T - lngHour;
            UT = adjust(UT, 24.0);

            var localT = d.AddHours(UT + localOffset(d));

            return localT;
        }
        #endregion SolarTime

        #region Beacon Stuff
        private Dictionary<string, string> parseBeaconMsg(byte[] msg)
        {
            // should be of this form
            // AMXB<-UUID=GC100_000C1E01CF87_GlobalCache><-SDKClass=Utility><-Make=GlobalCache><-Model=GC-100-12><-Revision=1.0.0><Config-Name=GC-100><Config-URL=http://192.168.0.207>
            //var reg = new System.Text.RegularExpressions.Regex(
            //    @"AMXB[(<-UUID=(?<uuid>[\w|_]+)>)|<-SDKClass=(?<sdkclass>[\w|_|-]+)><-Make=(?<make>[\w|_|-]+)><-Model=(?<model>[\w|_|-]+)><-Revision=(?<revision>[\w|\d|.]+)><Config-Name=(?<name>[\w|_|-|\d|.]+)><Config-URL=(?<url>[\w|\d|_|-|\.|\/]+)>"
            //);
            var reg2 = new System.Text.RegularExpressions.Regex(@"AMXB(.*?>)?(.*?>)?(.*?>)?(.*?>)?(.*?>)?(.*?>)?(.*?>)?(.*?>)?");

            var s = Encoding.ASCII.GetString(msg);
            var tmp = reg2.Match(s);

            if (tmp.Success)
            {
                var ret = new dss();

                for (int i = 1; i < tmp.Groups.Count; i++)
                {
                    var g = tmp.Groups[i].ToString();
                    if (string.IsNullOrEmpty(g) || string.IsNullOrWhiteSpace(g))
                        continue;

                    if (!addGroup(g, ret))
                        Console.WriteLine("Didnt recognize group: " + g);
                }
                //Console.WriteLine(string.Format("{0}Msg{0}{1}{2}", "==========", System.Environment.NewLine, string.Join(System.Environment.NewLine, ret.Keys.Select(a => a + " : " + ret[a]).ToArray())));
                return ret;
            }
            else
            {
                //Console.WriteLine("Unknown message: " + s);
                return null;
            }
        }

        private bool addGroup(string g, dss ret)
        {
            var reg2 = new System.Text.RegularExpressions.Regex(@"<-?(?<name>.*?)=(?<payload>(.*?))>");
            if (reg2.IsMatch(g))
            {
                var tmp = reg2.Match(g);
                var name = tmp.Result("${name}");
                var payload = tmp.Result("${payload}");

                if (null == payload || null == name)
                    return false;

                ret.Add(name, payload);
                return true;
            }
            return false;
        }

        private string getKey(dss bd)
        {
            return possibleKeys.Where(a => bd.ContainsKey(a)).First();
        }

        public void addBeaconData(dss bd)
        {
            if (null != bd)
            {
                var k = getKey(bd);
                if (null == k)
                    k = "RandomID_" + DateTime.Now.Ticks;
                else
                    k = bd[k];

                beaconItems.AddOrUpdate(k, bd, (a, b) => { return bd; });
            }
        }

        public string status()
        {
            var i = 0;
            return string.Join("",
                beaconItems.Values.Select(a => string.Format("{0}===== Component {1} ====={0}", System.Environment.NewLine, ++i) +
                string.Join(System.Environment.NewLine, a.Keys.Select(b => string.Format("{0} : {1}", b, a[b])))).ToArray()
                );
        }

        public void listen()
        {
            // Multicast port to listen to
            var beacon_address = new System.Net.IPAddress(new byte[] { 239, 255, 250, 250 });
            var beacon_port = 9131;
            var beacon_socket = new UdpClient();
            beacon_socket.Client.Bind(new IPEndPoint(IPAddress.Any, beacon_port));
            beacon_socket.JoinMulticastGroup(beacon_address);

            var bytes = new byte[2048];
            IPEndPoint remote = new IPEndPoint(IPAddress.Any, beacon_port);
            byte[] msg = null;
            while (OK)
            {
                if (beacon_socket.Available > 0)
                {
                    msg = beacon_socket.Receive(ref remote);
                    addBeaconData(parseBeaconMsg(msg));
                    Console.WriteLine(status());
                }
                else
                    System.Threading.Thread.Sleep(500);
            }
        }
        #endregion Beacon Stuff
    }
}
