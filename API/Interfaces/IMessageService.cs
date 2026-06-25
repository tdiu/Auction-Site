using API.Core;
using API.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace API.Interfaces;

public interface IMessageService
{
    public Task<Result<MessageDto>> CreateMessage(CreateMessageDto createMessageDto, string sender);
}
