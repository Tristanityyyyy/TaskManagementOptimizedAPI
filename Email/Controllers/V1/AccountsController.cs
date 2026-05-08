using Microsoft.AspNetCore.Mvc;
using TaskManagement.DTOs.Account;
using TaskManagement.DTOs.Common;
using TaskManagement.Services;

namespace TaskManagement.Controllers.V1;

[ApiController]
[Route("api/v1/accounts")]
public sealed class AccountsController : ApiControllerBase
{
    private readonly IAccountsService _accounts;

    public AccountsController(IAccountsService accounts)
    {
        _accounts = accounts;
    }

    [HttpGet]
    [ProducesResponseType(typeof(PagedResult<AccountListItemResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<AccountListItemResponse>>> List(
        [FromQuery] string? role = null,
        [FromQuery] bool? active = null,
        [FromQuery] int page = 1,
        [FromQuery] int pageSize = 100,
        CancellationToken cancellationToken = default)
        => Ok(await _accounts.ListAsync(CurrentAccount.Id, role, active, page, pageSize, cancellationToken));

    [HttpGet("me")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> Me(CancellationToken cancellationToken = default)
        => Ok(await _accounts.GetCurrentAsync(CurrentAccount.Id, cancellationToken));

    [HttpGet("stats/users")]
    [ProducesResponseType(typeof(IReadOnlyList<AccountStatsResponse>), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<AccountStatsResponse>>> UsersWithStats(
        CancellationToken cancellationToken = default)
        => Ok(await _accounts.GetUsersWithStatsAsync(CurrentAccount.Id, cancellationToken));

    [HttpGet("utils/generated-password")]
    [ProducesResponseType(typeof(GeneratedPasswordResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public ActionResult<GeneratedPasswordResponse> GeneratedPassword()
        => Ok(_accounts.GeneratePassword());

    [HttpGet("{id:int}")]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<AccountResponse>> Get(int id,
        CancellationToken cancellationToken = default)
    {
        var account = await _accounts.FindAsync(CurrentAccount.Id, id, cancellationToken);
        if (account == null)
            return NotFound();
        return Ok(account);
    }

    [HttpPost]
    [ProducesResponseType(typeof(AccountResponse), StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AccountResponse>> Create(
        [FromBody] CreateAccountRequest dto,
        CancellationToken cancellationToken = default)
    {
        var created = await _accounts.CreateAsync(CurrentAccount.Id, dto, cancellationToken);
        return CreatedAtAction(nameof(Get), new { id = created.Id }, created);
    }

    [HttpPatch("{id:int}")]
    [ProducesResponseType(typeof(UpdateAccountResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UpdateAccountResponse>> Update(int id,
        [FromBody] UpdateAccountRequest dto,
        CancellationToken cancellationToken = default)
        => Ok(await _accounts.UpdateAsync(CurrentAccount.Id, id, dto, cancellationToken));

    [HttpDelete("{id:int}")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Deactivate(int id,
        CancellationToken cancellationToken = default)
    {
        await _accounts.DeactivateAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpPatch("{id:int}/restore")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Restore(int id,
        CancellationToken cancellationToken = default)
    {
        await _accounts.ReactivateAsync(CurrentAccount.Id, id, cancellationToken);
        return NoContent();
    }

    [HttpDelete("{id:int}/profile-picture")]
    [ProducesResponseType(typeof(ProfilePictureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfilePictureResponse>> RemoveProfilePicture(int id,
        CancellationToken cancellationToken = default)
        => Ok(await _accounts.RemoveProfilePictureAsync(CurrentAccount.Id, id, cancellationToken));

    [HttpPost("{id:int}/profile-picture")]
    [ProducesResponseType(typeof(ProfilePictureResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProfilePictureResponse>> UploadProfilePicture(int id,
        IFormFile file,
        CancellationToken cancellationToken = default)
    {
        if (file == null)
            return BadRequest(new { error = "No file uploaded." });
        await using var stream = file.OpenReadStream();
        var result = await _accounts.UploadProfilePictureAsync(CurrentAccount.Id, id, stream, file.FileName,
            file.Length, cancellationToken);
        return Ok(result);
    }
}
