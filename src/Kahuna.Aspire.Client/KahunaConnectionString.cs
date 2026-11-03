/*
 * This file is part of Kahuna
 *
 * For the full copyright and license information, please view the LICENSE.txt
 * file that was distributed with this source code.
 */

using System.Data.Common;

namespace Kahuna.Aspire.Client;

/// <summary>Reads the node endpoints from a connection string. The accepted forms are on <see cref="KahunaClientSettings.ConnectionString"/>.</summary>
internal static class KahunaConnectionString
{
    private static readonly string[] EndpointKeys = ["Endpoints", "Endpoint"];

    public static bool TryParse(string? connectionString, out string[] endpoints, out string? error)
    {
        endpoints = [];

        if (string.IsNullOrWhiteSpace(connectionString))
        {
            error = "The connection string is empty.";
            return false;
        }

        string list = connectionString.Trim();

        if (!LooksLikeUrl(list))
        {
            DbConnectionStringBuilder builder = new() { ConnectionString = list };

            string? value = null;

            foreach (string key in EndpointKeys)
            {
                if (builder.TryGetValue(key, out object? found) && found is string text)
                {
                    value = text;
                    break;
                }
            }

            if (value is null)
            {
                error = "The connection string has no 'Endpoints' key.";
                return false;
            }

            list = value;
        }

        List<string> parsed = [];

        foreach (string part in list.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            if (!Uri.TryCreate(part, UriKind.Absolute, out Uri? uri) || (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
            {
                error = $"'{part}' is not an http or https URL.";
                return false;
            }

            parsed.Add(part);
        }

        if (parsed.Count == 0)
        {
            error = "The connection string has no endpoints.";
            return false;
        }

        endpoints = [.. parsed];
        error = null;
        return true;
    }

    private static bool LooksLikeUrl(string value) =>
        value.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
        value.StartsWith("https://", StringComparison.OrdinalIgnoreCase);
}
