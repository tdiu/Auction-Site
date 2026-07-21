using API.Core;
using API.DTOs;
using API.Entities;

namespace API.Interfaces;

public interface IMessageRepository
{
    void AddMessage(Message message);
    void DeleteMessage(Message message);
    Task<Message?> GetMessageByIdAsync(string messageId);
    Task<PagedList<MessageDto>> GetMessagesForMemberAsync(MessageParams messageParams);

    Task MarkThreadAsRead(string currentMemberId, string recipientId);
    Task<int> MarkMessageAsReadAsync(string messageId, string currentMemberId);
    Task<int> GetUnreadCountAsync(string memberId);
    Task<IReadOnlyList<MessageDto>> GetMessageThread(string currentMemberId, string recipientId);

    Task<bool> SaveAllAsync();
}
