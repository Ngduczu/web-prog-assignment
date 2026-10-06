using LMS.Api.application.features.enrollments;
using LMS.Api.application.common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController]
[Route("api")]
[Authorize]
public sealed class EnrollmentsController : ControllerBase
{
    [HttpPost("courses/{courseId:guid}/enrollments")]
    public async Task<ActionResult<EnrollmentDto>> RequestEnrollment(
        Guid courseId,
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await service.RequestAsync(courseId, cancellationToken));

    [HttpGet("courses/{courseId:guid}/enrollments")]
    public async Task<ActionResult<PagedResult<EnrollmentDto>>> ListByCourse(
        Guid courseId,
        [FromQuery] EnrollmentQuery query,
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => Ok(await service.ListByCourseAsync(courseId, query, cancellationToken));

    [HttpGet("enrollments/mine")]
    public async Task<ActionResult<IReadOnlyList<EnrollmentDto>>> ListMine(
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => Ok(await service.ListMineAsync(cancellationToken));

    [HttpPost("enrollments/{enrollmentId:guid}/accept")]
    public async Task<ActionResult<EnrollmentDto>> Accept(
        Guid enrollmentId,
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => Ok(await service.AcceptAsync(enrollmentId, cancellationToken));

    [HttpPost("enrollments/{enrollmentId:guid}/reject")]
    public async Task<ActionResult<EnrollmentDto>> Reject(
        Guid enrollmentId,
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => Ok(await service.RejectAsync(enrollmentId, cancellationToken));

    [HttpPost("enrollments/{enrollmentId:guid}/cancel")]
    public async Task<ActionResult<EnrollmentDto>> Cancel(
        Guid enrollmentId,
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => Ok(await service.CancelAsync(enrollmentId, cancellationToken));

    [HttpPost("enrollments/{enrollmentId:guid}/revoke")]
    public async Task<ActionResult<EnrollmentDto>> Revoke(
        Guid enrollmentId,
        [FromServices] IEnrollmentService service,
        CancellationToken cancellationToken)
        => Ok(await service.RevokeAsync(enrollmentId, cancellationToken));
}
