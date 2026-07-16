using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService;

public class AppUserRepositoryManager : IAppUserRepositoryManager
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private IAppUserRepository? _appUserRepository;
    private IUserAddressRepository? _userAddressRepository;
    private IUserAddressTypeRepository? _userAddressTypeRepository;
    private IUserEmailRepository? _userEmailRepository;
    private IUserEmailTypeRepository? _userEmailTypeRepository;
    private IUserLinkRepository? _userLinkRepository;
    private IUserLinkTypeRepository? _userLinkTypeRepository;
    private IUserMessageRepository? _userMessageRepository;
    private IUserMessageToRepository? _userMessageToRepository;
    private IUserMessageTypeRepository? _userMessageTypeRepository;
    private IUserNoteRepository? _userNoteRepository;
    private IUserNoteTypeRepository? _userNoteTypeRepository;
    private IUserPhoneRepository? _userPhoneRepository;
    private IUserPhoneTypeRepository? _userPhoneTypeRepository;


    public AppUserRepositoryManager(IDbContextFactory<BenDataContext> dbContextFactory) 
    {
        _dbContextFactory = dbContextFactory;
    }


    public IAppUserRepository AppUserRepository => _appUserRepository ??= new AppUserRepository(_dbContextFactory);
    public IUserAddressRepository AddressRepository => _userAddressRepository ??= new UserAddressRepository(_dbContextFactory);
    public IUserAddressTypeRepository AddressTypeRepository => _userAddressTypeRepository ??= new UserAddressTypeRepository(_dbContextFactory);
    public IUserEmailRepository EmailRepository => _userEmailRepository ??= new UserEmailRepository(_dbContextFactory);
    public IUserEmailTypeRepository EmailTypeRepository => _userEmailTypeRepository ??= new UserEmailTypeRepository(_dbContextFactory);
    public IUserLinkRepository LinkRepository => _userLinkRepository ??= new UserLinkRepository(_dbContextFactory);
    public IUserLinkTypeRepository LinkTypeRepository => _userLinkTypeRepository ??= new UserLinkTypeRepository(_dbContextFactory);
    public IUserMessageRepository MessageRepository => _userMessageRepository ??= new UserMessageRepository(_dbContextFactory);
    public IUserMessageToRepository MessageToRepository => _userMessageToRepository ??= new UserMessageToRepository(_dbContextFactory);
    public IUserMessageTypeRepository MessageTypeRepository => _userMessageTypeRepository ??= new UserMessageTypeRepository(_dbContextFactory);
    public IUserNoteRepository NoteRepository => _userNoteRepository ??= new UserNoteRepository(_dbContextFactory);
    public IUserNoteTypeRepository NoteTypeRepository => _userNoteTypeRepository ??= new UserNoteTypeRepository(_dbContextFactory);
    public IUserPhoneRepository PhoneRepository => _userPhoneRepository ??= new UserPhoneRepository(_dbContextFactory);
    public IUserPhoneTypeRepository PhoneTypeRepository => _userPhoneTypeRepository ??= new UserPhoneTypeRepository(_dbContextFactory);

}
