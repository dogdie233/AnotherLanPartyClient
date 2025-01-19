using System.Diagnostics.CodeAnalysis;
using System.Net.NetworkInformation;
using System.Runtime.CompilerServices;

using AnotherLanPartyClient;

using Spectre.Console;

using YamlDotNet.Serialization;

const string configFileName = "config.yaml";
const string defaultInterfaceDescription = "TAP-Windows Adapter V9";

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



return;


[MethodImpl(MethodImplOptions.AggressiveInlining)]
static NetworkInterface? FindInterface(string name)
    => NetworkInterface.GetAllNetworkInterfaces()
        .FirstOrDefault(nif => nif.NetworkInterfaceType == NetworkInterfaceType.Ethernet && nif.Description == name);

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void LogInfo(string message)
    => AnsiConsole.MarkupLine($"[[{DateTime.Now:hh:mm:ss}]][[INFO]] {message}");

[MethodImpl(MethodImplOptions.AggressiveInlining)]
static void LogError(string message)
    => AnsiConsole.MarkupLine($"[red][[{DateTime.Now:hh:mm:ss}]][[ERROR]] {message}[/]");

[DoesNotReturn]
static void Exit(bool noInteraction = false)
{
    AnsiConsole.MarkupLine("[yellow]程序已结束，按下任意键退出...[/]");
    if (!noInteraction)
        Console.ReadKey();
    Environment.Exit(0);
    throw new Exception();
}
