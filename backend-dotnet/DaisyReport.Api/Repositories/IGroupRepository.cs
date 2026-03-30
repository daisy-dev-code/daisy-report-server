using DaisyReport.Api.Models;

namespace DaisyReport.Api.Repositories;

public interface IGroupRepository
{
    Task<Group?> GetByIdAsync(long id);
    Task<(List<Group> Groups, int Total)> ListAsync(int page, int pageSize, string? search);
    Task<long> CreateAsync(Group group);
    Task<bool> UpdateAsync(Group group);
    Task<bool> DeleteAsync(long id);
    Task<List<User>> GetMembersAsync(long groupId);
    Task<bool> AddMemberAsync(long groupId, long userId);
    Task<bool> RemoveMemberAsync(long groupId, long userId);
}
