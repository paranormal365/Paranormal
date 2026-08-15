namespace Ben.Service.Mappings.Entities;

public class AppUserPhotoProfile : Profile
{
    public AppUserPhotoProfile()
    {
        CreateMap<AppUserPhoto, AppUserPhotoRecord>()
            // Null-safe: the navigation is only loaded on the listing queries that Include it.
            .ForMember(d => d.FileName, o => o.MapFrom(s => s.UploadFile.FileName));
    }
}
