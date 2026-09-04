using LMS.Api.application.features.questions;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Authorization;

namespace LMS.Api.presentation.controllers;

[ApiController, Route("api")]
[Authorize]
public sealed class QuestionsController : ControllerBase
{
    [HttpPost("question-banks")]
    public async Task<ActionResult<QuestionBankDto>> CreateBank(CreateQuestionBankRequest request, IQuestionService service, CancellationToken ct) => Ok(await service.CreateQuestionBankAsync(request, ct));
    [HttpPut("question-banks/{id:guid}")]
    public async Task<ActionResult<QuestionBankDto>> UpdateBank(Guid id, UpdateQuestionBankRequest request, IQuestionService service, CancellationToken ct) => Ok(await service.UpdateQuestionBankAsync(id, request, ct));
    [HttpGet("question-banks")]
    public async Task<ActionResult<IReadOnlyList<QuestionBankDto>>> ListBanks(Guid? teacherId, IQuestionService service, CancellationToken ct) => Ok(await service.ListQuestionBanksAsync(teacherId, ct));
    [HttpPost("question-banks/{id:guid}/multiple-choice")]
    public async Task<ActionResult<QuestionDto>> CreateMultipleChoice(Guid id, CreateMultipleChoiceQuestionRequest request, IQuestionService service, CancellationToken ct) => Ok(await service.CreateMultipleChoiceAsync(id, request, ct));
    [HttpPost("question-banks/{id:guid}/fill-in-blank")]
    public async Task<ActionResult<QuestionDto>> CreateFillInBlank(Guid id, CreateFillInBlankQuestionRequest request, IQuestionService service, CancellationToken ct) => Ok(await service.CreateFillInBlankAsync(id, request, ct));
    [HttpGet("question-banks/{id:guid}/questions")]
    public async Task<ActionResult<IReadOnlyList<QuestionDto>>> ListQuestions(Guid id, IQuestionService service, CancellationToken ct) => Ok(await service.ListQuestionsAsync(id, ct));
}
