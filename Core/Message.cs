using System.Net;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;
using Crayon.Box.Messages;

namespace Crayon.Box;


public class Message
{
    public MessageHeader Header { get; set; }
    public MessageType Type => Header.Type;
    public List<byte> RawData { get; set; } = new List<byte>();

    private static Dictionary<ulong, Message> MESSAGES_IN_PROCESSING = new Dictionary<ulong, Message>();

    public Message(MessageType t, byte[] data)
    {
        Header = new MessageHeader
        {
            Type = t
        };
        RawData = [.. data];
    }

    public Message(MessageHeader header, byte[] data)
    {
        Header = header;
        RawData = [.. data];
    }

    public Message(Message parent, MessageType t, byte[] data)
    {
        Header = new MessageHeader
        {
            Type = t,
            ID = parent.Header.ID,
        };
        RawData = [.. data];
    }

    public Message(Message parent, MessageType t, HttpStatusCode code, byte[] data)
    {
        Header = new MessageHeader
        {
            Type = t,
            StatusCode = code,
            ID = parent.Header.ID,
        };
        RawData = [.. data];
    }

    public async Task SendAsync(WebSocket socket)
    {
        var body = RawData.ToArray();

        // Estimate header overhead using worst-case Part/TotalParts digit counts
        // so the chunk size stays consistent across all parts.
        var worstCaseHeader = Header;
        worstCaseHeader.Part = 9999;
        worstCaseHeader.TotalParts = 9999;
        var headerOverhead = Encoding.UTF8.GetByteCount(JsonSerializer.Serialize(worstCaseHeader) + "\n");
        var chunkSize = Config.MaxBufferSize - headerOverhead;

        if (chunkSize <= 0)
            throw new InvalidOperationException("MaxBufferSize is too small to fit the message header.");

        var totalParts = body.Length == 0 ? 1 : (int)Math.Ceiling((double)body.Length / chunkSize);

        for (int part = 1; part <= totalParts; part++)
        {
            var header = Header;
            header.Part = part;
            header.TotalParts = totalParts;

            var headerBytes = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header) + "\n");
            var bodyStart = (part - 1) * chunkSize;
            var bodyChunk = body[bodyStart..Math.Min(bodyStart + chunkSize, body.Length)];

            var buffer = new byte[headerBytes.Length + bodyChunk.Length];
            headerBytes.CopyTo(buffer, 0);
            bodyChunk.CopyTo(buffer, headerBytes.Length);

            // Each protocol part has its own header, so it must also be a complete
            // WebSocket message. Sending all parts as one fragmented WebSocket
            // message lets a receiver observe a continuation without a header.
            await socket.SendAsync(new ArraySegment<byte>(buffer), WebSocketMessageType.Binary, true, CancellationToken.None);
        }
    }

    public async static Task SendCloseAsync(WebSocket socket, WebSocketCloseStatus status, string desc)
    {
        await socket.CloseAsync(status, desc, CancellationToken.None);
    }

    public async static Task SendCloseOutputAsync(WebSocket socket, WebSocketCloseStatus status, string desc)
    {
        await socket.CloseOutputAsync(status, desc, CancellationToken.None);
    }

    public static async Task<Message?> ReceiveAsync(WebSocket socket)
    {
        var buffer = new byte[Config.MaxBufferSize];
        using var payloadStream = new MemoryStream();
        WebSocketReceiveResult result;
        WebSocketMessageType? messageType = null;

        do
        {
            result = await socket.ReceiveAsync(new ArraySegment<byte>(buffer), CancellationToken.None);

            if (result.MessageType == WebSocketMessageType.Close)
            {
                var closeInfo = new CloseInformation
                {
                    CloseStatus = result.CloseStatus ?? WebSocketCloseStatus.Empty,
                    Description = result.CloseStatusDescription ?? string.Empty,
                };

                var json = JsonSerializer.Serialize(closeInfo);
                var encoded = Encoding.UTF8.GetBytes(json);
                return new Message(MessageType.CloseRequest, encoded);
            }

            messageType ??= result.MessageType;
            if (result.MessageType != messageType)
                throw new InvalidDataException("A WebSocket message changed type between fragments.");

            payloadStream.Write(buffer, 0, result.Count);
        }
        while (!result.EndOfMessage);

        var payload = payloadStream.ToArray();
        switch (messageType)
        {
            case WebSocketMessageType.Text:
                return FromBytes(payload, payload.Length);

            case WebSocketMessageType.Binary:
                {
                    var header_end = Array.IndexOf(payload, (byte)'\n');
                    if (header_end < 0)
                        throw new InvalidDataException("Received a binary protocol frame without a header delimiter.");

                    var header_string = Encoding.UTF8.GetString(payload, 0, header_end);

                    var body = payload[(header_end + 1)..];

                    var header = JsonSerializer.Deserialize<MessageHeader>(header_string);

                    if (!MESSAGES_IN_PROCESSING.ContainsKey(header.ID))
                    {
                        MESSAGES_IN_PROCESSING[header.ID] = new Message(header, body);
                    }
                    else
                    {
                        MESSAGES_IN_PROCESSING[header.ID].RawData.AddRange(body);
                    }


                    if (header.Part == header.TotalParts)
                    {
                        var message = MESSAGES_IN_PROCESSING[header.ID];
                        MESSAGES_IN_PROCESSING.Remove(header.ID);
                        return message;
                    }

                    return null;
                }
        }

        return null;
    }

    public static Message? FromBytes(byte[] buffer, int count)
    {
        var json = Encoding.UTF8.GetString(buffer, 0, count);
        var message = JsonSerializer.Deserialize<Message>(json);
        return message;
    }
}
