using Ben.Data.WebApi.Services.Audio.Voice;
using Ben.Service.Models.Entities;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Ben.Data.WebApi.Controllers.Entities;

/// <summary>
/// Whether EVP scans here measure voice-likeness (phase 9b), so the editor can say why a reading is
/// missing rather than leave a blank.
/// </summary>
[ApiController]
[Authorize]
public sealed class EvpVoiceModelController(IVoiceScorer scorer) : BenControllerBase
{
    [HttpGet("api/evp/voice-model")]
    public ActionResult<VoiceModelStatusRecord> Get()
        => Ok(new VoiceModelStatusRecord(scorer.IsAvailable, scorer.ModelName, scorer.UnavailableReason));
}
