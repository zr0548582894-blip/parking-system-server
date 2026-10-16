using System.Reflection;
using System.Security.Claims;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Moq;
using ParkingSystem.API.Controllers;
using ParkingSystem.Core.Common;
using ParkingSystem.Core.DTOs.Auth;
using ParkingSystem.Core.DTOs.Common;
using ParkingSystem.Core.DTOs.ParkingSpots;
using ParkingSystem.Core.DTOs.Reservations;
using ParkingSystem.Core.Enums;
using ParkingSystem.Core.Interfaces.Services;

namespace ParkingSystem.Tests.Api;

// בדיקות מפורשות ל-ModelState, לתרגום תוצאות לקודי HTTP, ולהרשאות לפי role. בלי שרת ובלי בסיס נתונים
public class ControllerTests
{
    private static ControllerContext Context(int? userId = null, UserRole? role = null)
    {
        var http = new DefaultHttpContext();
        if (userId is not null && role is not null)
        {
            http.User = new ClaimsPrincipal(new ClaimsIdentity(new[]
            {
                new Claim("sub", userId.Value.ToString()),
                new Claim("role", role.Value.ToString())
            }, "test"));
        }

        return new ControllerContext { HttpContext = http };
    }

    private static T WithContext<T>(T controller, ControllerContext context) where T : ControllerBase
    {
        controller.ControllerContext = context;
        return controller;
    }

    private static void AssertInvalidModelResponse(IActionResult result, string field)
    {
        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(StatusCodes.Status400BadRequest, objectResult.StatusCode);
        var details = Assert.IsType<ValidationProblemDetails>(objectResult.Value);
        Assert.Contains(field, details.Errors.Keys);
        Assert.True(details.Extensions.ContainsKey("correlationId"));
    }

    // ---------- ModelState: 400 אחיד, והשירות לא נקרא ----------

    [Fact]
    public async Task Register_InvalidModel_Returns400AndNeverCallsTheService()
    {
        var service = new Mock<IAuthService>();
        var controller = WithContext(new AuthController(service.Object), Context());
        controller.ModelState.AddModelError(nameof(RegisterRequest.Email), "invalid");

        var result = await controller.Register(new RegisterRequest(), CancellationToken.None);

        AssertInvalidModelResponse(result, nameof(RegisterRequest.Email));
        service.Verify(s => s.RegisterAsync(It.IsAny<RegisterRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task Login_InvalidModel_Returns400AndNeverCallsTheService()
    {
        var service = new Mock<IAuthService>();
        var controller = WithContext(new AuthController(service.Object), Context());
        controller.ModelState.AddModelError(nameof(LoginRequest.Password), "required");

        var result = await controller.Login(new LoginRequest(), CancellationToken.None);

        AssertInvalidModelResponse(result, nameof(LoginRequest.Password));
        service.Verify(s => s.LoginAsync(It.IsAny<LoginRequest>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpotsList_InvalidQuery_Returns400AndNeverCallsTheService()
    {
        var service = new Mock<IParkingSpotService>();
        var controller = WithContext(new ParkingSpotsController(service.Object), Context(1, UserRole.Employee));
        controller.ModelState.AddModelError(nameof(ParkingSpotQuery.Page), "out of range");

        var result = await controller.GetAll(new ParkingSpotQuery(), CancellationToken.None);

        AssertInvalidModelResponse(result, nameof(ParkingSpotQuery.Page));
        service.Verify(s => s.GetPagedAsync(It.IsAny<ParkingSpotQuery>(), It.IsAny<CancellationToken>()), Times.Never);
    }

    [Fact]
    public async Task SpotCreateAndUpdate_InvalidModel_Return400AndNeverCallTheService()
    {
        var service = new Mock<IParkingSpotService>();
        var controller = WithContext(new ParkingSpotsController(service.Object), Context(1, UserRole.Admin));
        controller.ModelState.AddModelError(nameof(CreateParkingSpotRequest.Code), "required");

        AssertInvalidModelResponse(await controller.Create(new CreateParkingSpotRequest(), CancellationToken.None), "Code");
        AssertInvalidModelResponse(await controller.Update(1, new UpdateParkingSpotRequest(), CancellationToken.None), "Code");

        service.VerifyNoOtherCalls();
    }

    [Fact]
    public async Task ReservationCreate_InvalidModel_Returns400AndNeverCallsTheService()
    {
        var service = new Mock<IReservationService>();
        var controller = WithContext(new ReservationsController(service.Object), Context(5, UserRole.Employee));
        controller.ModelState.AddModelError(nameof(CreateReservationRequest.Date), "required");

        var result = await controller.Create(new CreateReservationRequest(), CancellationToken.None);

        AssertInvalidModelResponse(result, nameof(CreateReservationRequest.Date));
        service.VerifyNoOtherCalls();
    }

    // ---------- תרגום תוצאות לקודי HTTP ----------

    [Theory]
    [InlineData(ErrorType.Validation, 400)]
    [InlineData(ErrorType.Unauthorized, 401)]
    [InlineData(ErrorType.Forbidden, 403)]
    [InlineData(ErrorType.NotFound, 404)]
    [InlineData(ErrorType.Conflict, 409)]
    public async Task ServiceFailures_AreTranslatedToTheMatchingHttpStatus(ErrorType error, int expectedStatus)
    {
        var service = new Mock<IReservationService>();
        service.Setup(s => s.CreateAsync(5, It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReservationResponse>.Fail(error, "message from the service"));
        var controller = WithContext(new ReservationsController(service.Object), Context(5, UserRole.Employee));

        var result = await controller.Create(
            new CreateReservationRequest { ParkingSpotId = 4, Date = new DateOnly(2030, 1, 1) }, CancellationToken.None);

        var objectResult = Assert.IsAssignableFrom<ObjectResult>(result);
        Assert.Equal(expectedStatus, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("message from the service", problem.Title);
        Assert.True(problem.Extensions.ContainsKey("correlationId"));
    }

    [Fact]
    public async Task ReservationCreate_Success_Returns201WithALocationToTheNewReservation()
    {
        var service = new Mock<IReservationService>();
        service.Setup(s => s.CreateAsync(5, It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReservationResponse>.Success(new ReservationResponse { Id = 77 }));
        var controller = WithContext(new ReservationsController(service.Object), Context(5, UserRole.Employee));

        var result = await controller.Create(
            new CreateReservationRequest { ParkingSpotId = 4, Date = new DateOnly(2030, 1, 1) }, CancellationToken.None);

        var created = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal(nameof(ReservationsController.GetById), created.ActionName);
        Assert.Equal(77, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task TheReservationOwnerIsTakenFromTheToken_NotFromTheRequest()
    {
        var service = new Mock<IReservationService>();
        service.Setup(s => s.CreateAsync(It.IsAny<int>(), It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()))
            .ReturnsAsync(Result<ReservationResponse>.Success(new ReservationResponse { Id = 1 }));
        var controller = WithContext(new ReservationsController(service.Object), Context(userId: 42, UserRole.Employee));

        await controller.Create(new CreateReservationRequest { ParkingSpotId = 4, Date = new DateOnly(2030, 1, 1) }, CancellationToken.None);

        service.Verify(s => s.CreateAsync(42, It.IsAny<CreateReservationRequest>(), It.IsAny<CancellationToken>()), Times.Once);
    }

    [Fact]
    public async Task Cancel_PassesTheUserAndRoleFromTheToken_AndReturns204OnSuccess()
    {
        var service = new Mock<IReservationService>();
        service.Setup(s => s.CancelAsync(10, 9, UserRole.Admin, It.IsAny<CancellationToken>())).ReturnsAsync(Result.Success());
        var controller = WithContext(new ReservationsController(service.Object), Context(9, UserRole.Admin));

        var result = await controller.Cancel(10, CancellationToken.None);

        Assert.IsType<NoContentResult>(result);
    }

    // ---------- הרשאות לפי role (מה שמגדיר מי רשאי לעשות מה) ----------

    private static string? RolesOn<TController>(string action)
    {
        var method = typeof(TController).GetMethod(action)!;
        return method.GetCustomAttribute<AuthorizeAttribute>()?.Roles;
    }

    [Theory]
    [InlineData(nameof(ParkingSpotsController.Create))]
    [InlineData(nameof(ParkingSpotsController.Update))]
    [InlineData(nameof(ParkingSpotsController.Delete))]
    public void SpotManagement_IsAdminOnly(string action) =>
        Assert.Equal("Admin", RolesOn<ParkingSpotsController>(action));

    [Theory]
    [InlineData(nameof(ReservationsController.Create), "Employee")]
    [InlineData(nameof(ReservationsController.GetMine), "Employee")]
    [InlineData(nameof(ReservationsController.GetAll), "Admin")]
    public void ReservationActions_HaveTheExpectedRole(string action, string role) =>
        Assert.Equal(role, RolesOn<ReservationsController>(action));

    [Fact]
    public void EveryControllerRequiresALogin_ExceptTheAuthController()
    {
        foreach (var type in new[] { typeof(ParkingSpotsController), typeof(ReservationsController), typeof(FeaturesController) })
        {
            Assert.NotNull(type.GetCustomAttribute<AuthorizeAttribute>());
        }

        Assert.NotNull(typeof(AuthController).GetCustomAttribute<AllowAnonymousAttribute>());
    }
}
