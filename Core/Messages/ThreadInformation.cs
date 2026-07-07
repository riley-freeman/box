namespace Crayon.Box.Messages;

public struct ThreadInformation(string title, string message, string author, DateTime timestamp)
{
    public string Title { get; set; } = title;
    public string Message { get; set; } = message;
    public string Author { get; set; } = author;
    public DateTime Timestamp { get; set; } = timestamp;
}