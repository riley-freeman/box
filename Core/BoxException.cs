public class BoxException : Exception
{
    public String Details { get; set; }
    public int Code { get; set; }

    public BoxException(int code, string reason) : base(reason)
    {
        Code = code;
        Details = reason;
    }
}