namespace LMS.Api.domain.Enums.Users;

public static class RoleExtensions
{
    public static bool IsAdminOrSystem(this Role role) => role is Role.Admin or Role.System;
}