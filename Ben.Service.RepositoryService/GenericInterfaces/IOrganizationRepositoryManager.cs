using Ben.Service.RepositoryService.EntityInterfaces;

namespace Ben.Service.RepositoryService.GenericInterfaces;

public interface IOrganizationRepositoryManager
{
    IOrganizationRepository OrganizationRepository { get; }
    IOrganizationAddressRepository AddressRepository { get; }
    IOrganizationAddressTypeRepository AddressTypeRepository { get; }
    IOrganizationAddressMapConfigRepository AddressMapConfigRepository { get; }
    IOrganizationAccessGrantRepository AccessGrantRepository { get; }
    IOrganizationUserMembershipRepository UserMembershipRepository { get; }
    IOrganizationMembershipRequestRepository MembershipRequestRepository { get; }
    IOrganizationEmailRepository EmailRepository { get; }
    IOrganizationEmailTypeRepository EmailTypeRepository { get; }
    IOrganizationLinkRepository LinkRepository { get; }
    IOrganizationLinkTypeRepository LinkTypeRepository { get; }
    IOrganizationLogoRepository LogoRepository { get; }
    IOrganizationNoteRepository NoteRepository { get; }
    IOrganizationNoteTypeRepository NoteTypeRepository { get; }
    IOrganizationPageRepository PageRepository { get; }
    IOrganizationPhoneRepository PhoneRepository { get; }
    IOrganizationPhoneTypeRepository PhoneTypeRepository { get; }
    IOrganizationFileRepository FileRepository { get; }
    IOrganizationFileDeleteLogRepository FileDeleteLogRepository { get; }
    IOrgMemberGroupRepository MemberGroupRepository { get; }
    IOrgMemberGroupMembershipRepository MemberGroupMembershipRepository { get; }
    ICmsSectionRepository CmsSectionRepository { get; }
    ICmsPagePermissionRepository CmsPagePermissionRepository { get; }
}