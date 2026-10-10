using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace Ben.Data.WebApi.Services;

/// <summary>
/// A group rule's refusal answered as one: 400 or 403 with its sentence, not a 500.
/// </summary>
/// <remarks>
/// Only these two exception types, which carry sentences written to be shown. Any other exception is a
/// fault and still goes to the error handler (site audit, 10/09/2026).
/// </remarks>
public sealed class OrganizationRuleRefusalFilter : IExceptionFilter
{
    public void OnException(ExceptionContext context)
    {
        switch (context.Exception)
        {
            case OrganizationRuleException rule:
                context.Result = new BadRequestObjectResult(rule.Message);
                context.ExceptionHandled = true;
                break;
            case OrganizationRuleDeniedException denied:
                context.Result = new ObjectResult(denied.Message) { StatusCode = StatusCodes.Status403Forbidden };
                context.ExceptionHandled = true;
                break;
        }
    }
}
