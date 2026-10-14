using Microsoft.EntityFrameworkCore;
using ParkingSystem.Data.Context;
using ParkingSystem.Data.Seed;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.

// ה-DbContext נרשם כ-Scoped (ברירת המחדל): instance אחד לכל בקשה.
// ה-connection string נקרא מהקונפיגורציה ולא כתוב בקוד
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("Default")));

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
    await DbSeeder.SeedAsync(context, app.Lifetime.ApplicationStopping);
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
