using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

namespace Ben.Service.Mappings.Entities;

public class AudioMarkerProfile : Profile
{
    public AudioMarkerProfile()
    {
        CreateMap<AudioMarker, AudioMarkerRecord>();
    }
}
