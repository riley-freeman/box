using System.Text.Json.Serialization;

namespace Crayon.Box.Messages;

public struct ClientInformation(string clientVersion, string organization)
{
    public string ClientVersion { get; set; } = clientVersion;

    [JsonIgnore]
    public Version ClientVersionParsed => Version.Parse(ClientVersion);

    public string Organization { get; set; } = organization;
    public string Platform { get; set; } = Environment.OSVersion.VersionString;
}
