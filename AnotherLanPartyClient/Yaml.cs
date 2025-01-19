using YamlDotNet.Serialization;

namespace AnotherLanPartyClient;

[YamlStaticContext]
public partial class YamlContext : StaticContext;

[YamlSerializable]
public class ConfigModel
{
    [YamlMember] public string Name { get; set; } = string.Empty;

    [YamlMember] public string Host { get; set; } = string.Empty;

    [YamlMember] public ushort TcpPort { get; set; }

    [YamlMember] public ushort UdpPort { get; set; }

    [YamlMember] public string Username { get; set; } = string.Empty;

    [YamlMember] public string Password { get; set; } = string.Empty;

    [YamlMember] public string? Interface { get; set; }

    [YamlMember] public bool UseTcp { get; set; } = false;

    [YamlMember] public uint PingDelay { get; set; } = 5000;
}