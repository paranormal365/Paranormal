using Ben.Data.Source.Context;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests
{
 public class RepositoryManagerTests
 {
 private IDbContextFactory<BenDataContext> CreateFactory()
 {
 var options = new DbContextOptionsBuilder<BenDataContext>()
 .UseInMemoryDatabase(Guid.NewGuid().ToString())
 .Options;
 return new PooledDbContextFactory<BenDataContext>(options);
 }

 [Fact]
 public void Organization_Property_Returns_Instance_And_Caches()
 {
 var factory = CreateFactory();
 var manager = new RepositoryManager(factory);

 var first = manager.Organization;
 var second = manager.Organization;

 Assert.NotNull(first);
 Assert.Same(first, second); // cached instance
 }

 [Fact]
 public void AppUser_Property_Returns_Instance_And_Caches()
 {
 var factory = CreateFactory();
 var manager = new RepositoryManager(factory);

 var first = manager.AppUser;
 var second = manager.AppUser;

 Assert.NotNull(first);
 Assert.Same(first, second);
 }
 }
}
