using LMS.Api.application.features.courses;
using LMS.Api.application.common;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController]
[Route("api/courses")]
[Authorize]
public sealed class CoursesController : ControllerBase
{
    [HttpPost]
    public async Task<ActionResult<CourseDto>> Create(
        [FromBody] CreateCourseRequest request,
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
        => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(request, cancellationToken));

    [HttpGet]
    public async Task<ActionResult<PagedResult<CourseDto>>> List(
        [FromQuery] CourseQuery query,
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
        => Ok(await service.ListAsync(query, cancellationToken));

    [HttpGet("mine")]
    public async Task<ActionResult<IReadOnlyList<CourseDto>>> ListMine(
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
        => Ok(await service.ListMineAsync(cancellationToken));

    [HttpGet("{courseId:guid}")]
    public async Task<ActionResult<CourseDto>> GetById(
        Guid courseId,
        [FromQuery] bool includeDeleted,
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
        => Ok(await service.GetByIdAsync(courseId, includeDeleted, cancellationToken));

    [HttpPut("{courseId:guid}")]
    public async Task<ActionResult<CourseDto>> Update(
        Guid courseId,
        [FromBody] UpdateCourseRequest request,
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
        => Ok(await service.UpdateAsync(courseId, request, cancellationToken));

    [HttpDelete("{courseId:guid}")]
    public async Task<IActionResult> Delete(
        Guid courseId,
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
    {
        await service.DeleteAsync(courseId, cancellationToken);
        return NoContent();
    }

    [HttpPost("{courseId:guid}/restore")]
    public async Task<ActionResult<CourseDto>> Restore(
        Guid courseId,
        [FromServices] ICourseService service,
        CancellationToken cancellationToken)
        => Ok(await service.RestoreAsync(courseId, cancellationToken));
}
