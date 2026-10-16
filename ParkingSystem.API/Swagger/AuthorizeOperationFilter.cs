using Microsoft.AspNetCore.Authorization;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ParkingSystem.API.Swagger;

// מסמן ב-Swagger (אייקון נעילה ושליחת ה-token) רק את הפעולות שדורשות התחברות.
// פעולות פתוחות, כמו הרשמה והתחברות, נשארות בלי נעילה
public class AuthorizeOperationFilter : IOperationFilter
{
    public const string SchemeName = "Bearer";

    public void Apply(OpenApiOperation operation, OperationFilterContext context)
    {
        var metadata = context.ApiDescription.ActionDescriptor.EndpointMetadata;

        var requiresLogin = metadata.OfType<AuthorizeAttribute>().Any()
                            && !metadata.OfType<AllowAnonymousAttribute>().Any();

        if (!requiresLogin)
        {
            return;
        }

        operation.Security.Add(new OpenApiSecurityRequirement
        {
            [new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference { Type = ReferenceType.SecurityScheme, Id = SchemeName }
            }] = Array.Empty<string>()
        });
    }
}
