using System.CommandLine;
using System.Net.Sockets;
using Crayon.Box;
using Crayon.Box.Provider;

var socketPath = "/tmp/crayon.box.provider.sock";

void SendIPCMessage(IPCMessage message)
{
    using var ipcClient = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    var endpoint = new UnixDomainSocketEndPoint(socketPath);
    ipcClient.Connect(endpoint);

    string jsonMessage = System.Text.Json.JsonSerializer.Serialize(message);
    byte[] messageBytes = System.Text.Encoding.UTF8.GetBytes(jsonMessage);
    ipcClient.Send(messageBytes);
}

void HandleIPCMessage(IPCMessage ipcMessage, Provider provider)
{
    switch (ipcMessage.Type)
    {
        case IPCMessageType.ImportFile:
            provider.ImportFile(ipcMessage.FilePath!, ipcMessage.Nickname).GetAwaiter().GetResult();
            break;

        case IPCMessageType.RemoveFile:
            provider.RemoveFile(ipcMessage.Nickname!).GetAwaiter().GetResult();
            break;

        case IPCMessageType.NewThread:
            provider.NewThread(ipcMessage.ThreadTitle!, ipcMessage.ThreadMessage!).GetAwaiter().GetResult();
            break;

        case IPCMessageType.ChangeDisplayName:
            provider.SetDisplayName(ipcMessage.NewDisplayName!).GetAwaiter().GetResult();
            break;

        default:
            Console.Error.WriteLine($"Received unknown IPC message type: {ipcMessage.Type}");
            break;
    }
}


var rootCommand = new RootCommand("Manages resources and delivers to the Crayon Box!");

var importCommand = new Command("import", "Imports a new resource file.");
var importPathArg = new Argument<string>("path");
var importNickname = new Option<string>(name: "--name", ["-n"]);
importCommand.Add(importPathArg);
importCommand.Add(importNickname);
importCommand.SetAction(async (parseResult) =>
{
    string path = parseResult.GetValue(importPathArg)!;
    string? nickname = parseResult.GetValue(importNickname);
    SendIPCMessage(new IPCMessage
    {
        Type = IPCMessageType.ImportFile,
        FilePath = path,
        Nickname = nickname
    });
});

var threadCommand = new Command("new-thread", "Posts a new thread");
var threadTitleArg = new Argument<string>("title");
var threadMessageArg = new Argument<string>("message");
threadCommand.Add(threadTitleArg);
threadCommand.Add(threadMessageArg);
threadCommand.SetAction((parseResult) =>
{
    string title = parseResult.GetValue(threadTitleArg)!;
    string message = parseResult.GetValue(threadMessageArg)!;
    SendIPCMessage(new IPCMessage
    {
        Type = IPCMessageType.NewThread,
        ThreadTitle = title,
        ThreadMessage = message
    });
});

var removeCommand = new Command("remove", "Deletes a resource file.");
var removeName = new Argument<string>("cached resource name");
removeCommand.Add(removeName);
removeCommand.SetAction(async (parseResult) =>
{
    string name = parseResult.GetValue(removeName)!;
    SendIPCMessage(new IPCMessage
    {
        Type = IPCMessageType.RemoveFile,
        Nickname = name
    });
});


var setDisplayNameCommand = new Command("set-display-name", "Changes the local display name.");
var setNameArg = new Argument<string>("display name");
setDisplayNameCommand.Add(setNameArg);
setDisplayNameCommand.SetAction(async (parseResult) =>
{
    string name = parseResult.GetValue(setNameArg)!;
    SendIPCMessage(new IPCMessage
    {
        Type = IPCMessageType.ChangeDisplayName,
        NewDisplayName = name
    });
});

var serveCommand = new Command("serve", "Start the provider server and serve resources to the Crayon Box.");
serveCommand.SetAction((parseResult) =>
{
    var provider = new Provider(null, null);
    if (File.Exists(socketPath)) File.Delete(socketPath); // Clean up previous socket

    using var ipcSocket = new Socket(AddressFamily.Unix, SocketType.Stream, ProtocolType.Unspecified);
    var endpoint = new UnixDomainSocketEndPoint(socketPath);

    ipcSocket.Bind(endpoint);
    ipcSocket.Listen(5);
    Console.WriteLine("Server listening for IPC Messages...");

    Task.Run(async () =>
    {
        while (true)
            await ipcSocket.AcceptAsync().ContinueWith(async acceptTask =>
            {
                using var clientSocket = acceptTask.Result;
                Console.WriteLine("Client connected for IPC!");

                var buffer = new byte[4096];
                while (true)
                {
                    int bytesRead = await clientSocket.ReceiveAsync(buffer, SocketFlags.None);
                    if (bytesRead == 0) break; // Client disconnected

                    string message = System.Text.Encoding.UTF8.GetString(buffer, 0, bytesRead);
                    IPCMessage ipcMessage = System.Text.Json.JsonSerializer.Deserialize<IPCMessage>(message)!;

                    Console.WriteLine($"Received IPC message: {ipcMessage}");
                    HandleIPCMessage(ipcMessage, provider);
                }

                Console.WriteLine("Messanger disconnected from IPC.");
            });
    });

    Console.CancelKeyPress += (_, e) =>
    {
        e.Cancel = true;
        provider.Dispose();
        Environment.Exit(1);
    };

    var uri = new Uri($"wss://{Config.ServerAddress}:{Config.ServerPort}/client-clock-in");
    provider.Serve(uri);

});



rootCommand.Add(importCommand);
rootCommand.Add(removeCommand);
rootCommand.Add(threadCommand);
rootCommand.Add(setDisplayNameCommand);
rootCommand.Add(serveCommand);

await rootCommand.Parse(args).InvokeAsync();
