using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class ExperienceCategoryProfile : Profile
{
    public ExperienceCategoryProfile()
    {
        CreateMap<ExperienceCategory, ExperienceCategoryRecord>();
    }
}
