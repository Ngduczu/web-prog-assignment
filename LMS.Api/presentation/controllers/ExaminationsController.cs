using LMS.Api.application.features.examinations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController, Route("api")]
[Authorize]
public sealed class ExaminationsController : ControllerBase
{
    [HttpPost("courses/{courseId:guid}/examinations")]
    public async Task<ActionResult<ExaminationDto>> Create(Guid courseId, CreateExaminationRequest request, IExaminationService service, CancellationToken ct) => StatusCode(StatusCodes.Status201Created, await service.CreateAsync(courseId, request, ct));
    [HttpGet("courses/{courseId:guid}/examinations")]
    public async Task<ActionResult<IReadOnlyList<ExaminationDto>>> List(Guid courseId, IExaminationService service, CancellationToken ct) => Ok(await service.ListByCourseAsync(courseId, ct));
    [HttpGet("examinations/{id:guid}")]
    public async Task<ActionResult<ExaminationDto>> Get(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.GetForManagementAsync(id, ct));
    [HttpPut("examinations/{id:guid}")]
    public async Task<ActionResult<ExaminationDto>> Update(Guid id, UpdateExaminationRequest request, IExaminationService service, CancellationToken ct) => Ok(await service.UpdateAsync(id, request, ct));
    [HttpPost("examinations/{id:guid}/publish")]
    public async Task<ActionResult<ExaminationDto>> Publish(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.PublishAsync(id, ct));
    [HttpPost("examinations/{id:guid}/close")]
    public async Task<ActionResult<ExaminationDto>> Close(Guid id, IExaminationService service, CancellationToken ct) => Ok(await service.CloseAsync(id, ct));
}
