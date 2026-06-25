using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;

namespace API.SignalR;

[Authorize]
public class MessageHub : Hub
{
    public override async Task OnConnectedAsync()
    {
        var httpContext = Context.GetHttpContext();
        var caller = Context.User?.FindFirstValue(ClaimTypes.NameIdentifier);
        var otherUser = httpContext?.Request.Query["userId"] ?? throw new HubException("Other user not found");

        var groupName = GetGroupName(caller, otherUser);
        // await Groups.AddToGroupAsync(Context.ConnectionId, groupName);

        await base.OnConnectedAsync();

    }

    private static string GetGroupName(string? caller, string? other)
    {
        var stringCompare = string.CompareOrdinal(caller, other) < 0;
        return stringCompare ? $"{caller} - {other}" : $"{other} - {caller}";
    }
}
