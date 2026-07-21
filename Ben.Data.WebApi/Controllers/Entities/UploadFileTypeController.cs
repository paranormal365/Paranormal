using AutoMapper;
using Ben.Data.Source.Context;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Ben.Data.WebApi.Controllers.Entities;

[Route("api/upload-file-types")]
public sealed class UploadFileTypeController : EntityReadControllerBase<UploadFileType, UploadFileTypeRecord>
{
    private readonly IDbContextFactory<BenDataContext> _dbContextFactory;
    private readonly IMapper _mapper;

    public UploadFileTypeController(IDbContextFactory<BenDataContext> dbContextFactory, IMapper mapper)
        : base(dbContextFactory, mapper)
    {
        _dbContextFactory = dbContextFactory;
        _mapper = mapper;
    }

    public override async Task<ActionResult<IEnumerable<UploadFileTypeRecord>>> GetAll(CancellationToken cancellationToken)
    {
        await using var db = await _dbContextFactory.CreateDbContextAsync(cancellationToken);
        var entities = await db.UploadFileTypes
            .Include(t => t.AllowedExtensions)
            .AsNoTracking()
            .ToListAsync(cancellationToken);
        return Ok(_mapper.Map<IEnumerable<UploadFileTypeRecord>>(entities));
    }
}
