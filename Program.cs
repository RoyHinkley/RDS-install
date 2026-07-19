// Installs and configures NetBird and RustDesk for a Remote Desktop Support service.
//
// Provider-specific defaults are defined in Program.Secrets.cs. An optional
// RDS-install.config file beside this executable may override individual values.
//
// Build a self-contained version with:
//     dotnet publish

using Microsoft.Win32;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

public static partial class Program
{
    private const string ConfigFileName = "RDS-install.config";
    private const string ValetSubscribePath = "/subscribe";

    private static bool Enroll = true;
    private static string NetBirdIp = "";
    private static string RustDeskPassword = "";

    private static readonly string WindowsDir =
        Environment.GetFolderPath(Environment.SpecialFolder.Windows);

    private static readonly string WindowsDrive =
        Path.GetPathRoot(WindowsDir)!;

    private static string BaseDirectory => AppContext.BaseDirectory;

    public static async Task<int> Main(string[] args)
    {
        Console.OutputEncoding = Encoding.UTF8;

        try
        {
            ParseArguments(args);

            if (args.Any(IsHelpArgument))
            {
                PrintUsage();
                return 0;
            }

            EnsureRunningOnWindows();
            EnsureElevated();

            LoadConfiguration();
            ValidateConfiguration();

            Console.WriteLine($"=== {ServiceName} Installer ===");
            Console.WriteLine();

            string netBirdExe = await EnsureNetBirdAsync();
            NetBirdIp = GetConnectedNetBirdIp(netBirdExe);

            RustDeskState rustDeskState = GetRustDeskState();
            if (rustDeskState == RustDeskState.Running)
            {
                Console.WriteLine();
                Console.WriteLine($"✅ {ServiceName} appears to be installed already.");
                Console.WriteLine("   If remote access is not working, uninstall NetBird and RustDesk,");
                Console.WriteLine("   then run RDS-install again.");
                return 0;
            }

            if (rustDeskState == RustDeskState.InstalledButNotRunning)
            {
                throw new InvalidOperationException(
                    "RustDesk is installed but is not running. Uninstall NetBird and RustDesk, " +
                    "then run RDS-install again.");
            }

            await InstallRustDeskAsync();

            bool valetSucceeded = true;
            if (Enroll)
                valetSucceeded = await SubscribeAsync(RustDeskPassword);

            Console.WriteLine();
            Console.WriteLine($"✅ {ServiceName} is active.");

            if (Enroll)
            {
                Console.WriteLine($"🔑 Permanent RustDesk password: {RustDeskPassword}");

                if (!valetSucceeded)
                {
                    Console.WriteLine();
                    Console.Error.WriteLine(
                        $"⚠️ {ServiceName} was installed, but automatic registration with " +
                        $"{ProviderName} failed.");
                    Console.Error.WriteLine(
                        "   Record the password shown above and provide it to the support staff.");
                    return 2;
                }
            }

            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine($"❌ Error: {ex.Message}");
            Console.Error.WriteLine("Run 'RDS-install --help' for usage.");
            return 1;
        }
    }

    private static bool IsHelpArgument(string arg) =>
        arg is "--help" or "-h" or "-?" or "--?";

    private static void ParseArguments(string[] args)
    {
        foreach (string arg in args)
        {
            if (IsHelpArgument(arg))
                continue;

            if (arg == "--guest")
            {
                Enroll = false;
                continue;
            }

            throw new ArgumentException($"Unknown option: {arg}");
        }
    }

    private static void PrintUsage()
    {
        Console.WriteLine("Usage:");
        Console.WriteLine("  RDS-install [--guest]");
        Console.WriteLine("  RDS-install --help");
        Console.WriteLine();
        Console.WriteLine("Options:");
        Console.WriteLine("  --guest      Install RDS for a Guest helper without subscribing this computer.");
        Console.WriteLine("  --help, -h   Show this help message.");
    }

    private static void LoadConfiguration()
    {
        string configPath = Path.Combine(BaseDirectory, ConfigFileName);
        if (!File.Exists(configPath))
            return;

        Console.WriteLine($"📄 Reading {ConfigFileName}");

        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int lineNumber = 0;

        foreach (string rawLine in File.ReadLines(configPath))
        {
            lineNumber++;
            string line = rawLine.Trim();

            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith(';'))
                continue;

            int equals = line.IndexOf('=');
            if (equals <= 0)
                throw new FormatException(
                    $"Invalid configuration line {lineNumber}: expected Name=Value.");

            string name = line[..equals].Trim();
            string value = line[(equals + 1)..].Trim();

            if (!seen.Add(name))
                throw new FormatException($"Duplicate configuration setting '{name}'.");

            ApplyConfigurationOverride(name, value, lineNumber);
        }
    }

    private static void ApplyConfigurationOverride(string name, string value, int lineNumber)
    {
        switch (name.ToLowerInvariant())
        {
            case "providername":
                ProviderName = value;
                break;
            case "servicename":
                ServiceName = value;
                break;
            case "publicserverurl":
                PublicServerUrl = value;
                break;
            case "vpnserverurl":
                VpnServerUrl = value;
                break;
            case "netbirdguestkey":
                NetBirdGuestKey = value;
                break;
            case "netbirdsubscriberkey":
                NetBirdSubscriberKey = value;
                break;
            case "rustdeskkey":
                RustDeskKey = value;
                break;
            case "rustdeskport":
                if (!int.TryParse(value, out int port) || port is < 1 or > 65535)
                    throw new FormatException(
                        $"Invalid RustDeskPort on configuration line {lineNumber}.");
                RustDeskPort = port;
                break;
            default:
                throw new FormatException($"Unknown configuration setting '{name}'.");
        }
    }

    private static void ValidateConfiguration()
    {
        RequireValue(nameof(ProviderName), ProviderName);
        RequireValue(nameof(ServiceName), ServiceName);
        RequireAbsoluteHttpUri(nameof(PublicServerUrl), PublicServerUrl, requireHttps: true);
        RequireValue(nameof(VpnServerUrl), VpnServerUrl);
        RequireValue(nameof(NetBirdGuestKey), NetBirdGuestKey);
        RequireValue(nameof(NetBirdSubscriberKey), NetBirdSubscriberKey);
        RequireValue(nameof(RustDeskKey), RustDeskKey);

        if (RustDeskPort is < 1 or > 65535)
            throw new InvalidOperationException("RustDeskPort must be between 1 and 65535.");
    }

    private static void RequireValue(string name, string value)
    {
        if (string.IsNullOrWhiteSpace(value) ||
            value.StartsWith('<') ||
            value.Contains("example", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                $"Installer configuration value '{name}' has not been set.");
        }
    }

    private static void RequireAbsoluteHttpUri(string name, string value, bool requireHttps)
    {
        RequireValue(name, value);

        if (!Uri.TryCreate(value, UriKind.Absolute, out Uri? uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps) ||
            (requireHttps && uri.Scheme != Uri.UriSchemeHttps))
        {
            throw new InvalidOperationException(
                $"Installer configuration value '{name}' is not a valid " +
                $"{(requireHttps ? "HTTPS" : "HTTP or HTTPS")} URL.");
        }
    }

    private static async Task<string> EnsureNetBirdAsync()
    {
        string? netBirdExe = FindNetBirdExecutable();

        if (netBirdExe is not null)
        {
            Console.WriteLine($"✅ NetBird found at {netBirdExe}");
            EnsureNetBirdConnected(netBirdExe);
            return netBirdExe;
        }

        if (NetBirdAppearsInstalled())
        {
            throw new InvalidOperationException(
                "NetBird appears to be installed, but netbird.exe could not be found. " +
                "Uninstall NetBird, then run RDS-install again.");
        }

        Console.WriteLine("📦 Installing NetBird...");

        string? localInstaller = FindNewestLocalInstaller(
            "netbird_installer_*.msi",
            "netbird_installer_*.exe");

        string installerPath;
        bool downloaded;

        if (localInstaller is not null)
        {
            installerPath = localInstaller;
            downloaded = false;
            Console.WriteLine($"📁 Using local installer: {Path.GetFileName(installerPath)}");
        }
        else
        {
            installerPath = Path.Combine(Path.GetTempPath(), "netbird-installer.msi");
            downloaded = true;
            Console.WriteLine("🌐 Downloading NetBird...");
            await DownloadFileAsync(
                "https://pkgs.netbird.io/windows/msi/x64",
                installerPath);
        }

        try
        {
            if (Path.GetExtension(installerPath)
                    .Equals(".msi", StringComparison.OrdinalIgnoreCase))
            {
                RunCommand(
                    "msiexec.exe",
                    $"/i \"{installerPath}\" AUTOSTART=0 /quiet /norestart");
            }
            else
            {
                RunCommand(installerPath, "/S");
                DisableNetBirdUiAutostart();
            }
        }
        finally
        {
            if (downloaded)
                TryDeleteFile(installerPath);
        }

        RemoveNetBirdDesktopShortcuts();
        DisableNetBirdUiAutostart();

        netBirdExe = FindNetBirdExecutable();
        if (netBirdExe is null)
        {
            throw new InvalidOperationException(
                "NetBird installation completed, but netbird.exe could not be found. " +
                "Uninstall NetBird, then run RDS-install again.");
        }

        string setupKey = Enroll ? NetBirdSubscriberKey : NetBirdGuestKey;

        Console.WriteLine("🔌 Connecting to the support VPN...");
        RunCommand(
            netBirdExe,
            $"up --management-url {Quote(PublicServerUrl)} --setup-key {Quote(setupKey)}");

        EnsureNetBirdConnected(netBirdExe);
        return netBirdExe;
    }

    private static void EnsureNetBirdConnected(string netBirdExe)
    {
        CommandResult result = RunCommand(netBirdExe, "status", allowFailure: true);

        if (result.ExitCode != 0 ||
            !result.StandardOutput.Contains("Management: Connected", StringComparison.OrdinalIgnoreCase) ||
            !result.StandardOutput.Contains("Signal: Connected", StringComparison.OrdinalIgnoreCase))
        {
            throw new InvalidOperationException(
                "NetBird is installed but is not connected and running correctly. " +
                "Uninstall NetBird, then run RDS-install again.");
        }
    }

    private static string GetConnectedNetBirdIp(string netBirdExe)
    {
        CommandResult result = RunCommand(netBirdExe, "status");

        Match match = Regex.Match(
            result.StandardOutput,
            @"\bIP:\s*(?<ip>\d{1,3}(?:\.\d{1,3}){3})\b",
            RegexOptions.IgnoreCase);

        if (!match.Success)
        {
            throw new InvalidOperationException(
                "NetBird is connected, but its VPN address could not be determined. " +
                "Uninstall NetBird, then run RDS-install again.");
        }

        string ip = match.Groups["ip"].Value;
        Console.WriteLine($"✅ Connected to {ProviderName} support VPN at {ip}");
        return ip;
    }

    private static string? FindNetBirdExecutable()
    {
        string[] candidates =
        {
            Path.Combine(WindowsDrive, "Programs", "NetBird", "netbird.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "NetBird",
                "netbird.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "NetBird",
                "netbird.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static bool NetBirdAppearsInstalled()
    {
        return IsServicePresent("NetBird") ||
               IsApplicationInUninstallRegistry("NetBird");
    }

    private static void DisableNetBirdUiAutostart()
    {
        const string runKey = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";

        using RegistryKey? key = Registry.LocalMachine.OpenSubKey(runKey, writable: true);
        key?.DeleteValue("NetBird", throwOnMissingValue: false);
        key?.DeleteValue("NetBird UI", throwOnMissingValue: false);
        key?.DeleteValue("netbird-ui", throwOnMissingValue: false);
    }

    private static void RemoveNetBirdDesktopShortcuts()
    {
        string[] desktops =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (string desktop in desktops.Where(Directory.Exists))
        {
            foreach (string shortcut in Directory.EnumerateFiles(desktop, "*NetBird*.lnk"))
                TryDeleteFile(shortcut);
        }
    }

    private static RustDeskState GetRustDeskState()
    {
        bool servicePresent = IsServicePresent("RustDesk");
        bool serviceRunning = IsServiceRunning("RustDesk");
        bool executablePresent = FindRustDeskExecutable() is not null;
        bool registered = IsApplicationInUninstallRegistry("RustDesk");

        if (serviceRunning)
            return RustDeskState.Running;

        if (servicePresent || executablePresent || registered)
            return RustDeskState.InstalledButNotRunning;

        return RustDeskState.NotInstalled;
    }
	
	private static string GetVpnServerHost()
	{
		if (!Uri.TryCreate(VpnServerUrl, UriKind.Absolute, out Uri? uri) ||
			(uri.Scheme != Uri.UriSchemeHttp &&
			 uri.Scheme != Uri.UriSchemeHttps))
		{
			throw new InvalidOperationException(
				$"Invalid VPN server URL: {VpnServerUrl}");
		}

		return uri.Host;
	}

    private static async Task InstallRustDeskAsync()
    {
        string rustDeskFolder = Path.Combine(WindowsDrive, "Programs", "RustDesk");
        string serviceConfigRoot = Path.Combine(
            WindowsDir,
            "ServiceProfiles",
            "LocalService",
            "AppData",
            "Roaming",
            "RustDesk");
        string userConfigRoot = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
            "RustDesk");

        // RustDesk is not installed. Remove residual configuration so that this
        // installation controls all settings, including the permanent password.
        TryDeleteDirectory(serviceConfigRoot);
        TryDeleteDirectory(userConfigRoot);

        string configDirectory = Path.Combine(serviceConfigRoot, "config");
        Directory.CreateDirectory(configDirectory);

        if (Enroll)
        {
            RustDeskPassword = GeneratePassword(10, 1, 1, 1);
            string salt = GeneratePassword(8, 0, 1, 1);

            File.WriteAllText(
                Path.Combine(configDirectory, "RustDesk.toml"),
                $"password = '{RustDeskPassword}'{Environment.NewLine}" +
                $"salt = '{salt}'{Environment.NewLine}");
        }

        File.WriteAllText(
            Path.Combine(configDirectory, "RustDesk2.toml"),
            BuildRustDeskConfiguration());

        string? localInstaller = FindNewestLocalInstaller("rustdesk-*.msi");
        string installerPath;
        bool downloaded;

        if (localInstaller is not null)
        {
            installerPath = localInstaller;
            downloaded = false;
            Console.WriteLine($"📁 Using local installer: {Path.GetFileName(installerPath)}");
        }
        else
        {
            Console.WriteLine("🌐 Downloading RustDesk...");
            installerPath = Path.Combine(Path.GetTempPath(), "rustdesk-installer.msi");
            downloaded = true;
            string downloadUrl = await GetLatestRustDeskMsiUrlAsync();
            await DownloadFileAsync(downloadUrl, installerPath);
        }

        try
        {
            Console.WriteLine("📦 Installing RustDesk...");
            RunCommand(
                "msiexec.exe",
                $"/i \"{installerPath}\" /qn /norestart " +
                $"INSTALLFOLDER={Quote(rustDeskFolder)} " +
                "CREATESTARTMENUSHORTCUTS=Y " +
                "CREATEDESKTOPSHORTCUTS=N " +
                "INSTALLPRINTER=N");
        }
        finally
        {
            if (downloaded)
                TryDeleteFile(installerPath);
        }

        RemoveRustDeskDesktopShortcuts();

        if (!WaitForServiceRunning("RustDesk", TimeSpan.FromSeconds(30)))
        {
            throw new InvalidOperationException(
                "RustDesk was installed, but its service did not start.");
        }

        Console.WriteLine("✅ RustDesk service is running.");
    }

    private static string BuildRustDeskConfiguration()
    {
		string serverHost = GetVpnServerHost();
        return $"""
            rendezvous_server = '{serverHost}:{RustDeskPort}'
            nat_type = 1
            serial = 0
            unlock_pin = ''
            trusted_devices = ''

            [options]
            local-ip-addr = '{NetBirdIp}'
            custom-rendezvous-server = '{serverHost}'
            verification-method = 'use-both-passwords'
            av1-test = 'Y'
            relay-server = '{serverHost}'
            allow-remote-config-modification = 'Y'
            enable-lan-discovery = 'N'
            key = '{RustDeskKey}'
            """ + Environment.NewLine;
    }

    private static string? FindRustDeskExecutable()
    {
        string[] candidates =
        {
            Path.Combine(WindowsDrive, "Programs", "RustDesk", "RustDesk.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles),
                "RustDesk",
                "RustDesk.exe"),
            Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86),
                "RustDesk",
                "RustDesk.exe")
        };

        return candidates.FirstOrDefault(File.Exists);
    }

    private static void RemoveRustDeskDesktopShortcuts()
    {
        string[] desktops =
        {
            Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory),
            Environment.GetFolderPath(Environment.SpecialFolder.CommonDesktopDirectory)
        };

        foreach (string desktop in desktops.Where(Directory.Exists))
        {
            foreach (string shortcut in Directory.EnumerateFiles(desktop, "*RustDesk*.lnk"))
                TryDeleteFile(shortcut);
        }
    }

    private static async Task<bool> SubscribeAsync(string password)
    {
        Console.WriteLine("📝 Registering with the support service...");

        Uri endpoint = new(
            new Uri($"{VpnServerUrl.TrimEnd('/')}/"),
            ValetSubscribePath.TrimStart('/'));

        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromSeconds(30)
        };

        using var content = new FormUrlEncodedContent(
            new Dictionary<string, string>
            {
                ["password"] = password
            });

        try
        {
            using HttpResponseMessage response = await client.PostAsync(endpoint, content);
            string responseText = (await response.Content.ReadAsStringAsync()).Trim();

            if (!response.IsSuccessStatusCode)
            {
                Console.Error.WriteLine(
                    $"⚠️ Valet registration failed: {(int)response.StatusCode} " +
                    $"{response.ReasonPhrase}{FormatServerMessage(responseText)}");
                return false;
            }

            if (!string.IsNullOrEmpty(responseText))
                Console.WriteLine($"✅ Registered RustDesk ID {responseText}.");
            else
                Console.WriteLine("✅ Registration accepted; RustDesk ID is pending.");

            return true;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️ Valet registration failed: {ex.Message}");
            return false;
        }
    }

    private static string FormatServerMessage(string message) =>
        string.IsNullOrWhiteSpace(message) ? "" : $": {message}";

    private static string? FindNewestLocalInstaller(params string[] patterns)
    {
        return patterns
            .SelectMany(pattern => Directory.EnumerateFiles(BaseDirectory, pattern))
            .Select(path => new FileInfo(path))
            .OrderByDescending(file => file.LastWriteTimeUtc)
            .ThenByDescending(file => file.Name, StringComparer.OrdinalIgnoreCase)
            .Select(file => file.FullName)
            .FirstOrDefault();
    }

    private static async Task<string> GetLatestRustDeskMsiUrlAsync()
    {
        const string releasesApi =
            "https://api.github.com/repos/rustdesk/rustdesk/releases/latest";

        using var client = new HttpClient();
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RDS-install", "1.0"));

        string json = await client.GetStringAsync(releasesApi);
        using JsonDocument document = JsonDocument.Parse(json);

        JsonElement assets = document.RootElement.GetProperty("assets");
        foreach (JsonElement asset in assets.EnumerateArray())
        {
            string name = asset.GetProperty("name").GetString() ?? "";
            if (Regex.IsMatch(
                    name,
                    @"^rustdesk-.*-x86_64\.msi$",
                    RegexOptions.IgnoreCase))
            {
                return asset.GetProperty("browser_download_url").GetString()
                    ?? throw new InvalidOperationException(
                        "RustDesk release asset has no download URL.");
            }
        }

        throw new InvalidOperationException(
            "The current RustDesk release does not contain a Windows x86-64 MSI installer.");
    }

    private static async Task DownloadFileAsync(string url, string destinationPath)
    {
        using var client = new HttpClient
        {
            Timeout = TimeSpan.FromMinutes(5)
        };
        client.DefaultRequestHeaders.UserAgent.Add(
            new ProductInfoHeaderValue("RDS-install", "1.0"));

        using HttpResponseMessage response = await client.GetAsync(
            url,
            HttpCompletionOption.ResponseHeadersRead);
        response.EnsureSuccessStatusCode();

        await using Stream source = await response.Content.ReadAsStreamAsync();
        await using var destination = new FileStream(
            destinationPath,
            FileMode.Create,
            FileAccess.Write,
            FileShare.None);
        await source.CopyToAsync(destination);
    }

    private static CommandResult RunCommand(
        string fileName,
        string arguments,
        bool allowFailure = false)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = fileName,
            Arguments = arguments,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Unable to start '{fileName}'.");

        Task<string> stdoutTask = process.StandardOutput.ReadToEndAsync();
        Task<string> stderrTask = process.StandardError.ReadToEndAsync();

        process.WaitForExit();
        Task.WaitAll(stdoutTask, stderrTask);

        var result = new CommandResult(
            process.ExitCode,
            stdoutTask.Result,
            stderrTask.Result);

        if (!allowFailure && result.ExitCode != 0)
        {
            string detail = string.IsNullOrWhiteSpace(result.StandardError)
                ? result.StandardOutput.Trim()
                : result.StandardError.Trim();

            throw new InvalidOperationException(
                $"'{fileName} {arguments}' failed with exit code {result.ExitCode}" +
                (string.IsNullOrEmpty(detail) ? "." : $": {detail}"));
        }

        return result;
    }

    private static bool IsServicePresent(string serviceName)
    {
        CommandResult result = RunCommand(
            "sc.exe",
            $"query {Quote(serviceName)}",
            allowFailure: true);

        return result.ExitCode == 0;
    }

    private static bool IsServiceRunning(string serviceName)
    {
        CommandResult result = RunCommand(
            "sc.exe",
            $"query {Quote(serviceName)}",
            allowFailure: true);

        return result.ExitCode == 0 &&
               result.StandardOutput.Contains("RUNNING", StringComparison.OrdinalIgnoreCase);
    }

    private static bool WaitForServiceRunning(string serviceName, TimeSpan timeout)
    {
        Stopwatch stopwatch = Stopwatch.StartNew();

        while (stopwatch.Elapsed < timeout)
        {
            if (IsServiceRunning(serviceName))
                return true;

            Thread.Sleep(1000);
        }

        return false;
    }

    private static bool IsApplicationInUninstallRegistry(string displayNameFragment)
    {
        string[] keyPaths =
        {
            @"SOFTWARE\Microsoft\Windows\CurrentVersion\Uninstall",
            @"SOFTWARE\WOW6432Node\Microsoft\Windows\CurrentVersion\Uninstall"
        };

        foreach (string keyPath in keyPaths)
        {
            using RegistryKey? root = Registry.LocalMachine.OpenSubKey(keyPath);
            if (root is null)
                continue;

            foreach (string subKeyName in root.GetSubKeyNames())
            {
                using RegistryKey? subKey = root.OpenSubKey(subKeyName);
                string? displayName = subKey?.GetValue("DisplayName") as string;

                if (displayName?.Contains(
                        displayNameFragment,
                        StringComparison.OrdinalIgnoreCase) == true)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private static string GeneratePassword(
        int length,
        int minUpper,
        int minLower,
        int minDigit)
    {
        const string uppers = "ABCDEFGHJKLMNPQRSTUVWXYZ";
        const string lowers = "abcdefghijkmnopqrstuvwxyz";
        const string digits = "23456789";

        int required = minUpper + minLower + minDigit;
        if (length < required)
            length = required;

        var characters = new List<char>(length);
        string available = "";

        AddRequired(uppers, minUpper);
        AddRequired(lowers, minLower);
        AddRequired(digits, minDigit);

        if (available.Length == 0)
            throw new ArgumentException("At least one password character class is required.");

        while (characters.Count < length)
            characters.Add(available[RandomNumberGenerator.GetInt32(available.Length)]);

        for (int i = characters.Count - 1; i > 0; i--)
        {
            int j = RandomNumberGenerator.GetInt32(i + 1);
            (characters[i], characters[j]) = (characters[j], characters[i]);
        }

        return new string(characters.ToArray());

        void AddRequired(string source, int count)
        {
            if (count <= 0)
                return;

            available += source;
            for (int i = 0; i < count; i++)
                characters.Add(source[RandomNumberGenerator.GetInt32(source.Length)]);
        }
    }

    private static string Quote(string value) =>
        "\"" + value.Replace("\"", "\\\"") + "\"";

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path))
                File.Delete(path);
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"⚠️ Unable to remove '{path}': {ex.Message}");
        }
    }

    private static void TryDeleteDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path))
                Directory.Delete(path, recursive: true);
        }
        catch (Exception ex)
        {
            throw new InvalidOperationException(
                $"Unable to remove residual RustDesk configuration '{path}': {ex.Message}");
        }
    }

    private static void EnsureRunningOnWindows()
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "This installer is supported only on Windows.");
    }

    private static void EnsureElevated()
    {
        using WindowsIdentity identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);

        if (!principal.IsInRole(WindowsBuiltInRole.Administrator))
        {
            throw new UnauthorizedAccessException(
                "This installer must be run with administrative privileges.");
        }
    }

    private enum RustDeskState
    {
        NotInstalled,
        Running,
        InstalledButNotRunning
    }

    private sealed record CommandResult(
        int ExitCode,
        string StandardOutput,
        string StandardError);
}
