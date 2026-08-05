using AutoMapper;
using Ben.Data.Source.Entities;
using Ben.Service.Models.Entities;

public class VideoProjectProfile : Profile
{
    public VideoProjectProfile()
    {
        CreateMap<VideoProject, VideoProjectRecord>();
    }
}
