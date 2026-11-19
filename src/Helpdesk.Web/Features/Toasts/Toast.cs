using Microsoft.AspNetCore.Mvc.ViewFeatures;

namespace Helpdesk.Web.Features.Toasts;

/// <summary>
/// A short message on how an operation went, kept until the next page is drawn and shown there at the top right:
/// a success fades away on its own, a failure stays until it is closed.
/// </summary>
public static class Toast
{
    public const string SuccessKey = "Toast.Success";

    public const string FailureKey = "Toast.Failure";

    public static void Success(this ITempDataDictionary tempData, string text) => tempData[SuccessKey] = text;

    public static void Failure(this ITempDataDictionary tempData, string text) => tempData[FailureKey] = text;
}
