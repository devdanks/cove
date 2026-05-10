using System.Globalization;
using Microsoft.AspNetCore.Http;

namespace Cove.Api.Controllers;

internal static class EntityImageUrls
{
    private const string AccessCookieName = "cove_access_token";
    public const int DefaultEntityImageMaxDimension = 640;
    public const int DefaultGalleryCoverMaxDimension = 640;

    public static string Scene(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/scenes/{id}/image", updatedAt, maxDimension);

    public static string Scene(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/scenes/{id}/image", updatedAt, maxDimension);

    public static string SceneScreenshot(HttpContext? context, int id, DateTime updatedAt, double? seconds = null)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("v", updatedAt.ToString("o", CultureInfo.InvariantCulture)),
        };
        if (seconds.HasValue)
        {
            query.Add(new("seconds", seconds.Value.ToString(CultureInfo.InvariantCulture)));
        }

        AppendAuthQuery(context, query);
        return $"/api/stream/scene/{id}/screenshot" + QueryString.Create(query);
    }

    public static string Performer(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/performers/{id}/image", updatedAt, maxDimension);

    public static string Performer(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/performers/{id}/image", updatedAt, maxDimension);

    public static string Face(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/faces/{id}/image", updatedAt, maxDimension);

    public static string Face(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/faces/{id}/image", updatedAt, maxDimension);

    public static string Studio(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/studios/{id}/image", updatedAt, maxDimension);

    public static string Studio(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/studios/{id}/image", updatedAt, maxDimension);

    public static string Tag(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/tags/{id}/image", updatedAt, maxDimension);

    public static string Tag(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/tags/{id}/image", updatedAt, maxDimension);

    public static string GroupFront(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/groups/{id}/image/front", updatedAt, maxDimension);

    public static string GroupFront(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/groups/{id}/image/front", updatedAt, maxDimension);

    public static string GroupBack(int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(null, $"/api/groups/{id}/image/back", updatedAt, maxDimension);

    public static string GroupBack(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultEntityImageMaxDimension)
        => Build(context, $"/api/groups/{id}/image/back", updatedAt, maxDimension);

    public static string GalleryCover(int id, DateTime updatedAt, int maxDimension = DefaultGalleryCoverMaxDimension)
        => Build(null, $"/api/galleries/{id}/cover", updatedAt, maxDimension);

    public static string GalleryCover(HttpContext? context, int id, DateTime updatedAt, int maxDimension = DefaultGalleryCoverMaxDimension)
        => Build(context, $"/api/galleries/{id}/cover", updatedAt, maxDimension);

    private static string Build(HttpContext? context, string path, DateTime updatedAt, int maxDimension)
    {
        var query = new List<KeyValuePair<string, string?>>
        {
            new("max", maxDimension.ToString(CultureInfo.InvariantCulture)),
            new("v", updatedAt.ToString("o", CultureInfo.InvariantCulture)),
        };

        AppendAuthQuery(context, query);
        return path + QueryString.Create(query);
    }

    private static void AppendAuthQuery(HttpContext? context, List<KeyValuePair<string, string?>> query)
    {
        if (context is null)
        {
            return;
        }

        var shareToken = context.Request.Headers["X-Share-Token"].ToString();
        if (string.IsNullOrWhiteSpace(shareToken))
        {
            shareToken = context.Request.Query["share_token"].ToString();
        }

        var sharePassword = context.Request.Headers["X-Share-Password"].ToString();
        if (string.IsNullOrWhiteSpace(sharePassword))
        {
            sharePassword = context.Request.Query["share_password"].ToString();
        }

        if (!string.IsNullOrWhiteSpace(shareToken))
        {
            query.Add(new("share_token", shareToken));
            if (!string.IsNullOrWhiteSpace(sharePassword))
            {
                query.Add(new("share_password", sharePassword));
            }
            return;
        }

        var accessToken = ResolveAccessToken(context);
        if (!string.IsNullOrWhiteSpace(accessToken))
        {
            query.Add(new("access_token", accessToken));
        }
    }

    private static string? ResolveAccessToken(HttpContext context)
    {
        var authHeader = context.Request.Headers.Authorization.ToString();
        if (!string.IsNullOrWhiteSpace(authHeader))
        {
            const string bearerPrefix = "Bearer ";
            if (authHeader.StartsWith(bearerPrefix, StringComparison.OrdinalIgnoreCase))
            {
                return authHeader[bearerPrefix.Length..].Trim();
            }
        }

        var queryToken = context.Request.Query["access_token"].ToString();
        if (!string.IsNullOrWhiteSpace(queryToken))
        {
            return queryToken;
        }

        if (context.Request.Cookies.TryGetValue(AccessCookieName, out var cookieToken)
            && !string.IsNullOrWhiteSpace(cookieToken))
        {
            return cookieToken;
        }

        return null;
    }
}