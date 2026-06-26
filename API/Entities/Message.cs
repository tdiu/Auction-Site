using System.ComponentModel.DataAnnotations;

namespace API.Entities;

public class Message
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public required string Content { get; set; }
    public DateTimeOffset? DateRead { get; set; }
    public DateTimeOffset MessageSent { get; set; }
    public bool SenderDeleted { get; set; }
    public bool RecipientDeleted { get; set; }

    // nav props
    public required string SenderId  { get; set; }
    public AppUser Sender { get; set; } = null!;
    public required string RecipientId  { get; set; }
    public AppUser Recipient { get; set; } = null!;
}
