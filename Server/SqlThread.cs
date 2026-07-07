namespace Crayon.Box;

struct SqlThread(string username, string title, string message, long time)
{
    public string Username { get; set; } = username;
    public string Title { get; set; } = title;
    public string Message { get; set; } = message;
    public long Time { get; set; } = time;
}