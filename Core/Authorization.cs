namespace Crayon.Box;

public struct Authorization(Guid sessionId)
{
    public Guid SessionID { get; set; } = sessionId;
}