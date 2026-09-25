using Ben.Data.WebApi.Services.Store;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers;

/// <summary>A store editor's refusal as the answer a controller gives (store sellers P3).</summary>
public static class StoreEditResults
{
    /// <summary>404 without a body, 409 and 400 with the editor's sentence.</summary>
    public static ActionResult Refused(this ControllerBase controller, StoreEditRefusal refusal) => refusal.Status switch
    {
        404 => controller.NotFound(),
        409 => controller.Conflict(refusal.Message),
        _ => controller.BadRequest(refusal.Message),
    };
}
