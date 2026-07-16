using Ben.Service.RepositoryService.EntityInterfaces;

namespace Ben.Service.RepositoryService.GenericInterfaces;

public interface IOrganizationRepositoryManager
{
    IOrganizationAddressRepository AddressRepository { get; }
    IOrganizationAddressTypeRepository AddressTypeRepository { get; }
    IOrganizationEmailRepository EmailRepository { get; }
    IOrganizationEmailTypeRepository EmailTypeRepository { get; }
    IOrganizationLinkRepository LinkRepository { get; }
    IOrganizationLinkTypeRepository LinkTypeRepository { get; }
    IOrganizationNoteRepository NoteRepository { get; }
    IOrganizationNoteTypeRepository NoteTypeRepository { get; }
    IOrganizationPageRepository PageRepository { get; }
    IOrganizationPhoneRepository PhoneRepository { get; }
    IOrganizationPhoneTypeRepository PhoneTypeRepository { get; }
    IOrganizationRepository OrganizationRepository { get; }
}