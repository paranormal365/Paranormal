using Ben.Data.Source.Context;
using Ben.Service.RepositoryService.EntityInterfaces;
using Ben.Service.RepositoryService.Repositories;
using Microsoft.EntityFrameworkCore;

namespace Ben.Service.RepositoryService;

public class OrganizationRepositoryManager : IOrganizationRepositoryManager
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private IOrganizationAddressRepository? _organizationAddressRepository;
    private IOrganizationAddressTypeRepository? _organizationAddressTypeRepository;
    private IOrganizationEmailRepository? _organizationEmailRepository;
    private IOrganizationEmailTypeRepository? _organizationEmailTypeRepository;
    private IOrganizationLinkRepository? _organizationLinkRepository;
    private IOrganizationLinkTypeRepository? _organizationLinkTypeRepository;
    private IOrganizationNoteRepository? _organizationNoteRepository;
    private IOrganizationNoteTypeRepository? _organizationNoteTypeRepository;
    private IOrganizationPageRepository? _organizationPageRepository;
    private IOrganizationPhoneRepository? _organizationPhoneRepository;
    private IOrganizationPhoneTypeRepository? _organizationPhoneTypeRepository;
    private IOrganizationRepository? _organizationRepository;

    public OrganizationRepositoryManager(IDbContextFactory<BenDataContext> dbContextFactory)
    {
        _dbContextFactory = dbContextFactory;
    }


    public IOrganizationAddressRepository AddressRepository => _organizationAddressRepository ??= new OrganizationAddressRepository(_dbContextFactory);
    public IOrganizationAddressTypeRepository AddressTypeRepository => _organizationAddressTypeRepository ??= new OrganizationAddressTypeRepository(_dbContextFactory);
    public IOrganizationEmailRepository EmailRepository => _organizationEmailRepository ??= new OrganizationEmailRepository(_dbContextFactory);
    public IOrganizationEmailTypeRepository EmailTypeRepository => _organizationEmailTypeRepository ??= new OrganizationEmailTypeRepository(_dbContextFactory);
    public IOrganizationLinkRepository LinkRepository => _organizationLinkRepository ??= new OrganizationLinkRepository(_dbContextFactory);
    public IOrganizationLinkTypeRepository LinkTypeRepository => _organizationLinkTypeRepository ??= new OrganizationLinkTypeRepository(_dbContextFactory);
    public IOrganizationNoteRepository NoteRepository => _organizationNoteRepository ??= new OrganizationNoteRepository(_dbContextFactory);
    public IOrganizationNoteTypeRepository NoteTypeRepository => _organizationNoteTypeRepository ??= new OrganizationNoteTypeRepository(_dbContextFactory);
    public IOrganizationPageRepository PageRepository => _organizationPageRepository ??= new OrganizationPageRepository(_dbContextFactory);
    public IOrganizationPhoneRepository PhoneRepository => _organizationPhoneRepository ??= new OrganizationPhoneRepository(_dbContextFactory);
    public IOrganizationPhoneTypeRepository PhoneTypeRepository => _organizationPhoneTypeRepository ??= new OrganizationPhoneTypeRepository(_dbContextFactory);
    public IOrganizationRepository OrganizationRepository => _organizationRepository ??= new OrganizationRepository(_dbContextFactory);
}
