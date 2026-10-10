using System.Text;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Azure.Functions.Worker;

namespace Membership.Functions;

public sealed class MapRegistrationFunction
{
    [Function("MapRegistration")]
    public async Task<IActionResult> Run(
        [HttpTrigger(AuthorizationLevel.Function, "post", Route = "registrations/map")] HttpRequest request,
        CancellationToken cancellationToken)
    {
        using var reader = new StreamReader(request.Body, Encoding.UTF8, detectEncodingFromByteOrderMarks: true,
            bufferSize: 1024, leaveOpen: true);
        var result = RegistrationMapper.Map(await reader.ReadToEndAsync(cancellationToken));
        if (!result.IsValid)
            return new BadRequestObjectResult(new { error = "validation_failed", errors = result.Errors });

        return new ContentResult
        {
            StatusCode = StatusCodes.Status200OK,
            ContentType = "application/json; charset=utf-8",
            Content = RegistrationMapper.ToJson(result.Payload!)
        };
    }
}
