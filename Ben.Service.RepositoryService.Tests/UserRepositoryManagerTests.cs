using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.Repositories;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Xunit;

namespace Ben.Service.RepositoryService.Tests
{
 public class UserRepositoryManagerTests
 {
 private IDbContextFactory<BenDataContext> CreateFactory()
 {
 var options = new DbContextOptionsBuilder<BenDataContext>()
 .UseInMemoryDatabase(Guid.NewGuid().ToString())
 .Options;
 return new PooledDbContextFactory<BenDataContext>(options);
 }

 [Fact]
 public void AppUserRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.AppUserRepository;
 var second = manager.AppUserRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void AddressRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.AddressRepository;
 var second = manager.AddressRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void AddressTypeRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.AddressTypeRepository;
 var second = manager.AddressTypeRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void EmailRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.EmailRepository;
 var second = manager.EmailRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void EmailTypeRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.EmailTypeRepository;
 var second = manager.EmailTypeRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void LinkRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.LinkRepository;
 var second = manager.LinkRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void LinkTypeRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.LinkTypeRepository;
 var second = manager.LinkTypeRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void MessageRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.MessageRepository;
 var second = manager.MessageRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void MessageToRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.MessageToRepository;
 var second = manager.MessageToRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void MessageTypeRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.MessageTypeRepository;
 var second = manager.MessageTypeRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void NoteRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.NoteRepository;
 var second = manager.NoteRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void NoteTypeRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.NoteTypeRepository;
 var second = manager.NoteTypeRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void PhoneRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.PhoneRepository;
 var second = manager.PhoneRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }

 [Fact]
 public void PhoneTypeRepository_Caches_Instance()
 {
 var manager = new AppUserRepositoryManager(CreateFactory());
 var first = manager.PhoneTypeRepository;
 var second = manager.PhoneTypeRepository;
 Assert.NotNull(first);
 Assert.Same(first, second);
 }
 }
}
