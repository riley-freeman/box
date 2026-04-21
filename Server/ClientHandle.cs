using System.Net.WebSockets;

namespace Crayon.Box;

public class ClientHandle(WebSocket webSocket)
{
    private ulong NextMessageID = 1;

    private readonly Dictionary<ulong, TaskCompletionSource<Message>> PendingReplies = [];

    public void Close()
    {
        try
        {
            webSocket.CloseAsync(WebSocketCloseStatus.NormalClosure, string.Empty, CancellationToken.None).Wait();
            webSocket.Dispose();
        }
        catch (AggregateException) { }
    }

    public async Task<Message> SendMessage(Message message)
    {
        var h = message.Header;
        h.ID = NextMessageID++;
        message.Header = h;

        var tcs = new TaskCompletionSource<Message>();
        PendingReplies[h.ID] = tcs;

        await message.SendAsync(webSocket);

        return await tcs.Task;
    }

    public void HandleMessage(Message message)
    {
        if (PendingReplies.TryGetValue(message.Header.ID, out var tcs))
        {
            tcs.SetResult(message);
            PendingReplies.Remove(message.Header.ID);
        }
    }
}
