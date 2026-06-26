namespace API.Core;

public class MessageParams
{
    public string? MemberId { get; set; }
    public string Container { get; set; } = "Inbox";
    public int Page { get; set; } = 1;
    public int PageSize { get; set; } = 10;
}
