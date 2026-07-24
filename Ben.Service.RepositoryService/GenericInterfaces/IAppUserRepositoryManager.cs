using Ben.Service.RepositoryService.EntityInterfaces;

namespace Ben.Service.RepositoryService.GenericInterfaces;

public interface IAppUserRepositoryManager
{
    IAppUserRepository AppUserRepository { get; }
    IUserAddressRepository AddressRepository { get; }
    IUserAddressTypeRepository AddressTypeRepository { get; }
    IUserEmailRepository EmailRepository { get; }
    IUserEmailTypeRepository EmailTypeRepository { get; }
    IUserLinkRepository LinkRepository { get; }
    IUserLinkTypeRepository LinkTypeRepository { get; }
    IUserMessageRepository MessageRepository { get; }
    IUserMessageToRepository MessageToRepository { get; }
    IUserMessageTypeRepository MessageTypeRepository { get; }
    IUserNoteRepository NoteRepository { get; }
    IUserNoteTypeRepository NoteTypeRepository { get; }
    IUserPhoneRepository PhoneRepository { get; }
    IUserPhoneTypeRepository PhoneTypeRepository { get; }
    IUploadFileRepository UploadFileRepository { get; }
    IUploadFileTypeRepository UploadFileTypeRepository { get; }
    IUploadFileTypeExtensionRepository UploadFileTypeExtensionRepository { get; }
    IUploadFileAudioConfigRepository UploadFileAudioConfigRepository { get; }
    IUploadFileOrganizationShareRepository UploadFileShareRepository { get; }
    IUploadFilePermissionRequestRepository UploadFilePermissionRequestRepository { get; }
    IUploadFileRegionNoteRepository UploadFileRegionNoteRepository { get; }
    IUploadFileVoteRepository UploadFileVoteRepository { get; }
}
