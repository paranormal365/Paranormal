using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class ExperienceTypeProfile : Profile
{
    public ExperienceTypeProfile()
    {
        CreateMap<ExperienceType, ExperienceTypeRecord>();
    }
}
