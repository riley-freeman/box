using System.Net.WebSockets;
using Crayon.Box;
using Microsoft.Extensions.ObjectPool;

public class ServerHandle
{
    public ClientWebSocket Socket { get; set; }

    public static ulong NextMessageID = 1;

    private Dictionary<ulong, Action> CallbackActions = new Dictionary<ulong, Action>();

    public ServerHandle(ClientWebSocket webSocket)
    {
        Socket = webSocket;
    }


    public async Task SendMessageAsync(Message message, Action? callback)
    {
        NextMessageID++;
        MessageHeader header = message.Header;
        header.ID = NextMessageID - 1;

        if (callback != null)
        {
            CallbackActions.Add(header.ID, callback);
        }
        await message.SendAsync(Socket);
    }

    public async Task<Message?> ReceiveMessageAsync()
    {
        var message = await Message.ReceiveAsync(Socket);
        if (message != null)
            HandleIncommingMessage(message);
        return message;
    }

    public void HandleIncommingMessage(Message message)
    {
        ulong id = message.Header.ID;
        if (CallbackActions.ContainsKey(id))
        {
            CallbackActions[message.Header.ID]();
            CallbackActions.Remove(id);
        }
    }


}