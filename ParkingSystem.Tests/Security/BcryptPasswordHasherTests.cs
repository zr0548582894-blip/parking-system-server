using ParkingSystem.Service.Security;

namespace ParkingSystem.Tests.Security;

public class BcryptPasswordHasherTests
{
    // מתעד עובדה שהוכחה בביקורת: BCrypt מתעלם ממה שאחרי 72 בייטים (לא תווים).
    // לכן הרשמה מגבילה סיסמה ל-72 בייטים (ראו DtoValidationTests)
    [Theory]
    [InlineData("a", 72)]
    [InlineData("א", 36)]
    [InlineData("😀", 18)]
    public void Hasher_IgnoresEverythingAfter72Bytes(string unit, int count)
    {
        var hasher = new BcryptPasswordHasher();
        var prefix = string.Concat(Enumerable.Repeat(unit, count));

        Assert.True(hasher.Verify(prefix + "Y", hasher.Hash(prefix + "X")));
    }

    private readonly BcryptPasswordHasher _hasher = new();

    [Fact]
    public void Hash_DoesNotReturnThePlainPassword()
    {
        var hash = _hasher.Hash("Secret123!");

        Assert.NotEqual("Secret123!", hash);
        Assert.DoesNotContain("Secret123!", hash);
        Assert.StartsWith("$2", hash); // הפורמט של BCrypt
    }

    [Fact]
    public void Hash_SamePasswordTwice_ProducesDifferentHashesBecauseOfRandomSalt()
    {
        Assert.NotEqual(_hasher.Hash("Secret123!"), _hasher.Hash("Secret123!"));
    }

    [Fact]
    public void Verify_CorrectPassword_ReturnsTrue()
    {
        var hash = _hasher.Hash("Secret123!");

        Assert.True(_hasher.Verify("Secret123!", hash));
    }

    [Fact]
    public void Verify_WrongPassword_ReturnsFalse()
    {
        var hash = _hasher.Hash("Secret123!");

        Assert.False(_hasher.Verify("secret123!", hash));
    }

    [Theory]
    [InlineData("not-a-bcrypt-hash")]
    [InlineData("")]
    public void Verify_MalformedHash_ReturnsFalseInsteadOfThrowing(string malformedHash)
    {
        Assert.False(_hasher.Verify("Secret123!", malformedHash));
    }
}
