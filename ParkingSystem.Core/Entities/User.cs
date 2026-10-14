using ParkingSystem.Core.Enums;

namespace ParkingSystem.Core.Entities;

public class User
{
    public int Id { get; set; }
    public string Email { get; set; } = string.Empty;
    public string FullName { get; set; } = string.Empty;
    public string PasswordHash { get; set; } = string.Empty;
    public UserRole Role { get; set; } = UserRole.Employee;

    public ICollection<Reservation> Reservations { get; set; } = new List<Reservation>();
}
