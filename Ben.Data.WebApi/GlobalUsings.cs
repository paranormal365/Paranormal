global using Ben.Data.Common.Constants;
global using Ben.Data.Source.Context;
global using Ben.Data.Source.Entities;
global using Ben.Service.RepositoryService;
global using Ben.Service.RepositoryService.GenericInterfaces;
global using Microsoft.AspNetCore.Identity;
global using Microsoft.EntityFrameworkCore;
//global using Serilog;

[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("Ben.Web.Tests")]
// Item 242's measuring bench (tools/EvpLab, outside Ben.slnx) grades the real EvpDetector rather than a copy of it.
[assembly: System.Runtime.CompilerServices.InternalsVisibleTo("EvpLab")]
