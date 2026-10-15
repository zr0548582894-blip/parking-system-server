using ParkingSystem.Service.Security;

namespace ParkingSystem.Tests.Security;

public class BcryptPasswordHasherTests
{
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
