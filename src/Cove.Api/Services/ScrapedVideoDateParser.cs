using System.Globalization;

namespace Cove.Api.Services;

internal static class ScrapedVideoDateParser
{
    private static readonly string[] ExactDateFormats =
    [
        "yyyyMMdd",
        "yyyy-MM-dd",
        "yyyy/MM/dd",
        "yyyy.MM.dd",
        "MM/dd/yyyy",
        "M/d/yyyy",
        "dd/MM/yyyy",
        "d/M/yyyy",
    ];

    private static readonly string[] ExactDateTimeFormats =
    [
        "yyyyMMddTHHmmss",
        "yyyy-MM-ddTHH:mm:ss",
        "yyyy-MM-ddTHH:mm:ssK",
        "yyyy-MM-ddTHH:mm:ss.fffK",
        "yyyy-MM-dd HH:mm:ss",
        "yyyy/MM/dd HH:mm:ss",
    ];

    public static bool TryParse(string? value, out DateOnly parsedDate)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            parsedDate = default;
            return false;
        }

        var normalized = value.Trim();

        if (DateOnly.TryParseExact(normalized, ExactDateFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate))
            return true;

        if (DateTime.TryParseExact(normalized, ExactDateTimeFormats, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces | DateTimeStyles.AssumeLocal, out var parsedDateTime))
        {
            parsedDate = DateOnly.FromDateTime(parsedDateTime);
            return true;
        }

        if (DateOnly.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate))
            return true;

        if (DateOnly.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDate))
            return true;

        if (DateTime.TryParse(normalized, CultureInfo.InvariantCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDateTime)
            || DateTime.TryParse(normalized, CultureInfo.CurrentCulture, DateTimeStyles.AllowWhiteSpaces, out parsedDateTime))
        {
            parsedDate = DateOnly.FromDateTime(parsedDateTime);
            return true;
        }

        parsedDate = default;
        return false;
    }
}
