using API.DTOs;
using API.Entities;
using API.Interfaces;

namespace API.Data;

public class MessageRepository(AppDbContext context) : IMessageRepository
{
    public void AddMessage(Message message) => context.Messages.Add(message);

    public void DeleteMessage(Message message) => context.Messages.Remove(message);

    public async Task<Message?> GetMessageByIdAsync(string messageId) => await context.Messages.FindAsync(messageId);

    public Task<MessageDto[]> GetMessagesForMemberAsync(string memberId) => throw new NotImplementedException();

    public Task<IReadOnlyList<MessageDto>> GetMessageThread(string currentMemberId, string recipientId) => throw new NotImplementedException();

    public async Task<bool> SaveAllAsync() => await context.SaveChangesAsync() > 0;
}
