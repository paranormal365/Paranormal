namespace Ben.Service.RepositoryService.GenericInterfaces;

public interface IRepositoryManager
{
    IOrganizationRepositoryManager Organization { get; }
    IAppUserRepositoryManager AppUser { get; }
    Task<int> SaveChangesAsync(CancellationToken token = default);
}
