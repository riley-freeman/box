
using System.Net;
using Crayon.Box;

public struct MessageHeader()
{
    public int Time { get; } = DateTime.Now.Millisecond;
    public ulong ID { get; set; } = 0;
    public MessageType Type { get; set; } = MessageType.Invalid;

    public int Part { get; set; } = 0;
    public int TotalParts { get; set; } = 0;

    public HttpStatusCode StatusCode { get; set; } = HttpStatusCode.OK;

}