namespace Crayon.Box.Provider;

public enum IPCMessageType
{
    ImportFile,
    RemoveFile,

    NewThread,
    ChangeDisplayName,
}

public class IPCMessage
{
    public IPCMessageType Type { get; set; }

    public string? FilePath { get; set; } = null;
    public string? Nickname { get; set; } = null;

    public string? NewDisplayName { get; set; } = null;
    public string? ThreadTitle { get; set; } = null;
    public string? ThreadMessage { get; set; } = null;
}