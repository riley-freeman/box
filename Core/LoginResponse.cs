namespace Crayon.Box;

public readonly struct LoginResponse(Guid sessionId)
{
    public Guid SessionId { get; init; } = sessionId;

    public Guid SessionID() => SessionId;
}