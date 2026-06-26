using API.Core;
using API.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace API.Interfaces;

public interface IMessageService
{
    public Task<Result<MessageDto>> CreateMessage(CreateMessageDto createMessageDto, string sender);
    Task<PagedList<MessageDto>> GetMessagesByContainer(MessageParams messageParams);
    Task<IReadOnlyList<MessageDto>> GetMessageThread(string memberId, string recipientId);
}
