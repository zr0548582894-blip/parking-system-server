using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ParkingSystem.Core.Interfaces.Security;
using ParkingSystem.Core.Options;
using ParkingSystem.Data.Context;
using ParkingSystem.Data.Seed;
using ParkingSystem.Service.Security;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// ה-DbContext נרשם כ-Scoped (ברירת המחדל): instance אחד לכל בקשה.
// ה-connection string נקרא מהקונפיגורציה ולא כתוב בקוד
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

// משתמשי הדמו וסיסמאותיהם מגיעים מהקונפיגורציה (User Secrets בפיתוח), לא מהקוד
builder.Services.Configure<SeedUsersOptions>(builder.Configuration.GetSection(SeedUsersOptions.SectionName));

// Singleton: ה-hasher חסר מצב, ולכן בטוח לשתף אותו בין בקשות (הוא לא מחזיק DbContext)
builder.Services.AddSingleton<IPasswordHasher, BcryptPasswordHasher>();

builder.Services.AddControllers();
// Learn more about configuring Swagger/OpenAPI at https://aka.ms/aspnetcore/swashbuckle
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

var app = builder.Build();

// בעליית השרת: מחילים migrations שעוד לא הוחלו ומזינים נתוני התחלה,
// כדי שהמערכת תרוץ בלי שום פעולה ידנית בבסיס הנתונים
using (var scope = app.Services.CreateScope())
{
    var context = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await context.Database.MigrateAsync(app.Lifetime.ApplicationStopping);
    await DbSeeder.SeedAsync(
        context,
        scope.ServiceProvider.GetRequiredService<IPasswordHasher>(),
        scope.ServiceProvider.GetRequiredService<IOptions<SeedUsersOptions>>().Value,
        app.Logger,
        app.Lifetime.ApplicationStopping);
}

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();

app.UseAuthorization();

app.MapControllers();

app.Run();
