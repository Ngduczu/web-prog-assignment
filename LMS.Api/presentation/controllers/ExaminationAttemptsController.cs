using LMS.Api.application.features.examinationattempts;
using LMS.Api.domain.Enums.Examinations;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController, Route("api/examination-attempts")]
[Authorize]
public sealed class ExaminationAttemptsController : ControllerBase
{
    [HttpPost("{examinationId:guid}/start")]
    public async Task<ActionResult<ExaminationAttemptDto>> Start(Guid examinationId, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.StartAsync(examinationId, ct));
    [HttpPut("{attemptId:guid}/questions/{questionId:guid}/multiple-choice")]
    public async Task<ActionResult<StudentAnswerDto>> SaveMultipleChoice(Guid attemptId, Guid questionId, Guid selectedOptionId, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.SaveMultipleChoiceAnswerAsync(attemptId, questionId, selectedOptionId, ct));
    [HttpPut("{attemptId:guid}/questions/{questionId:guid}/fill-in-blank")]
    public async Task<ActionResult<StudentAnswerDto>> SaveFillInBlank(Guid attemptId, Guid questionId, IReadOnlyList<StudentFillInBlankAnswerInput> answers, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.SaveFillInBlankAnswerAsync(attemptId, questionId, answers, ct));
    [HttpPost("{attemptId:guid}/disconnect")]
    public async Task<ActionResult<ExaminationAttemptDto>> Disconnect(Guid attemptId, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.DisconnectAsync(attemptId, ct));
    [HttpPost("{attemptId:guid}/reconnect")]
    public async Task<ActionResult<ExaminationAttemptDto>> Reconnect(Guid attemptId, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.ReconnectAsync(attemptId, ct));
    [HttpPost("{attemptId:guid}/violations")]
    public async Task<ActionResult<ExaminationAttemptDto>> Violation(Guid attemptId, ExaminationViolationType violationType, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.RecordViolationAsync(attemptId, violationType, ct));
    [HttpPost("{attemptId:guid}/submit")]
    public async Task<ActionResult<ExaminationAttemptDto>> Submit(Guid attemptId, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.SubmitAsync(attemptId, ct));
    [HttpPost("{attemptId:guid}/auto-submit")]
    [Authorize(Roles = "Admin")]
    public async Task<ActionResult<ExaminationAttemptDto>> AutoSubmit(Guid attemptId, ExaminationSubmitReason reason, IExaminationAttemptService service, CancellationToken ct) => Ok(await service.AutoSubmitAsync(attemptId, reason, ct));
}
