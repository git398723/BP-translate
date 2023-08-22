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
using Newtonsoft.Json.Linq;
using System.Linq;
using System.Net.Http;
using Ionic.Zip;

namespace BPtranslate {
    public sealed class Program {
        static X509Certificate2 serverCertificate;
        static string masterDataIp;
        const string redirectEntry = "127.0.0.1 masterdata-main.aws.blue-protocol.com";
        const string masterDataAwsHost = "masterdata-main.aws.blue-protocol.com";

        static string filePath_hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32/drivers/etc/hosts");

        const string fileName_MostRecentPatch = "most-recent-patch.json";
        static string urlPath_MostRecentPatch = "https://raw.githubusercontent.com/DOTzX/BP-translate/main/most-recent-patch.json?t=" + Convert.ToString((int) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);

        const string fileName_pfx = "bpmasterdata.pfx";
        static string filePath_pfx = Path.Combine(Directory.GetCurrentDirectory(), fileName_pfx);

        const string fileName_json = "loc.json";
        static string filePath_json = Path.Combine(Directory.GetCurrentDirectory(), fileName_json);

        const string fileName_bptlSetting = "bptl_setting.json";
        static string filePath_bptlSetting = Path.Combine(Directory.GetCurrentDirectory(), fileName_bptlSetting);

        const string fileName_zip = "loc.zip";
        static string filePath_zip = Path.Combine(Directory.GetCurrentDirectory(), fileName_zip);

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
                            string loc = File.ReadAllText(filePath_json);
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

        public static async Task<int> Main(string[] args) {
            Console.Title = "app";

            if (File.Exists(filePath_pfx)) {
                serverCertificate = new X509Certificate2(filePath_pfx);
            } else {
                Console.WriteLine($"[ERROR] Unable to locate '{fileName_pfx}' file, please make sure the file is in the same location with this application.");
                Console.WriteLine("\nThe program will close in 10 seconds");
                Thread.Sleep(TimeSpan.FromSeconds(10));
                Environment.Exit(1);
                return 1;
            }

            bool isDownload = !File.Exists(filePath_json);
            bool isSave = false;
            bool isUnpack = false;

            JObject jsonObjectAppSetting = new JObject();
            bool isAutoUpdate = true;
            string selectedLanguage = "en";
            double installedVersion = 0.0;

            JObject jsonOjectMostRecentPatch = new JObject();
            double latestVersion = 0.0;
            string alternativeLanguageName = "";
            string urlPath_LocZip = "";

            Console.Write($"[INIT] Loading '{fileName_MostRecentPatch}' from remote url...");
            try {
                using (HttpClient client = new HttpClient()) {
                    HttpResponseMessage response = await client.GetAsync(urlPath_MostRecentPatch);
                    if (response.IsSuccessStatusCode) {
                        string jsonString = await response.Content.ReadAsStringAsync();
                        jsonOjectMostRecentPatch = JObject.Parse(jsonString);
                        Console.WriteLine($"\r[INIT] Success to load '{fileName_MostRecentPatch}' from remote url.");
                    } else {
                        Console.WriteLine($"\r[INIT] Fail to load '{fileName_MostRecentPatch}' from remote url, skipping. Status Code: {response.StatusCode}");
                    }
                }
            } catch (Exception e) {
                Console.WriteLine($"\r[INIT] Fail to load '{fileName_MostRecentPatch}' from remote url, skipping.\n{e.Message}");
            }

            Console.Write($"[INIT] Loading file '{fileName_bptlSetting}'...");
            if (File.Exists(filePath_bptlSetting)) {
                string jsonString = File.ReadAllText(filePath_bptlSetting);
                try {
                    jsonObjectAppSetting = JObject.Parse(jsonString);

                    try {
                        isAutoUpdate = (bool) jsonObjectAppSetting["auto_update"];
                    } catch (Exception) {
                        jsonObjectAppSetting["auto_update"] = isAutoUpdate;
                        isSave = true;
                    }

                    try {
                        selectedLanguage = (string) jsonObjectAppSetting["selected_language"];
                    } catch (Exception) {
                        jsonObjectAppSetting["selected_language"] = selectedLanguage;
                        isSave = true;
                    }

                    try {
                        installedVersion = (double) jsonObjectAppSetting[$"installed_version_{selectedLanguage}"];
                    } catch (Exception) {}

                    Console.WriteLine($"\r[INIT] Success to load '{fileName_bptlSetting}'");
                } catch (Exception e) {
                    Console.WriteLine($"\r[INIT] Fail to load '{fileName_bptlSetting}', skipping.\n{e.Message}");
                }
            } else {
                Console.WriteLine($"\r[INIT] File not found: '{fileName_bptlSetting}', skipping.");
                jsonObjectAppSetting["auto_update"] = isAutoUpdate;
                jsonObjectAppSetting["selected_language"] = selectedLanguage;
                jsonObjectAppSetting[$"installed_version_{selectedLanguage}"] = installedVersion;
                isSave = true;
            }

            if (jsonOjectMostRecentPatch.Properties().Any()) {
                JArray availableLanguage = jsonOjectMostRecentPatch["_available"] as JArray;
                string joinedAvailable = string.Join(", ", availableLanguage.Select(item => (string) item));
                Console.WriteLine($"[INFO] Available language: {joinedAvailable}");
                if (!availableLanguage.Any(item => (string) item == selectedLanguage)) {
                    Console.WriteLine($"[WARN] Invalid selected language: '{selectedLanguage}', revert to '{availableLanguage[0]}'");
                    selectedLanguage = (string) availableLanguage[0];
                    jsonObjectAppSetting["selected_language"] = selectedLanguage;
                    isSave = true;
                }

                if (jsonOjectMostRecentPatch.ContainsKey(selectedLanguage)) {
                    JArray arraySelectedLanguage = jsonOjectMostRecentPatch[selectedLanguage] as JArray;
                    latestVersion = (double) arraySelectedLanguage[0];
                    alternativeLanguageName = $" ({arraySelectedLanguage[1]})";
                    urlPath_LocZip = (string) arraySelectedLanguage[2];
                }
            }
            Console.WriteLine($"[LOAD] Selected Language: {selectedLanguage}{alternativeLanguageName}");
            Console.WriteLine($"[LOAD] Auto Update: {isAutoUpdate}");
            Console.WriteLine($"[LOAD] Installed Version: {installedVersion}");

            if (latestVersion != 0.0 && latestVersion > installedVersion) {
                if (isAutoUpdate) isDownload = true;
                Console.WriteLine($"[INFO] New Version Found: {latestVersion}");
            } else {
                Console.WriteLine($"[INFO] Latest Version: {latestVersion}");
            }

            if (isDownload) {
                Console.Write($"[INIT] Downloading '{fileName_zip}' from remote url...");
                try {
                    using (HttpClient client = new HttpClient()) {
                        HttpResponseMessage response = await client.GetAsync(urlPath_LocZip);

                        if (response.IsSuccessStatusCode) {
                            using (FileStream fileStream = File.Create(filePath_zip)) {
                                await response.Content.CopyToAsync(fileStream);
                            }
                            isUnpack = true;
                            ClearCurrentConsoleLine();
                            Console.WriteLine($"\r[INIT] Downloaded '{fileName_zip}'");
                        } else {
                            Console.WriteLine($"\r[INIT] Fail to download '{fileName_zip}' from remote url, skipping. Status Code: {response.StatusCode}");
                        }
                    }
                } catch (Exception e) {
                    Console.WriteLine($"\r[INIT] Fail to download '{fileName_zip}' from remote url, skipping.\n{e.Message}");
                }
            }

            if (isUnpack) {
                Console.Write($"[INIT] Unpacking '{fileName_zip}'...");
                try {
                    string extractDirectory = Directory.GetCurrentDirectory();

                    using (ZipFile zip = ZipFile.Read(filePath_zip)) {
                        zip.ExtractAll(extractDirectory, ExtractExistingFileAction.OverwriteSilently);
                    }

                    jsonObjectAppSetting[$"installed_version_{selectedLanguage}"] = latestVersion;
                    isSave = true;

                    ClearCurrentConsoleLine();
                    Console.WriteLine($"\r[INIT] Unpacked '{fileName_zip}'");
                } catch (Exception e) {
                    ClearCurrentConsoleLine();
                    Console.WriteLine($"\r[INIT] Fail to unpack '{fileName_zip}', skipping.\n{e.Message}");
                }
            }

            if (isSave) {
                Console.Write($"[INIT] Saving '{fileName_bptlSetting}'...");
                File.WriteAllText(filePath_bptlSetting, jsonObjectAppSetting.ToString());
                ClearCurrentConsoleLine();
                Console.WriteLine($"\r[INIT] Saved '{fileName_bptlSetting}'");
            }

            if (!File.Exists(filePath_json)) {
                Console.WriteLine($"[ERROR] Unable to locate '{fileName_json}' file, please make sure the file is in the same location with this application.");
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

            Console.WriteLine("\n[INFO] Waiting for game start...");

            while(true) Console.ReadKey();
        }

        static void ClearCurrentConsoleLine() {
            int currentLineCursor = Console.CursorTop;
            Console.SetCursorPosition(0, Console.CursorTop);
            Console.Write(new string(' ', Console.WindowWidth));
            Console.SetCursorPosition(0, currentLineCursor);
        }

        static void AddRedirectToHosts() {
            try {
                string textHostsFile = File.ReadAllText(filePath_hosts);
                if (textHostsFile.Contains(redirectEntry)) return;

                bool addNewLine = true;
                using (FileStream fs = new FileStream(filePath_hosts, FileMode.Open)) {
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
                File.AppendAllText(filePath_hosts, toAppend);
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
                string textHostsFile = File.ReadAllText(filePath_hosts);
                if (textHostsFile.Contains(redirectEntry)) {
                    File.WriteAllText(filePath_hosts, textHostsFile.Replace(redirectEntry, ""));
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