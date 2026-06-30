using System.Security.Claims;
using API.Core;
using API.DTOs;
using API.Interfaces;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace API.Controllers;

public class MessagesController(IMessageService messageService) : BaseApiController
{
    [HttpPost]
    [Authorize]
    public async Task<IActionResult> CreateMessage(CreateMessageDto createMessageDto)
    {
        var sender = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(sender))
            return Unauthorized();

        var res = await messageService.CreateMessage(createMessageDto, sender);
        if (!res.IsSuccess)
            return HandleFailure(res);

        return Ok(res.Value);
    }

    [HttpGet]
    [Authorize]
    public async Task<ActionResult<PagedList<MessageDto>>> GetMessagesByContainer([FromQuery] MessageParams messageParams)
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberId))
            return Unauthorized();

        messageParams.MemberId = memberId;
        var messages = await messageService.GetMessagesByContainer(messageParams);
        return Ok(messages);
    }

    [HttpGet("thread/{recipientId}")]
    [Authorize]
    public async Task<ActionResult<IReadOnlyList<MessageDto>>> GetMessagesThread(string recipientId)
    {
        var memberId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(memberId))
            return Unauthorized();

        return Ok(await messageService.GetMessageThread(memberId, recipientId));
    }
}
