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
    private IUploadFileRepository? _uploadFileRepository;
    private IUploadFileTypeRepository? _uploadFileTypeRepository;
    private IUploadFileTypeExtensionRepository? _uploadFileTypeExtensionRepository;
    private IUploadFileAudioConfigRepository? _uploadFileAudioConfigRepository;
    private IUploadFileOrganizationShareRepository? _uploadFileShareRepository;
    private IUploadFilePermissionRequestRepository? _uploadFilePermissionRequestRepository;
    private IUploadFileRegionNoteRepository? _uploadFileRegionNoteRepository;
    private IUploadFileVoteRepository? _uploadFileVoteRepository;
    private IAudioMarkerRepository? _audioMarkerRepository;

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
    public IUploadFileRepository UploadFileRepository => _uploadFileRepository ??= new UploadFileRepository(_dbContextFactory);
    public IUploadFileTypeRepository UploadFileTypeRepository => _uploadFileTypeRepository ??= new UploadFileTypeRepository(_dbContextFactory);
    public IUploadFileTypeExtensionRepository UploadFileTypeExtensionRepository => _uploadFileTypeExtensionRepository ??= new UploadFileTypeExtensionRepository(_dbContextFactory);
    public IUploadFileAudioConfigRepository UploadFileAudioConfigRepository => _uploadFileAudioConfigRepository ??= new UploadFileAudioConfigRepository(_dbContextFactory);
    public IUploadFileOrganizationShareRepository UploadFileShareRepository => _uploadFileShareRepository ??= new UploadFileOrganizationShareRepository(_dbContextFactory);
    public IUploadFilePermissionRequestRepository UploadFilePermissionRequestRepository => _uploadFilePermissionRequestRepository ??= new UploadFilePermissionRequestRepository(_dbContextFactory);
    public IUploadFileRegionNoteRepository UploadFileRegionNoteRepository => _uploadFileRegionNoteRepository ??= new UploadFileRegionNoteRepository(_dbContextFactory);
    public IUploadFileVoteRepository UploadFileVoteRepository => _uploadFileVoteRepository ??= new UploadFileVoteRepository(_dbContextFactory);
    public IAudioMarkerRepository AudioMarkerRepository => _audioMarkerRepository ??= new AudioMarkerRepository(_dbContextFactory);
}
