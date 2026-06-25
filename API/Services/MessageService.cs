using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;

namespace API.Services;

public class MessageService(IMessageRepository messageRepository, IUserRepository userRepository) : IMessageService
{
    public async Task<Result<MessageDto>> CreateMessage(CreateMessageDto createMessageDto, string sender)
    {
        var content = createMessageDto.Content.Trim();
        if (string.IsNullOrWhiteSpace(content))
            return Result<MessageDto>.ValidationFailure("Content", "Content must not be empty");
        var user = await userRepository.GetUserByIdAsync(sender);
        var recipient = await userRepository.GetUserByIdAsync(createMessageDto.RecipientId);
        if (recipient == null || user == null || recipient.Id == sender)
            return Result<MessageDto>.Failure("Cannot send message", FailureReason.Conflict);

        var currTime = DateTimeOffset.UtcNow;

        var newMessage = new Message
        {
            SenderId = user.Id,
            Sender = user,
            RecipientId = recipient.Id,
            Recipient = recipient,
            Content = content,
            MessageSent = currTime
        };

        messageRepository.AddMessage(newMessage);

        var result = await messageRepository.SaveAllAsync();
        if (!result)
            return Result<MessageDto>.Failure("Failed to save message", FailureReason.InternalError);

        return Result<MessageDto>.Success(newMessage.ToDto());
    }
}
