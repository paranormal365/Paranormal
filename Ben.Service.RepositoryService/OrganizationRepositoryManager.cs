using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService;

public class OrganizationRepositoryManager : IOrganizationRepositoryManager
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private IOrganizationRepository? _organizationRepository;
    private IOrganizationAddressRepository? _organizationAddressRepository;
    private IOrganizationAddressTypeRepository? _organizationAddressTypeRepository;
    private IOrganizationAddressMapConfigRepository? _organizationAddressMapConfigRepository;
    private IOrganizationAccessGrantRepository? _organizationAccessGrantRepository;
    private IOrganizationUserMembershipRepository? _organizationUserMembershipRepository;
    private IOrganizationMembershipRequestRepository? _organizationMembershipRequestRepository;
    private IOrganizationEmailRepository? _organizationEmailRepository;
    private IOrganizationEmailTypeRepository? _organizationEmailTypeRepository;
    private IOrganizationLinkRepository? _organizationLinkRepository;
    private IOrganizationLinkTypeRepository? _organizationLinkTypeRepository;
    private IOrganizationLogoRepository? _organizationLogoRepository;
    private IOrganizationNoteRepository? _organizationNoteRepository;
    private IOrganizationNoteTypeRepository? _organizationNoteTypeRepository;
    private IOrganizationPageRepository? _organizationPageRepository;
    private IOrganizationPhoneRepository? _organizationPhoneRepository;
    private IOrganizationPhoneTypeRepository? _organizationPhoneTypeRepository;
    private IOrganizationFileRepository? _organizationFileRepository;
    private IOrganizationFileDeleteLogRepository? _organizationFileDeleteLogRepository;
    private IOrgMemberGroupRepository? _orgMemberGroupRepository;
    private IOrgMemberGroupMembershipRepository? _orgMemberGroupMembershipRepository;
    private ICmsSectionRepository? _cmsSectionRepository;
    private ICmsPagePermissionRepository? _cmsPagePermissionRepository;

    public OrganizationRepositoryManager(IDbContextFactory<BenDataContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }

    public IOrganizationRepository OrganizationRepository => _organizationRepository ??= new OrganizationRepository(_dbContextFactory);
    public IOrganizationAddressRepository AddressRepository => _organizationAddressRepository ??= new OrganizationAddressRepository(_dbContextFactory);
    public IOrganizationAddressTypeRepository AddressTypeRepository => _organizationAddressTypeRepository ??= new OrganizationAddressTypeRepository(_dbContextFactory);
    public IOrganizationAddressMapConfigRepository AddressMapConfigRepository => _organizationAddressMapConfigRepository ??= new OrganizationAddressMapConfigRepository(_dbContextFactory);
    public IOrganizationAccessGrantRepository AccessGrantRepository => _organizationAccessGrantRepository ??= new OrganizationAccessGrantRepository(_dbContextFactory);
    public IOrganizationUserMembershipRepository UserMembershipRepository => _organizationUserMembershipRepository ??= new OrganizationUserMembershipRepository(_dbContextFactory);
    public IOrganizationMembershipRequestRepository MembershipRequestRepository => _organizationMembershipRequestRepository ??= new OrganizationMembershipRequestRepository(_dbContextFactory);
    public IOrganizationEmailRepository EmailRepository => _organizationEmailRepository ??= new OrganizationEmailRepository(_dbContextFactory);
    public IOrganizationEmailTypeRepository EmailTypeRepository => _organizationEmailTypeRepository ??= new OrganizationEmailTypeRepository(_dbContextFactory);
    public IOrganizationLinkRepository LinkRepository => _organizationLinkRepository ??= new OrganizationLinkRepository(_dbContextFactory);
    public IOrganizationLinkTypeRepository LinkTypeRepository => _organizationLinkTypeRepository ??= new OrganizationLinkTypeRepository(_dbContextFactory);
    public IOrganizationLogoRepository LogoRepository => _organizationLogoRepository ??= new OrganizationLogoRepository(_dbContextFactory);
    public IOrganizationNoteRepository NoteRepository => _organizationNoteRepository ??= new OrganizationNoteRepository(_dbContextFactory);
    public IOrganizationNoteTypeRepository NoteTypeRepository => _organizationNoteTypeRepository ??= new OrganizationNoteTypeRepository(_dbContextFactory);
    public IOrganizationPageRepository PageRepository => _organizationPageRepository ??= new OrganizationPageRepository(_dbContextFactory);
    public IOrganizationPhoneRepository PhoneRepository => _organizationPhoneRepository ??= new OrganizationPhoneRepository(_dbContextFactory);
    public IOrganizationPhoneTypeRepository PhoneTypeRepository => _organizationPhoneTypeRepository ??= new OrganizationPhoneTypeRepository(_dbContextFactory);
    public IOrganizationFileRepository FileRepository => _organizationFileRepository ??= new OrganizationFileRepository(_dbContextFactory);
    public IOrganizationFileDeleteLogRepository FileDeleteLogRepository => _organizationFileDeleteLogRepository ??= new OrganizationFileDeleteLogRepository(_dbContextFactory);
    public IOrgMemberGroupRepository MemberGroupRepository => _orgMemberGroupRepository ??= new OrgMemberGroupRepository(_dbContextFactory);
    public IOrgMemberGroupMembershipRepository MemberGroupMembershipRepository => _orgMemberGroupMembershipRepository ??= new OrgMemberGroupMembershipRepository(_dbContextFactory);
    public ICmsSectionRepository CmsSectionRepository => _cmsSectionRepository ??= new CmsSectionRepository(_dbContextFactory);
    public ICmsPagePermissionRepository CmsPagePermissionRepository => _cmsPagePermissionRepository ??= new CmsPagePermissionRepository(_dbContextFactory);
}
