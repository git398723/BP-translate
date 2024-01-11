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
using System.Reflection;
using System.Net.Http.Headers;
using System.Security.Principal;
using System.Net.NetworkInformation;
using Microsoft.Win32;
using System.Text.RegularExpressions;

namespace BPtranslate {
    public sealed class Program {
        static X509Certificate2 serverCertificate;
        static string masterDataIp;
        const string redirectEntry = "127.0.0.1 masterdata-main.aws.blue-protocol.com";
        const string masterDataAwsHost = "masterdata-main.aws.blue-protocol.com";

        static string filePath_hosts = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "system32/drivers/etc/hosts");

        const string fileName_LatestPatch = "latest-patch.json";
        static string urlPath_LatestPatch = "https://raw.githubusercontent.com/DOTzX/BP-translate/main/latest-patch.json?t=" + Convert.ToString((int) DateTime.UtcNow.Subtract(new DateTime(1970, 1, 1)).TotalSeconds);

        const string fileName_pfx = "bpmasterdata.pfx";
        static string filePath_pfx = Path.Combine(Directory.GetCurrentDirectory(), fileName_pfx);

        const string fileName_json = "loc.json";
        static string filePath_json = Path.Combine(Directory.GetCurrentDirectory(), fileName_json);

        const string fileName_bptlSetting = "bptl_setting.json";
        static string filePath_bptlSetting = Path.Combine(Directory.GetCurrentDirectory(), fileName_bptlSetting);

        const string server_fileName_zip = "loc.zip";
        static string server_filePath_zip = Path.Combine(Directory.GetCurrentDirectory(), server_fileName_zip);

        const string client_fileName_zip = "modpak.zip";
        static string client_filePath_zip = Path.Combine(Directory.GetCurrentDirectory(), client_fileName_zip);

        const string locVersionFormat = "YYYYMMDD-HHmm";
        static string applicationVersion = Assembly.GetExecutingAssembly().GetName().Version.ToString();

        const string registryKeyPath = @"SOFTWARE\WOW6432Node\BNOLauncher\Contents\production";
        const string registryValueName = "ELrmVeJRPRZMwrerwsJWkTxpKQABgNw3";

        const string dirPath_BpBinaries = @"BLUEPROTOCOL\Binaries\Win64";
        const string fileName_bpExe = "BLUEPROTOCOL-Win64-Shipping.exe";
        const string fileName_patchDll = "dinput8.dll";
        const string dirPath_BpModPak = @"BLUEPROTOCOL\Content\Paks\~mods";

        static void RunServer() {
            TcpListener listener = new TcpListener(IPAddress.Loopback, 443);
            try {
                listener.Start();
            } catch (Exception e) {
                Console.WriteLine("[ERROR] Port 443 is being used, make sure port 443 is not used before running this application.");
                Console.WriteLine("- If xampp is installed, please temporarily turn off the apache web server.");
                RemoveRedirectFromHosts();
                RemoveCertificate();
                Console.WriteLine("\nThe program will close in 5 seconds");
                Console.WriteLine("\n\nAdditional Error Message:");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
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

            string bpDirectory = "";
            bool isSave = false;
            bool server_isDownload = !File.Exists(filePath_json);
            bool server_isUnpack = false;
            bool client_isDownload = !File.Exists(client_filePath_zip);
            bool client_isUnpack = false;
            bool client_isPatchedDllDetected = false;

            JObject jsonObjectAppSetting = new JObject();
            bool isOnlineMode = true;
            bool server_isAutoUpdate = true;
            string server_selectedLanguage = "en";
            double server_installedVersion = 0.0;
            bool client_isAutoUpdate = true;
            string client_selectedLanguage = "en";
            double client_installedVersion = 0.0;
            string client_installedModPakVersion = "Unable to detect";

            JObject jsonOjectMostRecentPatch = new JObject();
            string appLatestVersion = "0.0.0.0";
            double server_latestVersion = 0.0;
            string server_alternativeLanguageName = "";
            string server_urlPath_LocZip = "";
            string server_joinedAvailable = "";
            double client_latestVersion = 0.0;
            string client_alternativeLanguageName = "";
            string client_urlPath_LocZip = "";
            string client_joinedAvailable = "";

            Console.WriteLine($"[INIT] Privilege: {IsAdministrator}");

            Console.Write($"[INIT] Obtaining BLUE PROTOCOL directory location from registry...");
            try {
                using (RegistryKey registryKey = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64).OpenSubKey(registryKeyPath)) {
                    if (registryKey != null) {
                        string tmpBpDirectory = "";
                        bool isBpRegistryFound = false;
                        if (registryKey.GetValue(registryValueName) != null) {
                            string bpRegistry = registryKey.GetValue(registryValueName).ToString();
                            JObject jsonObjectBpRegistry = JObject.Parse(bpRegistry);
                            string InstallRoot = (string) jsonObjectBpRegistry["InstallRoot"];
                            string InstallRootNew = (string)jsonObjectBpRegistry["InstallRootNew"];
                            string ResumptionInstallRoot = (string)jsonObjectBpRegistry["ResumptionInstallRoot"];
                            string InstallDirName = (string)jsonObjectBpRegistry["InstallDirName"];
                            string BNOGameDir = !string.IsNullOrEmpty(InstallRoot) ? InstallRoot :
                                            !string.IsNullOrEmpty(InstallRootNew) ? InstallRootNew :
                                            ResumptionInstallRoot;
                            tmpBpDirectory = Path.Combine(BNOGameDir, InstallDirName);
                            isBpRegistryFound = File.Exists(Path.Combine(tmpBpDirectory, dirPath_BpBinaries, fileName_bpExe));
                        }

                        if (!isBpRegistryFound) {
                            foreach (string valueName in registryKey.GetValueNames()) {
                                if (valueName == registryValueName) continue;

                                if (!isBpRegistryFound) {
                                    string bpRegistry = registryKey.GetValue(registryValueName).ToString();
                                    JObject jsonObjectBpRegistry = JObject.Parse(bpRegistry);
                                    string InstallRoot = (string)jsonObjectBpRegistry["InstallRoot"];
                                    string InstallRootNew = (string)jsonObjectBpRegistry["InstallRootNew"];
                                    string ResumptionInstallRoot = (string)jsonObjectBpRegistry["ResumptionInstallRoot"];
                                    string InstallDirName = (string)jsonObjectBpRegistry["InstallDirName"];
                                    string BNOGameDir = !string.IsNullOrEmpty(InstallRoot) ? InstallRoot :
                                                    !string.IsNullOrEmpty(InstallRootNew) ? InstallRootNew :
                                                    ResumptionInstallRoot;
                                    tmpBpDirectory = Path.Combine(BNOGameDir, InstallDirName);
                                    isBpRegistryFound = File.Exists(Path.Combine(tmpBpDirectory, dirPath_BpBinaries, fileName_bpExe));
                                }
                            }
                        }

                        if (isBpRegistryFound) {
                            bpDirectory = tmpBpDirectory;
                            ClearCurrentConsoleLine();
                            Console.WriteLine($"\r[INIT] BP Directory: {bpDirectory}");
                        } else {
                            Console.WriteLine($"\r[INIT] BP Directory: Not found, not installed ?");
                        }
                    }
                }
            } catch (Exception e) {
                Console.WriteLine($"\r[INIT] Unable to obtain BLUE PROTOCOL directory location: {e.Message}");
            }

            Console.Write($"[INIT] Loading file '{fileName_bptlSetting}'...");
            if (File.Exists(filePath_bptlSetting)) {
                string jsonString = File.ReadAllText(filePath_bptlSetting);
                try {
                    jsonObjectAppSetting = JObject.Parse(jsonString);

                    try {
                        isOnlineMode = (bool) jsonObjectAppSetting["online_mode"];
                    } catch (Exception) {
                        jsonObjectAppSetting["online_mode"] = isOnlineMode;
                        isSave = true;
                    }

                    try {
                        server_isAutoUpdate = (bool) jsonObjectAppSetting["server_auto_update"];
                    } catch (Exception) {
                        jsonObjectAppSetting["server_auto_update"] = server_isAutoUpdate;
                        isSave = true;
                    }

                    try {
                        client_isAutoUpdate = (bool)jsonObjectAppSetting["client_auto_update"];
                    } catch (Exception) {
                        jsonObjectAppSetting["client_auto_update"] = client_isAutoUpdate;
                        isSave = true;
                    }

                    try {
                        server_selectedLanguage = (string) jsonObjectAppSetting["server_selected_language"];
                    } catch (Exception) {
                        jsonObjectAppSetting["server_selected_language"] = server_selectedLanguage;
                        isSave = true;
                    }

                    try {
                        client_selectedLanguage = (string)jsonObjectAppSetting["client_selected_language"];
                    } catch (Exception) {
                        jsonObjectAppSetting["client_selected_language"] = client_selectedLanguage;
                        isSave = true;
                    }

                    try {
                        server_installedVersion = (double) jsonObjectAppSetting[$"server_installed_version_{server_selectedLanguage}"];
                    } catch (Exception) {}

                    try {
                        client_installedVersion = (double)jsonObjectAppSetting[$"client_installed_version_{client_selectedLanguage}"];
                    } catch (Exception) {}

                    Console.WriteLine($"\r[INIT] Success to load '{fileName_bptlSetting}'");
                } catch (Exception e) {
                    Console.WriteLine($"\r[INIT] Fail to load '{fileName_bptlSetting}', skipping.\n{e.Message}");
                }
            } else {
                Console.WriteLine($"\r[INIT] File not found: '{fileName_bptlSetting}', skipping.");
                jsonObjectAppSetting["online_mode"] = isOnlineMode;

                jsonObjectAppSetting["server_auto_update"] = server_isAutoUpdate;
                jsonObjectAppSetting["server_selected_language"] = server_selectedLanguage;
                jsonObjectAppSetting[$"server_installed_version_{server_selectedLanguage}"] = server_installedVersion;

                jsonObjectAppSetting["client_auto_update"] = client_isAutoUpdate;
                jsonObjectAppSetting["client_selected_language"] = client_selectedLanguage;
                jsonObjectAppSetting[$"client_installed_version_{client_selectedLanguage}"] = client_installedVersion;
                isSave = true;
            }

            if (Directory.Exists(Path.Combine(bpDirectory, dirPath_BpModPak))) {
                string[] pakFiles = Directory.GetFiles(Path.Combine(bpDirectory, dirPath_BpModPak), "*.PAK");
                if (pakFiles.Length == 1) {
                    try {
                        using (FileStream fileStream = new FileStream(pakFiles[0], FileMode.Open, FileAccess.Read))
                        using (BinaryReader binaryReader = new BinaryReader(fileStream)) {
                            byte[] searchPattern = { 0x50, 0x61, 0x74, 0x63, 0x68, 0x20, 0x56, 0x65, 0x72, 0x73, 0x69, 0x6F, 0x6E }; // "Patch Version"
                            long offset = FindOffset(binaryReader, searchPattern);

                            if (offset != -1) {
                                fileStream.Seek(offset, SeekOrigin.Begin);
                                byte[] bytes = binaryReader.ReadBytes(36);
                                string pakFileRead = Encoding.UTF8.GetString(bytes);

                                string pattern = @"Patch Version\: (\d{8}-\d{4})";
                                Match match = Regex.Match(pakFileRead, pattern);
                                if (match.Success) client_installedModPakVersion = match.Groups[1].Value;
                            }
                        }
                    } catch (Exception e) {
                        Console.WriteLine(e.StackTrace);
                    }
                } else if (pakFiles.Length == 0) {
                    client_installedModPakVersion = "Not installed";
                }
            }

            client_isPatchedDllDetected = !string.IsNullOrEmpty(bpDirectory) ? File.Exists(Path.Combine(bpDirectory, dirPath_BpBinaries, fileName_patchDll)) : false;

            Console.WriteLine($"[APP] Online Mode: {isOnlineMode}");
            Console.WriteLine($"[APP] DLL Installed on BP Directory: {client_isPatchedDllDetected}");
            Console.WriteLine($"[APP] PAK Installed Version on BP Directory: {client_installedModPakVersion}");

            if (!isOnlineMode) {
                Console.WriteLine($"[APP] Version: {applicationVersion}");
                Console.WriteLine($"[LOC/PAK] Auto Update: False (Offline Mode), Actual: LOC={server_isAutoUpdate} / PAK={client_isAutoUpdate}");
                Console.WriteLine($"[LOC/PAK] Selected Language: LOC={server_selectedLanguage} / PAK={client_selectedLanguage}");
                Console.WriteLine($"[LOC/PAK] Installed Version: LOC={server_installedVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')} / PAK={client_installedVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')}");
            } else {
                Console.Write($"[INIT] Loading '{fileName_LatestPatch}' from remote url...");
                try {
                    using (HttpClient client = new HttpClient()) {
                        client.DefaultRequestHeaders.CacheControl = new CacheControlHeaderValue {
                            NoCache = true
                        };
                        HttpResponseMessage response = await client.GetAsync(urlPath_LatestPatch);
                        if (response.IsSuccessStatusCode) {
                            string jsonString = await response.Content.ReadAsStringAsync();
                            jsonOjectMostRecentPatch = JObject.Parse(jsonString);
                            Console.WriteLine($"\r[INIT] Success to load '{fileName_LatestPatch}' from remote url.");
                        } else {
                            Console.WriteLine($"\r[INIT] Fail to load '{fileName_LatestPatch}' from remote url, skipping. Status Code: {response.StatusCode}");
                        }
                    }
                } catch (Exception e) {
                    Console.WriteLine($"\r[INIT] Fail to load '{fileName_LatestPatch}' from remote url, skipping.\n{e.Message}");
                }

                if (jsonOjectMostRecentPatch.Properties().Any()) {
                    appLatestVersion = (string)jsonOjectMostRecentPatch["_appver"];
                    string[] server_client = { "server", "client" };
                    foreach (string servcli in server_client) {
                        string selectedLanguage = "";
                        JArray arraySelectedLanguage = new JArray();

                        if (servcli == "server") {
                            selectedLanguage = server_selectedLanguage;
                        } else if (servcli == "client") {
                            selectedLanguage = client_selectedLanguage;
                        }

                        JArray availableLanguage = jsonOjectMostRecentPatch[servcli]["_available"] as JArray;
                        string joinedAvailable = string.Join(", ", availableLanguage.Select(item => (string)item));
                        if (!availableLanguage.Any(item => (string)item == selectedLanguage)) {
                            Console.WriteLine($"[WARN-{servcli}] Invalid selected language: '{selectedLanguage}', revert to '{availableLanguage[0]}'");
                            selectedLanguage = (string)availableLanguage[0];
                        }
                        if ((jsonOjectMostRecentPatch[servcli] as JObject).ContainsKey(selectedLanguage)) {
                            arraySelectedLanguage = jsonOjectMostRecentPatch[servcli][selectedLanguage] as JArray;
                        }

                        if (servcli == "server") {
                            server_joinedAvailable = joinedAvailable;
                            if (selectedLanguage != server_selectedLanguage) {
                                server_selectedLanguage = selectedLanguage;
                                jsonObjectAppSetting["server_selected_language"] = server_selectedLanguage;
                                isSave = true;
                            }
                            if (arraySelectedLanguage.Count > 0) {
                                server_latestVersion = (double)arraySelectedLanguage[0];
                                server_alternativeLanguageName = $" ({arraySelectedLanguage[1]})";
                                server_urlPath_LocZip = (string)arraySelectedLanguage[2];
                            }
                        } else if (servcli == "client") {
                            client_joinedAvailable = joinedAvailable;
                            if (selectedLanguage != client_selectedLanguage) {
                                client_selectedLanguage = selectedLanguage;
                                jsonObjectAppSetting["client_selected_language"] = client_selectedLanguage;
                                isSave = true;
                            }
                            if (arraySelectedLanguage.Count > 0) {
                                client_latestVersion = (double)arraySelectedLanguage[0];
                                client_alternativeLanguageName = $" ({arraySelectedLanguage[1]})";
                                client_urlPath_LocZip = (string)arraySelectedLanguage[2];
                            }
                        }
                    }
                }

                Console.WriteLine($"[APP] Installed Version: {applicationVersion}");

                if (appLatestVersion != "0.0.0.0" && appLatestVersion != applicationVersion) {
                    Console.WriteLine($"[APP] New Version Found: {appLatestVersion}, download: https://github.com/DOTzX/BP-translate/releases");
                } else {
                    Console.WriteLine($"[APP] Latest Version: {appLatestVersion}");
                }

                if (server_joinedAvailable.Length > 0) Console.WriteLine($"[LOC] Available language: {server_joinedAvailable}");
                if (client_joinedAvailable.Length > 0) Console.WriteLine($"[PAK] Available language: {client_joinedAvailable}");
                Console.WriteLine($"[LOC/PAK] Auto Update: LOC={server_isAutoUpdate} / PAK={client_isAutoUpdate}");
                Console.WriteLine($"[LOC/PAK] Selected Language: LOC={server_selectedLanguage}{server_alternativeLanguageName} / PAK={client_selectedLanguage}{client_alternativeLanguageName}");
                Console.WriteLine($"[LOC/PAK] Installed Version: LOC={server_installedVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')} / PAK={client_installedVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')}");

                if (server_latestVersion != 0.0 && server_latestVersion > server_installedVersion) {
                    if (server_isAutoUpdate) server_isDownload = true;
                    Console.WriteLine($"[LOC] New Version Found: {server_latestVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')}");
                } else {
                    Console.WriteLine($"[LOC] Latest Version: {server_latestVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')}");
                }

                if (client_latestVersion != 0.0 && client_latestVersion > client_installedVersion) {
                    if (client_isAutoUpdate) client_isDownload = true;
                    Console.WriteLine($"[PAK] New Version Found: {client_latestVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')}");
                } else {
                    Console.WriteLine($"[PAK] Latest Version: {client_latestVersion.ToString().Replace(',', '-').Replace('.', '-').PadRight(locVersionFormat.Length, '0')}");
                }

                if (server_isDownload) {
                    Console.Write($"[INIT] Downloading '{server_fileName_zip}' from remote url...");
                    try {
                        using (HttpClient client = new HttpClient()) {
                            HttpResponseMessage response = await client.GetAsync(server_urlPath_LocZip);

                            if (response.IsSuccessStatusCode) {
                                using (FileStream fileStream = File.Create(server_filePath_zip)) {
                                    await response.Content.CopyToAsync(fileStream);
                                }
                                server_isUnpack = true;
                                ClearCurrentConsoleLine();
                                Console.WriteLine($"\r[INIT] Downloaded '{server_fileName_zip}'");
                            } else {
                                Console.WriteLine($"\r[INIT] Fail to download '{server_fileName_zip}' from remote url, skipping. Status Code: {response.StatusCode}");
                            }
                        }
                    } catch (Exception e) {
                        Console.WriteLine($"\r[INIT] Fail to download '{server_fileName_zip}' from remote url, skipping.\n{e.Message}");
                    }
                }

                if (server_isUnpack) {
                    Console.Write($"[INIT] Unpacking '{server_fileName_zip}'...");
                    try {
                        string extractDirectory = Directory.GetCurrentDirectory();

                        using (ZipFile zip = ZipFile.Read(server_filePath_zip)) {
                            zip.ExtractAll(extractDirectory, ExtractExistingFileAction.OverwriteSilently);
                        }

                        jsonObjectAppSetting[$"server_installed_version_{server_selectedLanguage}"] = server_latestVersion;
                        isSave = true;

                        ClearCurrentConsoleLine();
                        Console.WriteLine($"\r[INIT] Unpacked '{server_fileName_zip}'");
                    } catch (Exception e) {
                        ClearCurrentConsoleLine();
                        Console.WriteLine($"\r[INIT] Fail to unpack '{server_fileName_zip}', skipping.\n{e.Message}");
                    }
                }

                if (client_isDownload) {
                    if (client_isPatchedDllDetected) {
                        Console.Write($"[INIT] Downloading '{client_fileName_zip}' from remote url...");
                        try {
                            using (HttpClient client = new HttpClient()) {
                                HttpResponseMessage response = await client.GetAsync(client_urlPath_LocZip);

                                if (response.IsSuccessStatusCode) {
                                    using (FileStream fileStream = File.Create(client_filePath_zip)) {
                                        await response.Content.CopyToAsync(fileStream);
                                    }
                                    client_isUnpack = true;
                                    ClearCurrentConsoleLine();
                                    Console.WriteLine($"\r[INIT] Downloaded '{client_fileName_zip}'");
                                } else {
                                    Console.WriteLine($"\r[INIT] Fail to download '{client_fileName_zip}' from remote url, skipping. Status Code: {response.StatusCode}");
                                }
                            }
                        } catch (Exception e) {
                            Console.WriteLine($"\r[INIT] Fail to download '{client_fileName_zip}' from remote url, skipping.\n{e.Message}");
                        }
                    } else {
                        Console.WriteLine($"[INIT] Canceled downloading '{client_fileName_zip}' from remote url, DLL is not installed.");
                    }
                }

                if (client_isUnpack) {
                    Console.Write($"[INIT] Unpacking '{client_fileName_zip}'...");
                    try {
                        string extractDirectory = Path.Combine(bpDirectory, dirPath_BpModPak);

                        if (!Directory.Exists(extractDirectory)) Directory.CreateDirectory(extractDirectory);

                        using (ZipFile zip = ZipFile.Read(client_filePath_zip)) {
                            zip.ExtractAll(extractDirectory, ExtractExistingFileAction.OverwriteSilently);
                        }

                        jsonObjectAppSetting[$"client_installed_version_{client_selectedLanguage}"] = client_latestVersion;
                        isSave = true;

                        ClearCurrentConsoleLine();
                        Console.WriteLine($"\r[INIT] Unpacked '{client_fileName_zip}'");
                    } catch (Exception e) {
                        ClearCurrentConsoleLine();
                        Console.WriteLine($"\r[INIT] Fail to unpack '{client_fileName_zip}', skipping.\n{e.Message}");
                    }
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
                Console.WriteLine("- Temporarily turn off the anti-virus");
                Console.WriteLine("- Application is run as Administrator");
                Console.WriteLine("- File/Directory Permission is not Read-Only");
                Console.WriteLine("- File/Directory Ownership is the current logged-in user");
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
            } catch (Exception e) {
                Console.WriteLine("[ERROR] Unable to access hosts file, please make sure:");
                Console.WriteLine("- Temporarily turn off the anti-virus");
                Console.WriteLine("- Application is run as Administrator");
                Console.WriteLine("- File/Directory Permission is not Read-Only");
                Console.WriteLine("- File/Directory Ownership is the current logged-in user");
                RemoveCertificate();
                Console.WriteLine("\nThe program will close in 5 seconds");
                Console.WriteLine("\n\nAdditional Error Message:");
                Console.WriteLine(e.Message);
                Console.WriteLine(e.StackTrace);
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

        static bool IsAdministrator {
            get {
                WindowsIdentity identity = WindowsIdentity.GetCurrent();
                WindowsPrincipal principal = new WindowsPrincipal(identity);

                return principal.IsInRole(WindowsBuiltInRole.Administrator);
            }
        }

        static long FindOffset(BinaryReader binaryReader, byte[] searchPattern) {
            int bufferSize = 4096;
            byte[] buffer = new byte[bufferSize];
            int bytesRead;

            while ((bytesRead = binaryReader.Read(buffer, 0, bufferSize)) > 0) {
                for (int i = 0; i <= bytesRead - searchPattern.Length; i++) {
                    bool found = true;
                    for (int j = 0; j < searchPattern.Length; j++) {
                        if (buffer[i + j] != searchPattern[j]) {
                            found = false;
                            break;
                        }
                    }

                    if (found) {
                        return binaryReader.BaseStream.Position - bufferSize + i;
                    }
                }

                binaryReader.BaseStream.Seek(-searchPattern.Length + 1, SeekOrigin.Current);
            }

            return -1;
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