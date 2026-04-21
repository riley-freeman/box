namespace Crayon.Box;

public enum MessageType
{
    ServerInfo,
    ClientInfo,

    CloseRequest,

    PrivilegeElevation,
    AuthSuccessful,
    AuthFailed,

    GetWasmPage,
    WasmPageResponse,

    GetResource,
    ResourceResponse,

    GetDisplayName,
    DisplayNameResponse,

    Invalid = int.MaxValue
}