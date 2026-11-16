using Broker.Api.Contracts;
using Broker.Api.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace Broker.Api.Controllers;

/// <summary>Profil zalogowanego użytkownika (dane do wyceny i wynajmu).</summary>
[Route("api/v1/profile")]
[Authorize]
public sealed class ProfileController(ProfileService profiles) : ApiControllerBase
{
    /// <summary>Profil. Przy pierwszym wejściu tworzony z danych konta (imię, nazwisko, email).</summary>
    [HttpGet]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileResponse>> Get(CancellationToken cancellationToken)
    {
        var result = await profiles.GetAsync(cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }

    /// <summary>Zapis profilu (data urodzenia, data prawa jazdy, lokalizacja, imię, nazwisko, email). Wymaga X-CSRF: 1.</summary>
    [HttpPut]
    [Consumes("application/json")]
    [ProducesResponseType<ProfileResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ValidationProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<ProfileResponse>> Update(UpdateProfileRequest request, CancellationToken cancellationToken)
    {
        var result = await profiles.UpdateAsync(request, cancellationToken);
        return result.IsSuccess ? Ok(result.Value) : ErrorResult(result.Error);
    }
}
