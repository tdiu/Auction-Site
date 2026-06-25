namespace API.DTOs;

public class MessageDto
{
    public required string Id { get; set; }
    public required string SenderId { get; set; }
    public required string SenderDisplayName { get; set; }
    public required string RecipientId { get; set; }
    public required string RecipientDisplayName { get; set; }
    public required string Content { get; set; }
    public DateTime? DateRead { get; set; }
    public DateTimeOffset MessageSent { get; set; }
}
