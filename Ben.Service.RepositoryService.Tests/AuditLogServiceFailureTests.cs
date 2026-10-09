using Ben.Service.RepositoryService.Services;
using Xunit;

namespace Ben.Service.RepositoryService.Tests;

/// <summary>
/// An audit that cannot be written must not fail the request it describes.
/// </summary>
/// <remarks>
/// Controllers pass the audit's task to TryAuditAsync, which catches and logs. A comparison of two different shapes
/// used to throw before the task existed, so the change was saved and the request still answered 500 — every CMS
/// section reorder and draft publish (10/09/2026).
/// </remarks>
public class AuditLogServiceFailureTests
{
    [Fact]
    public async Task An_update_audit_of_two_different_shapes_fails_its_task_rather_than_throwing()
    {
        var service = new AuditLogService(null!);   // never reached: the comparison fails first

        var task = service.LogUpdateAsync("Thing", Guid.NewGuid(), new { A = 1 }, new { B = "x" }, Guid.NewGuid(), "test");

        await Assert.ThrowsAsync<ArgumentException>(() => task);
    }
}
