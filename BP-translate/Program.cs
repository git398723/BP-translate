using System.Net;
using System.Net.Sockets;
using System.Net.Security;
using System.Security.Cryptography.X509Certificates;
using System.Threading.Tasks;
using System;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Runtime.CompilerServices;

namespace BPtranslate {
    public sealed class Program {
        static X509Certificate2 serverCertificate;
        static string masterDataIp;
        static string hostsFile = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32/drivers/etc/hosts");

        const string redirectEntry = "127.0.0.1 masterdata-main.aws.blue-protocol.com";
        const string masterDataAwsHost = "masterdata-main.aws.blue-protocol.com";

        static void RunServer() {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 443);
            try {
                listener.Start();
            } catch (Exception) {
                Console.WriteLine("[ERROR] Port 443 has been used, make sure port 443 is not used before running this application.");
                RemoveRedirectFromHosts();
                RemoveCertificate();
                Console.WriteLine("\nThe program will close in 5 seconds");
                Thread.Sleep(TimeSpan.FromSeconds(5));
                Environment.Exit(1);
                return;
            }

            new Task(() => {
                while (true) {
                    TcpClient client = listener.AcceptTcpClient();
                    new Task(() => {
                        SslStream clientStream = new SslStream(client.GetStream(), false);
                        clientStream.AuthenticateAsServer(serverCertificate, clientCertificateRequired: false, checkCertificateRevocation: false);

                        SslStream serverStream = new SslStream(new TcpClient(masterDataIp, 443).GetStream(), false);
                        serverStream.AuthenticateAsClient(masterDataAwsHost);

                        ProxyConnection(clientStream, serverStream, true);
                        ProxyConnection(serverStream, clientStream, false);
                    }).Start();
                }
            }).Start();
        }

        static void ProxyConnection(SslStream src, SslStream output, bool toServer) {
            new Task(() => {
                byte[] message = new byte[4096];
                int clientBytes;
                while (true) {
                    try {
                        clientBytes = src.Read(message, 0, 4096);
                    } catch {
                        break;
                    }
                    if (clientBytes == 0) {
                        break;
                    }

                    if (toServer) {
                        string request = Encoding.UTF8.GetString(message);
                        string[] reqLines = request.Split(new string[] { Environment.NewLine }, StringSplitOptions.None);
                        string firstLine = reqLines[0];

                        if (firstLine.StartsWith("GET /apiext/texts/ja_JP")) {
                            string loc = File.ReadAllText(Path.Combine(Directory.GetCurrentDirectory(), "loc.json"));
                            string response = "HTTP/1.1 200 OK\r\nContent-Type: text/plain\r\nContent-Length: " + loc.Length + "\r\nConnection: keep-alive\r\n\r\n" + loc;
                            src.Write(Encoding.ASCII.GetBytes(response));

                            Console.WriteLine("Translation sent, the program will close in 15 seconds");
                            Thread.Sleep(TimeSpan.FromSeconds(15));
                            RemoveRedirectFromHosts();
                            RemoveCertificate();
                            Environment.Exit(0);
                            continue;
                        }
                    }
                    output.Write(message, 0, clientBytes);
                }
                src.Close();
            }).Start();
        }

        public static int Main(string[] args) {
            Console.Title = "app";

            if (File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "bpmasterdata.pfx"))) {
                serverCertificate = new X509Certificate2(Path.Combine(Directory.GetCurrentDirectory(), "bpmasterdata.pfx"));
            } else {
                Console.WriteLine("[ERROR] Unable to locate bpmasterdata.pfx file, please make sure the file is in the same location with this application.");
                Console.WriteLine("\nThe program will close in 10 seconds");
                Thread.Sleep(TimeSpan.FromSeconds(10));
                Environment.Exit(1);
                return 1;
            }
            if (!File.Exists(Path.Combine(Directory.GetCurrentDirectory(), "loc.json"))) {
                Console.WriteLine("[ERROR] Unable to locate loc.json file, please make sure the file is in the same location with this application.");
                Console.WriteLine("\nThe program will close in 10 seconds");
                Thread.Sleep(TimeSpan.FromSeconds(10));
                Environment.Exit(1);
                return 1;
            }

            handler = new ConsoleEventDelegate(ConsoleEventCallback);
            SetConsoleCtrlHandler(handler, true);

            RemoveRedirectFromHosts();
            UpdateMasterDataRealIp();
            AddCertificate();
            AddRedirectToHosts();

            RunServer();

            Console.WriteLine("Waiting for game start");

            while(true) Console.ReadKey();
        }

        static void AddRedirectToHosts() {
            try {
                string textHostsFile = File.ReadAllText(hostsFile);
                if (textHostsFile.Contains(redirectEntry)) return;

                bool addNewLine = true;
                using (FileStream fs = new FileStream(hostsFile, FileMode.Open)) {
                    if (fs.Length == 0) {
                        addNewLine = false;
                    } else {
                        using (BinaryReader rd = new BinaryReader(fs)) {
                            fs.Position = fs.Length - 1;
                            int last = rd.Read();
                            if (last == 10) addNewLine = false;
                        }
                    }
                }

                string toAppend = (addNewLine ? Environment.NewLine + redirectEntry : redirectEntry);
                File.AppendAllText(hostsFile, toAppend);
            } catch (Exception e) {
                Console.WriteLine("[ERROR] Unable to access hosts file, please make sure:");
                Console.WriteLine("- File/Directory Ownership is the current logged-in user");
                Console.WriteLine("- File/Directory Permission is not Read-Only");
                Console.WriteLine("- Crrent logged-in user is Administrator");
                RemoveCertificate();
                Console.WriteLine("\nThe program will close in 5 seconds");
                Console.WriteLine("\n\nAdditional Error Message:");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
                Thread.Sleep(TimeSpan.FromSeconds(5));
                Environment.Exit(1);
            }
        }

        static void RemoveRedirectFromHosts() {
            try {
                string textHostsFile = File.ReadAllText(hostsFile);
                if (textHostsFile.Contains(redirectEntry)) {
                    File.WriteAllText(hostsFile, textHostsFile.Replace(redirectEntry, ""));
                }
            } catch (Exception) {
                Console.WriteLine("[ERROR] Unable to access hosts file, please make sure:");
                Console.WriteLine("- File/Directory Ownership is the current logged-in user");
                Console.WriteLine("- File/Directory Permission is not Read-Only");
                Console.WriteLine("- Current logged-in user is Administrator");
                RemoveCertificate();
                Console.WriteLine("\nThe program will close in 5 seconds");
                Thread.Sleep(TimeSpan.FromSeconds(5));
                Environment.Exit(1);
            }
        }

        static void AddCertificate() {
            using (X509Store store = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) {
                store.Open(OpenFlags.ReadWrite);
                store.Add(serverCertificate);
            }
        }

        static void RemoveCertificate() {
            using (X509Store store = new X509Store(StoreName.Root, StoreLocation.LocalMachine)) {
                store.Open(OpenFlags.ReadWrite);
                store.Remove(serverCertificate);
            }
        }

        static void UpdateMasterDataRealIp() {
            masterDataIp = Dns.GetHostEntry(masterDataAwsHost).AddressList[0].ToString();
        }

        //Handle console close
        static bool ConsoleEventCallback(int eventType) {
            if (eventType == 2) {
                RemoveRedirectFromHosts();
                RemoveCertificate();
            }
            return false;
        }
        static ConsoleEventDelegate handler;
        private delegate bool ConsoleEventDelegate(int eventType);
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool SetConsoleCtrlHandler(ConsoleEventDelegate callback, bool add);
    }
}