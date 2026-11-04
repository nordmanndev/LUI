namespace Nordmann.LUI.Sample.Api;

/// <summary>The error responses of the API: RFC 9457 problem details, with the message of the library as the detail.</summary>
internal static class Problems
{
    internal static IResult BadRequest(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status400BadRequest, title: "Bad request");

    internal static IResult NotFound(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status404NotFound, title: "Not found");

    internal static IResult Conflict(string detail) => Results.Problem(detail, statusCode: StatusCodes.Status409Conflict, title: "Not possible for this index");
}
