using System.ComponentModel.DataAnnotations;
using System.Text;

namespace ParkingSystem.Core.Validation;

/// <summary>
/// מגביל את גודל המחרוזת בבייטים של UTF-8 ולא באורך המחרוזת.
/// נחוץ לסיסמאות: BCrypt קורא רק 72 בייטים, ותו בעברית תופס 2 בייטים.
/// </summary>
[AttributeUsage(AttributeTargets.Property | AttributeTargets.Field | AttributeTargets.Parameter)]
public sealed class MaxUtf8BytesAttribute : ValidationAttribute
{
    private readonly int _maxBytes;

    public MaxUtf8BytesAttribute(int maxBytes)
    {
        _maxBytes = maxBytes;
        ErrorMessage = "{0} ארוכה מדי: מותרים עד {1} בייטים (תו בעברית תופס 2 בייטים).";
    }

    public override bool IsValid(object? value) =>
        value is not string text || Encoding.UTF8.GetByteCount(text) <= _maxBytes;

    public override string FormatErrorMessage(string name) =>
        string.Format(System.Globalization.CultureInfo.CurrentCulture, ErrorMessageString, name, _maxBytes);
}
