using LMS.Api.domain.exceptions;

namespace LMS.Api.application.features.auth.utils;

internal static class AuthFailurePolicy
{
    internal static bool IsCredentialRestorable(Exception exception) =>
        exception is not BadRequestException
            and not UnauthorizeException
            and not ForbiddenException
            and not NotFoundException
            and not ConflictException;
}
