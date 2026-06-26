using API.Core;
using API.DTOs;
using API.Entities;
using API.Extensions;
using API.Interfaces;
using Microsoft.EntityFrameworkCore;

namespace API.Data;

public class MessageRepository(AppDbContext context) : IMessageRepository
{
    public void AddMessage(Message message) => context.Messages.Add(message);

    public void DeleteMessage(Message message) => context.Messages.Remove(message);

    public async Task<Message?> GetMessageByIdAsync(string messageId) => await context.Messages.FindAsync(messageId);

    public Task<PagedList<MessageDto>> GetMessagesForMemberAsync(MessageParams messageParams)
    {
        var query = context.Messages
            .OrderByDescending(x => x.MessageSent)
            .AsQueryable();

        query = messageParams.Container switch
        {
            "Outbox" => query.Where(x => x.SenderId == messageParams.MemberId),
            _ => query.Where(x => x.RecipientId == messageParams.MemberId)
        };

        return PagedList<MessageDto>.CreateAsync(
            query.ProjectToDto(), messageParams.Page, messageParams.PageSize);
    }

    public Task MarkThreadAsRead(string currentMemberId, string recipientId) =>
        context.Messages
            .Where(x => x.RecipientId == currentMemberId
                        && x.SenderId == recipientId && x.DateRead == null)
            .ExecuteUpdateAsync(setters => setters.SetProperty(x => x.DateRead, DateTimeOffset.UtcNow));

    public async Task<IReadOnlyList<MessageDto>> GetMessageThread(string currentMemberId, string recipientId) =>
        await context.Messages
            .Where(x => (x.SenderId == recipientId && x.RecipientId == currentMemberId && !x.RecipientDeleted)
                        || (x.SenderId == currentMemberId && x.RecipientId == recipientId && !x.SenderDeleted))
            .OrderBy(x => x.MessageSent)
            .ProjectToDto()
            .ToListAsync();

    public async Task<bool> SaveAllAsync() => await context.SaveChangesAsync() > 0;
}
