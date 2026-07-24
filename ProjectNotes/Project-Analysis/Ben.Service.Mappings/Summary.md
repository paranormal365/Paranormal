# Ben.Service.Mappings — Project Summary

**Type:** Class Library (`Microsoft.NET.Sdk`)  
**Target Framework:** net10.0

## Purpose

Contains all AutoMapper `Profile` classes that define entity→record mappings.  
Separating mappings into their own project keeps the data layer (`Ben.Data.Source`) free of DTO knowledge and keeps service models (`Ben.Service.Models`) free of EF entity knowledge.

## Dependencies

| Direction | Project |
|---|---|
| Depends on | Ben.Data.Common, Ben.Data.Source, Ben.Service.Models |
| Referenced by | Ben.Data.WebApi (registers all profiles via assembly scan) |

## How Profiles Are Registered

In `Ben.Data.WebApi/Program.cs`:
```csharp
builder.Services.AddAutoMapper(_ => { }, typeof(AppUserProfile).Assembly);
```
This registers every `Profile` class in the `Ben.Service.Mappings` assembly.

## Contents

| File | Description |
|---|---|
| [Profiles.md](Profiles.md) | All AutoMapper profile classes (Admin, Entities, Identity) including `UploadFileAudioConfigProfile`, `UploadFileRegionNoteProfile`, `UploadFileVoteProfile` |

## Profile Patterns

All profiles follow one of two conventions:

### Entity → Record
```csharp
public class OrganizationAdminProfile : Profile
{
    public OrganizationAdminProfile()
    {
        CreateMap<Organization, OrganizationAdminRecord>();
    }
}
```
AutoMapper by-convention maps identically-named public properties. No custom mapping is needed for standard fields.

### Custom mappings
A handful of profiles (e.g. `AppUserAdminProfile`) have explicit property mappings where source/destination names differ or require transformation.
