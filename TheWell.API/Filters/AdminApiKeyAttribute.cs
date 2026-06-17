using System.Security.Cryptography;
using System.Text;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;

namespace TheWell.API.Filters;

[AttributeUsage(AttributeTargets.Method | AttributeTargets.Class)]
public class AdminApiKeyAttribute : Attribute, IAuthorizationFilter
{
    public void OnAuthorization(AuthorizationFilterContext context)
    {
        var config = context.HttpContext.RequestServices.GetRequiredService<IConfiguration>();
        var expected = config["Admin:ApiKey"];

        if (string.IsNullOrEmpty(expected))
        {
            context.Result = new ObjectResult(new { error = "Admin API key not configured." })
            {
                StatusCode = StatusCodes.Status500InternalServerError
            };
            return;
        }

        if (!context.HttpContext.Request.Headers.TryGetValue("X-Admin-Key", out var provided)
            || !CryptographicOperations.FixedTimeEquals(
                Encoding.UTF8.GetBytes(provided.ToString()),
                Encoding.UTF8.GetBytes(expected)))
        {
            context.Result = new UnauthorizedObjectResult(new { error = "Missing or invalid admin key." });
        }
    }
}
