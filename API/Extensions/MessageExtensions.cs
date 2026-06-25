using API.DTOs;
using API.Entities;

namespace API.Extensions;

public static class MessageExtensions
{
    public static MessageDto ToDto(this Message message)
    {
        return new MessageDto
        {
            Id = message.Id,
            SenderId = message.SenderId,
            SenderDisplayName = message.Sender.DisplayName,
            RecipientId = message.RecipientId,
            RecipientDisplayName = message.Recipient.DisplayName,
            Content = message.Content,
            DateRead = message.DateRead,
            MessageSent = message.MessageSent,
        };
    }
}
