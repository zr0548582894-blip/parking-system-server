using Microsoft.EntityFrameworkCore;
using ParkingSystem.Core.Entities;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Exceptions;
using ParkingSystem.Data.Repositories;

namespace ParkingSystem.Tests.Integration;

// מוכיחות שכל אילוץ ייחודי מזוהה לפי שמו המדויק, ושאילוץ אחד לא מתורגם בטעות כאילו היה אחר
public class UniqueConstraintTranslationTests : IClassFixture<PostgresDatabaseFixture>
{
    private const int ParallelSaves = 8;

    private readonly PostgresDatabaseFixture _db;

    public UniqueConstraintTranslationTests(PostgresDatabaseFixture db)
    {
        _db = db;
    }

    [PostgresFact]
    public async Task ParallelRegistrationsWithTheSameEmail_ExactlyOneSucceeds_TheRestThrowDuplicateEmail()
    {
        var email = $"race-{Guid.NewGuid():N}@test.local";

        var outcomes = await RunConcurrentlyAsync(async () =>
        {
            await using var context = _db.CreateContext();
            var repository = new UserRepository(context);
            await repository.AddAsync(
                new User { Email = email, FullName = "Race", PasswordHash = "hash", Role = UserRole.Employee },
                CancellationToken.None);

            await repository.SaveChangesAsync(CancellationToken.None);
        });

        Assert.Equal(1, outcomes.Count(e => e is null));
        Assert.Equal(ParallelSaves - 1, outcomes.Count(e => e is DuplicateEmailException));
    }

    [PostgresFact]
    public async Task ParallelCreationsWithTheSameSpotCode_ExactlyOneSucceeds_TheRestThrowDuplicateSpotCode()
    {
        var code = $"C{Guid.NewGuid():N}"[..12];

        var outcomes = await RunConcurrentlyAsync(async () =>
        {
            await using var context = _db.CreateContext();
            var repository = new ParkingSpotRepository(context);
            await repository.AddAsync(new ParkingSpot { Code = code, Floor = 1 }, CancellationToken.None);

            await repository.SaveChangesAsync(CancellationToken.None);
        });

        Assert.Equal(1, outcomes.Count(e => e is null));
        Assert.Equal(ParallelSaves - 1, outcomes.Count(e => e is DuplicateSpotCodeException));
    }

    [PostgresFact]
    public async Task AnUnrelatedUniqueViolation_IsNotTranslatedIntoAnyOfTheDomainExceptions()
    {
        var name = $"F{Guid.NewGuid():N}"[..12];

        await using (var seed = _db.CreateContext())
        {
            seed.Features.Add(new Feature { Name = name });
            await seed.SaveChangesAsync();
        }

        // שם מאפיין כפול מפר אילוץ ייחודי אחר: לא אימייל, לא קוד מקום ולא הזמנה
        await using var context = _db.CreateContext();
        context.Features.Add(new Feature { Name = name });
        var exception = await Record.ExceptionAsync(() => new UserRepository(context).SaveChangesAsync(CancellationToken.None));

        Assert.IsType<DbUpdateException>(exception);
    }

    // מריץ את הפעולה כמה פעמים באותו רגע, ומחזיר לכל ריצה את החריגה שנזרקה (או null אם הצליחה)
    private static async Task<List<Exception?>> RunConcurrentlyAsync(Func<Task> action)
    {
        var startGate = new TaskCompletionSource();

        var tasks = Enumerable.Range(0, ParallelSaves).Select(_ => Task.Run<Exception?>(async () =>
        {
            await startGate.Task;
            return await Record.ExceptionAsync(action);
        })).ToList();

        startGate.SetResult();

        return (await Task.WhenAll(tasks)).ToList();
    }
}
