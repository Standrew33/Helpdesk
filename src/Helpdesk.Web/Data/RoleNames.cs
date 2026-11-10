namespace Helpdesk.Web.Data;

public static class RoleNames
{
    public const string Customer = "Customer";
    public const string Operator = "Operator";
    public const string Admin = "Admin";

    public static readonly IReadOnlyList<string> All = [Customer, Operator, Admin];
}
