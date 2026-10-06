using LMS.Api.domain.exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using LMS.Api.infrastructure.Persistence;

namespace LMS.Api.presentation.middleware;

public class GlobalExceptionHandler: IExceptionHandler
{
    private readonly  ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var statusCode = exception switch
        {
            BadRequestException => StatusCodes.Status400BadRequest,
            UnauthorizeException => StatusCodes.Status401Unauthorized,
            ForbiddenException => StatusCodes.Status403Forbidden,
            NotFoundException => StatusCodes.Status404NotFound,
            ConflictException => StatusCodes.Status409Conflict,
            DbUpdateException dbUpdateException when DatabaseConstraintPolicy.IsBusinessUniqueViolation(dbUpdateException) => StatusCodes.Status409Conflict,
            
            _ => StatusCodes.Status500InternalServerError
        };

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            _logger.LogError(
                "Unhandled request failure of type {ExceptionType}.",
                exception.GetType().Name);
        }
        else
        {
            _logger.LogWarning(
                "Request rejected with status {StatusCode} and exception type {ExceptionType}.",
                statusCode,
                exception.GetType().Name);
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = exception switch
            {
                BadRequestException => "Bad Request",
                UnauthorizeException =>  "Unauthorize",
                ForbiddenException =>  "Forbidden",
                NotFoundException =>  "Not found",
                ConflictException => "Conflict",
                DbUpdateException dbUpdateException when DatabaseConstraintPolicy.IsBusinessUniqueViolation(dbUpdateException) => "Conflict",
                _ => "Internal Server Error"
            },
            Detail = statusCode == StatusCodes.Status500InternalServerError
                ? "An unexpected error occurred."
                : exception is DbUpdateException
                    ? "The requested change conflicts with existing data."
                    : exception.Message
        };

        httpContext.Response.StatusCode = statusCode;
        
        await httpContext.Response.WriteAsJsonAsync(problemDetails, cancellationToken);
        return true;
    }
}
