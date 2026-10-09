using LMS.Api.application.common;
using LMS.Api.application.features.users;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController]
[Route("api/users")]
[Authorize]
public sealed class UsersController : ControllerBase
{
    [HttpGet("{userId:guid}/profile")]
    public async Task<ActionResult<UserProfileDto>> Profile(Guid userId, [FromServices] IUserService service, CancellationToken cancellationToken)
        => Ok(await service.GetProfileAsync(userId, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<UserProfileDto>>> List([FromQuery] UserQuery query, [FromServices] IUserService service, CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("search-for-chat")]
    public async Task<ActionResult<ChatUserSearchDto>> FindForChat(
        [FromQuery] string email,
        [FromServices] IUserService service,
        CancellationToken cancellationToken) =>
        Ok(await service.FindForChatByEmailAsync(email, cancellationToken));

    [HttpPut("me")]
    public async Task<ActionResult<UserProfileDto>> UpdateMe(
        [FromBody] UpdateMyProfileRequest request,
        [FromServices] IUserService service,
        CancellationToken cancellationToken) =>
        Ok(await service.UpdateMyProfileAsync(request, cancellationToken));

    [HttpPut("me/password")]
    public async Task<IActionResult> ChangeMyPassword(
        [FromBody] ChangeMyPasswordRequest request,
        [FromServices] IUserService service,
        CancellationToken cancellationToken)
    {
        await service.ChangeMyPasswordAsync(request, cancellationToken);
        return NoContent();
    }

    [HttpPost]
    public async Task<ActionResult<UserProfileDto>> Create([FromBody] AdminCreateUserRequest request, [FromServices] IUserService service, CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await service.CreateByAdminAsync(request, cancellationToken));

    [HttpPatch("{userId:guid}/status")]
    public async Task<ActionResult<UserProfileDto>> UpdateStatus(Guid userId, [FromBody] UpdateUserStatusRequest request, [FromServices] IUserService service, CancellationToken cancellationToken)
        => Ok(await service.UpdateStatusAsync(userId, request, cancellationToken));

    [HttpPut("{userId:guid}")]
    public async Task<ActionResult<UserProfileDto>> Update(Guid userId, [FromBody] AdminUpdateUserRequest request, [FromServices] IUserService service, CancellationToken cancellationToken)
        => Ok(await service.UpdateByAdminAsync(userId, request, cancellationToken));
}
