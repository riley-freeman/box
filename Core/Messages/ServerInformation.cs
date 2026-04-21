using System.Text.Json.Serialization;

namespace Crayon.Box.Messages;

public struct ServerInformation(string serverVersion, string organization)
{
    public string ServerVersion { get; set; } = serverVersion;

    [JsonIgnore]
    public Version ServerVersionParsed => Version.Parse(ServerVersion);

    public string Organization { get; set; } = organization;
}
