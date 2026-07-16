namespace Ben.Web.WebApp.Services.WebApi;

public interface IWebApiAuthService
{
    Task<bool> LoginAsync(string email, string password, CancellationToken token = default);
    Task<bool> RefreshIfNeededAsync(CancellationToken token = default);
    void Logout();

    Task<bool> ImpersonateAsync(Guid targetUserId, string targetUserEmail, CancellationToken token = default);
    void StopImpersonating();
}
