using Microsoft.OpenApi.Models;

namespace ParkingSystem.API.Swagger;

public static class SwaggerServiceCollectionExtensions
{
    // הגדרת Swagger במקום אחד: תיאור ה-API, כפתור Authorize עם JWT, ותיאורים מתגובות ה-XML שבקוד
    public static IServiceCollection AddSwaggerDocumentation(this IServiceCollection services)
    {
        services.AddEndpointsApiExplorer();

        services.AddSwaggerGen(options =>
        {
            options.SwaggerDoc("v1", new OpenApiInfo
            {
                Title = "ParkingSystem API",
                Version = "v1",
                Description = "הקצאת מקומות חניה: עובדים מזמינים מקום ליום מסוים. " +
                              "המשאב המוגבל הוא מקום חניה בתאריך נתון, ושתי הזמנות לאותו מקום וליום לא יתקבלו (409). " +
                              "כדי לבדוק פעולות מוגנות: התחברו ב-POST /api/tokens, לחצו Authorize והדביקו את ה-token."
            });

            // כפתור Authorize: מדביקים רק את ה-token, ו-Swagger מוסיף לבד את "Bearer "
            options.AddSecurityDefinition(AuthorizeOperationFilter.SchemeName, new OpenApiSecurityScheme
            {
                Type = SecuritySchemeType.Http,
                Scheme = "bearer",
                BearerFormat = "JWT",
                In = ParameterLocation.Header,
                Description = "ה-token שהתקבל מהתחברות או מהרשמה. אין צורך לכתוב Bearer לפניו."
            });

            // נעילה רק על פעולות שדורשות התחברות
            options.OperationFilter<AuthorizeOperationFilter>();

            // התיאורים מתגובות ה-/// של ה-Controllers ושל ה-DTOs (ב-API וב-Core)
            foreach (var xmlFile in Directory.GetFiles(AppContext.BaseDirectory, "ParkingSystem.*.xml"))
            {
                options.IncludeXmlComments(xmlFile, includeControllerXmlComments: true);
            }
        });

        return services;
    }
}
