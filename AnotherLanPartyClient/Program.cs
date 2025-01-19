using System.Collections.Concurrent;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using System.Runtime.CompilerServices;

using AnotherLanPartyClient;

using Spectre.Console;

using YamlDotNet.Serialization;

const string configFileName = "config.yaml";
const string defaultInterfaceDescription = "TAP-Windows Adapter V9";
const string openvpnPath = "openvpn.exe";
const string openvpnConfigName = "config.ovpn";
const string passwordFileName = "pass.txt";

// Set the current directory to the directory of the executable
Environment.CurrentDirectory = Path.GetDirectoryName(Environment.ProcessPath) ?? Environment.CurrentDirectory;
LogInfo("当前工作目录: " + Environment.CurrentDirectory);

if (!File.Exists(configFileName))
{
    LogError("配置文件不存在, 请检查配置文件是否存在");
    Exit();
}

ConfigModel config;
var yamlDeserializer = new StaticDeserializerBuilder(new YamlContext()).Build();
using (var reader = new StreamReader(configFileName))
{
    config = yamlDeserializer.Deserialize<ConfigModel>(reader);
}

if (config.Interface is null)
{
    LogInfo($"未指定网卡接口, 将使用默认的 {defaultInterfaceDescription}");
    config.Interface = defaultInterfaceDescription;
}

if (FindInterface(config.Interface) is not {} nif)
{
    LogError($"未找到指定的网卡接口: {config.Interface}");
    Exit();
}
var nifId = nif.Id;
LogInfo($"已找到指定的网卡接口: {nif.Description} ({nifId})");

if (await GetIPAddress(config.Host) is not { } ip)
{
    LogError($"无法解析主机 {config.Host} 的ip地址");
    Exit();
}

File.WriteAllText(openvpnConfigName, GetOpenVpnConfig(config, ip));
File.WriteAllText(passwordFileName, $"{config.Username}\n{config.Password}");

var psi = new ProcessStartInfo(openvpnPath, $"--config \"{openvpnConfigName}\" --dev-node \"{nifId}\"")
{
    UseShellExecute = false,
    RedirectStandardOutput = true,
    RedirectStandardError = true,
    CreateNoWindow = true,
    WorkingDirectory = Environment.CurrentDirectory
};
var openVpnProcess = Process.Start(psi);
if (openVpnProcess is null)
{
    LogError("无法启动OpenVPN进程");
    Exit();
}

Process.GetCurrentProcess().Exited += (sender, args) =>
{
    openVpnProcess.Kill();
    openVpnProcess.WaitForExit();
};

Task<PingReply>? pingTask = null;
var pingPayload = Enumerable.Range(0, 32).Select(i => (byte)('a' + i % 23)).ToArray();
var pingOptions = new PingOptions();
ConcurrentQueue<string> ovpnOutputQueue = new();

openVpnProcess.OutputDataReceived += OnProcessData;
openVpnProcess.ErrorDataReceived += OnProcessData;

while (true)
{
    if (pingTask is null || pingTask.IsCompleted)
    {
        if (pingTask is not null)
        {
            var pingResult = pingTask.Result;
            LogInfo(pingResult.Status == IPStatus.Success ? $"当前延迟为 {pingResult.RoundtripTime}ms" : $"连接失败: {pingResult.Status}");
        }

        pingTask = new Ping().SendPingAsync(ip, TimeSpan.FromSeconds(10), pingPayload, pingOptions);
    }

    while (ovpnOutputQueue.TryDequeue(out var message))
        LogOpenVPN(message);
}

return;


void OnProcessData(object sender, DataReceivedEventArgs e)
{
    if (e.Data is {} content)
        ovpnOutputQueue.Enqueue(content);
}

static string GetOpenVpnConfig(ConfigModel config, IPAddress serverIp)
{
    return $"""
             client
             dev tap
             proto {(config.UseTcp ? "tcp" : "udp")}
             sndbuf 0
             rcvbuf 0
             remote {serverIp} {(config.UseTcp ? config.TcpPort : config.UdpPort)}
             resolv-retry infinite
             nobind
             persist-key
             #persist-tun
             remote-cert-tls server
             #comp-lzo
             comp-noadapt
             verb 3
             nice 0
             mute 0
             cipher none
             auth none
             auth-user-pass {passwordFileName}
             max-routes 9999
             socks-proxy-retry
             reneg-sec 0
             #mute-replay-warnings
             #route-delay 2
             #route-method ipapi
             tun-mtu 1500
             #TCP不支持
             {(config.UseTcp ? "#" : "")}fragment 1356
             mssfix 1356
             management localhost 7506
             log openvpn-log.log
             
             
             <ca>
             -----BEGIN CERTIFICATE-----
             MIIDBzCCAe+gAwIBAgIBATANBgkqhkiG9w0BAQsFADARMQ8wDQYDVQQDDAZVc2JF
             QW0wHhcNMjAwNDE0MDcxMjM4WhcNMzAwNDEyMDcxMjM4WjARMQ8wDQYDVQQDDAZV
             c2JFQW0wggEiMA0GCSqGSIb3DQEBAQUAA4IBDwAwggEKAoIBAQDKI9j4QgqcRKj0
             5WQPh3inLHA34GSTPGdoHZUKhCl5V63shSAjE+Yk7xQr6BH++UEXdaIOJRJ4mBlH
             6tMpJAGMsbhJxXlzTWc/6jps63bqy9noa1+0TD8S7pQ6XhkPqCcQzMxoacj8gy6p
             7ytqMxhrrtdfTHkwAPUPoXJG7UYyrhcpSbMz1qdUTOUbSH5ppmsc3IUary7q2aaf
             KYXvXICRElq9YGr6AKaZwtg+3kTcytZnQYGd/0u2MyG0QuF3UZe1TWuur4Om03tG
             DQpvxRhHAwqTZHrl7JKl03QEt++8TuMIoD8nYJ8lt8Li2Jiryy/GD46a4PraYRzw
             XNNOK0RTAgMBAAGjajBoMAwGA1UdEwQFMAMBAf8wHQYDVR0OBBYEFG5nPeDCx+Up
             IHCwg9DUtq0jJD9DMDkGA1UdIwQyMDCAFG5nPeDCx+UpIHCwg9DUtq0jJD9DoRWk
             EzARMQ8wDQYDVQQDDAZVc2JFQW2CAQEwDQYJKoZIhvcNAQELBQADggEBAG9QVmFi
             4lD5IIIfPMnRxgpd9HraW2Nha4KsSm+mNmKCs8JttWAkD2Ih4ciFI3dxcGkv9HEZ
             qnw45cgp6jEMzk4rGTNf6UWZeQSex+d3usZkLP8V8H79UYCiIazecCjQH1BFnoe+
             NZ4VuaVeNXPUmJMRNsoqMWHQwHnF2ewtVKwy5ZqnOz9VqTWgavDpxorrTZpstMYR
             jw6lvXs30syGn/QW3hmetoDlZbaZiO0xNcaC60M5u2giDRWP+KDIZeYJoHgVEhIh
             pdXIQmg1k/h6Po2mafetaj2ZC+UQ5IjZOQcfvWT+ACCshVv7BD3uT1vsO3IbaVHK
             LwJDGbC9NMqIcrc=
             -----END CERTIFICATE-----
             </ca>
             """;
}

// ReSharper disable once InconsistentNaming
static async Task<IPAddress?> GetIPAddress(string host)
{
    if (IPAddress.TryParse(host, out var ip))
        return ip;

    LogInfo($"正在解析主机 {host} 的ip地址...");
    var hostEntry = await Dns.GetHostEntryAsync(host);
    return hostEntry.AddressList.FirstOrDefault(ip => ip.AddressFamily == AddressFamily.InterNetwork);
}

static NetworkInterface? FindInterface(string name)
    => NetworkInterface.GetAllNetworkInterfaces()
        .FirstOrDefault(nif => nif.NetworkInterfaceType == NetworkInterfaceType.Ethernet && nif.Description == name);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void LogInfo(string message)
    => AnsiConsole.MarkupLine($"[[{DateTime.Now:hh:mm:ss}]][[INFO]] {message}");

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void LogError(string message)
    => AnsiConsole.MarkupLine($"[red][[{DateTime.Now:hh:mm:ss}]][[ERROR]] {message}[/]");

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void LogOpenVPN(string message)
    => AnsiConsole.MarkupLine($"[blue][[{DateTime.Now:hh:mm:ss}]][[OpenVPN]] {message}[/]");

[DoesNotReturn]
static void Exit(bool noInteraction = false)
{
    AnsiConsole.MarkupLine("[yellow]程序已结束，按下任意键退出...[/]");
    if (!noInteraction)
        Console.ReadKey();
    Environment.Exit(0);
    throw new Exception();
}
