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
}
