using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Service.Mappings.Entities;

public class UploadFileVoteProfile : Profile
{
    public UploadFileVoteProfile()
    {
        CreateMap<UploadFileVote, UploadFileVoteRecord>();
    }
}
