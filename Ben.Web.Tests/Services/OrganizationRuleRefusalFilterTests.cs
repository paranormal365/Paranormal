using Ben.Data.WebApi.Services;
using Ben.Service.RepositoryService.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Abstractions;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.AspNetCore.Routing;
using Xunit;

namespace Ben.Web.Tests.Services;

/// <summary>
/// A group rule's refusal (a taken web address, the last owner) reached the error handler and answered 500
/// (site audit, 10/09/2026). It answers with its sentence; anything else is still a fault.
/// </summary>
public sealed class OrganizationRuleRefusalFilterTests
{
    private static ExceptionContext Context(Exception ex)
        => new(new ActionContext(new DefaultHttpContext(), new RouteData(), new ActionDescriptor()), [])
        { Exception = ex };

    [Fact]
    public void A_rule_refusal_answers_400_with_its_sentence()
    {
        var context = Context(new OrganizationRuleException("That web address is taken."));
        new OrganizationRuleRefusalFilter().OnException(context);
        Assert.True(context.ExceptionHandled);
        Assert.Equal("That web address is taken.", Assert.IsType<BadRequestObjectResult>(context.Result).Value);
    }

    [Fact]
    public void A_rank_refusal_answers_403()
    {
        var context = Context(new OrganizationRuleDeniedException("Not at your rank."));
        new OrganizationRuleRefusalFilter().OnException(context);
        Assert.Equal(StatusCodes.Status403Forbidden, Assert.IsType<ObjectResult>(context.Result).StatusCode);
    }

    [Fact]
    public void Any_other_exception_is_left_to_the_error_handler()
    {
        var context = Context(new InvalidOperationException("Sequence contains no elements"));
        new OrganizationRuleRefusalFilter().OnException(context);
        Assert.False(context.ExceptionHandled);
        Assert.Null(context.Result);
    }
}
