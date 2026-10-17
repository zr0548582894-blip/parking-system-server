using System.ComponentModel.DataAnnotations;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.DTOs.Reservations;

namespace ParkingSystem.Tests.Api;

// בדיקות מפורשות לוולידציה של ה-DTOs (Data Annotations): אילו קלטים נדחים ואילו מתקבלים
public class DtoValidationTests
{
    private static List<ValidationResult> Validate(object instance)
    {
        var results = new List<ValidationResult>();
        Validator.TryValidateObject(instance, new ValidationContext(instance), results, validateAllProperties: true);
        return results;
    }

    private static bool HasErrorOn(List<ValidationResult> results, string member) =>
        results.Any(r => r.MemberNames.Contains(member));

    // ---------- הרשמה והתחברות ----------

    private static RegisterRequest ValidRegister() =>
        new() { FullName = "Dana Levi", Email = "dana@example.com", Password = "Passw0rd!x" };

    [Fact]
    public void Register_ValidRequest_HasNoErrors() => Assert.Empty(Validate(ValidRegister()));

    [Theory]
    [InlineData("")]
    [InlineData("x")]                       // קצר מדי
    public void Register_BadFullName_IsRejected(string name)
    {
        var request = ValidRegister();
        request.FullName = name;

        Assert.True(HasErrorOn(Validate(request), nameof(RegisterRequest.FullName)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("not-an-email")]
    [InlineData("missing-at.example.com")]
    public void Register_BadEmail_IsRejected(string email)
    {
        var request = ValidRegister();
        request.Email = email;

        Assert.True(HasErrorOn(Validate(request), nameof(RegisterRequest.Email)));
    }

    [Fact]
    public void Register_PasswordLongerThanBcryptLimit_IsRejected()
    {
        var request = ValidRegister();
        request.Password = new string('a', 73);

        Assert.True(HasErrorOn(Validate(request), nameof(RegisterRequest.Password)));
    }

    [Theory]
    [InlineData("א", 37)]       // עברית: 2 בייטים לתו, 74 בייטים
    [InlineData("😀", 19)]      // אימוג'י: 4 בייטים לתו, 76 בייטים
    public void Register_PasswordOverTheByteLimit_IsRejectedEvenIfShortInCharacters(string unit, int count)
    {
        var request = ValidRegister();
        request.Password = string.Concat(Enumerable.Repeat(unit, count));

        Assert.True(request.Password.Length <= 72);
        Assert.True(HasErrorOn(Validate(request), nameof(RegisterRequest.Password)));
    }

    [Theory]
    [InlineData("א", 36)]       // בדיוק 72 בייטים
    [InlineData("😀", 18)]
    public void Register_PasswordOfExactly72Bytes_IsAccepted(string unit, int count)
    {
        var request = ValidRegister();
        request.Password = string.Concat(Enumerable.Repeat(unit, count));

        Assert.False(HasErrorOn(Validate(request), nameof(RegisterRequest.Password)));
    }

    [Fact]
    public void Register_PasswordOfExactlyMaxLength_IsAccepted()
    {
        var request = ValidRegister();
        request.Password = new string('a', 72);

        Assert.False(HasErrorOn(Validate(request), nameof(RegisterRequest.Password)));
    }

    [Theory]
    [InlineData("")]
    [InlineData("short")]                   // פחות מ-8 תווים
    public void Register_BadPassword_IsRejected(string password)
    {
        var request = ValidRegister();
        request.Password = password;

        Assert.True(HasErrorOn(Validate(request), nameof(RegisterRequest.Password)));
    }

    [Fact]
    public void Login_MissingFields_AreRejected()
    {
        var errors = Validate(new LoginRequest());

        Assert.True(HasErrorOn(errors, nameof(LoginRequest.Email)));
        Assert.True(HasErrorOn(errors, nameof(LoginRequest.Password)));
    }

    // ---------- מקום חניה ----------

    [Fact]
    public void CreateSpot_ValidRequest_HasNoErrors() =>
        Assert.Empty(Validate(new CreateParkingSpotRequest { Code = "G-01", Floor = 0 }));

    [Theory]
    [InlineData("")]
    [InlineData("THIS-CODE-IS-WAY-TOO-LONG")]   // יותר מ-20 תווים
    public void CreateSpot_BadCode_IsRejected(string code) =>
        Assert.True(HasErrorOn(Validate(new CreateParkingSpotRequest { Code = code }), nameof(CreateParkingSpotRequest.Code)));

    [Theory]
    [InlineData(-11)]
    [InlineData(101)]
    public void CreateSpot_FloorOutOfRange_IsRejected(int floor) =>
        Assert.True(HasErrorOn(Validate(new CreateParkingSpotRequest { Code = "G-01", Floor = floor }), nameof(CreateParkingSpotRequest.Floor)));

    [Fact]
    public void UpdateSpot_InheritsTheCreateRules() =>
        Assert.True(HasErrorOn(Validate(new UpdateParkingSpotRequest { Code = "" }), nameof(UpdateParkingSpotRequest.Code)));

    // ---------- הזמנה ----------

    [Fact]
    public void CreateReservation_ValidRequest_HasNoErrors() =>
        Assert.Empty(Validate(new CreateReservationRequest { ParkingSpotId = 4, Date = new DateOnly(2030, 1, 1) }));

    [Fact]
    public void CreateReservation_MissingDate_IsRejected() =>
        Assert.True(HasErrorOn(Validate(new CreateReservationRequest { ParkingSpotId = 4 }), nameof(CreateReservationRequest.Date)));

    [Fact]
    public void CreateReservation_SpotIdMustBePositive() =>
        Assert.True(HasErrorOn(
            Validate(new CreateReservationRequest { ParkingSpotId = 0, Date = new DateOnly(2030, 1, 1) }),
            nameof(CreateReservationRequest.ParkingSpotId)));

    // ---------- pagination וחיפוש ----------

    [Fact]
    public void Pagination_Defaults_AreValid() => Assert.Empty(Validate(new PaginationQuery()));

    [Theory]
    [InlineData(0, 10)]
    [InlineData(-1, 10)]
    [InlineData(1, 0)]
    [InlineData(1, 101)]
    [InlineData(1_000_001, 10)]             // מעל התקרה
    [InlineData(int.MaxValue, 100)]         // היה גולש בחישוב ההדלגה (500 בשרת)
    public void Pagination_OutOfRange_IsRejected(int page, int pageSize) =>
        Assert.NotEmpty(Validate(new PaginationQuery { Page = page, PageSize = pageSize }));

    [Fact]
    public void SpotQuery_InheritsPaginationRules_AndLimitsTheSearchLength()
    {
        Assert.NotEmpty(Validate(new ParkingSpotQuery { Page = 0 }));
        Assert.True(HasErrorOn(Validate(new ParkingSpotQuery { Search = new string('x', 21) }), nameof(ParkingSpotQuery.Search)));
    }
}
