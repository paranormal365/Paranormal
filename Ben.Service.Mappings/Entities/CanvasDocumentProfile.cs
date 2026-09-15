using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

/// <summary>
/// Maps a stored board to the two shapes the canvas API returns.
/// </summary>
/// <remarks>
/// <see cref="CanvasDocumentRecord.OrganizationId"/> comes from the case navigation, so a query
/// that forgets <c>Include(d =&gt; d.Case)</c> maps it as null rather than failing — the live check
/// of the running API (GET a case board, see a non-null organizationId) is what proves the
/// controller includes it. <c>CreatedByName</c> is filled by the controller in one query for a
/// whole list, never per row here.
/// </remarks>
public class CanvasDocumentProfile : Profile
{
    public CanvasDocumentProfile()
    {
        CreateMap<CanvasDocument, CanvasDocumentRecord>()
            .ForMember(d => d.OrganizationId, o => o.MapFrom(s => s.Case != null ? s.Case.OrganizationId : (Guid?)null))
            .ForMember(d => d.CreatedByName, o => o.Ignore())
            .ForMember(d => d.CanEdit, o => o.Ignore());

        CreateMap<CanvasDocument, CanvasDocumentSummaryRecord>()
            .ForMember(d => d.OrganizationId, o => o.MapFrom(s => s.Case != null ? s.Case.OrganizationId : (Guid?)null))
            .ForMember(d => d.CreatedByName, o => o.Ignore())
            .ForMember(d => d.CanEdit, o => o.Ignore());
    }
}
