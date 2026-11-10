namespace Helpdesk.Core.Users;

public enum UserRole
{
    Customer = 0,
    Operator = 1,
    Admin = 2,
}

public static class UserRoleExtensions
{
    /// <summary>Operators and admins work with tickets of any customer.</summary>
    public static bool IsStaff(this UserRole role) => role is UserRole.Operator or UserRole.Admin;
}
