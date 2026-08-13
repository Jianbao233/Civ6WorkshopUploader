using System.Globalization;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace Civ6WorkshopUploader;

/// <summary>
/// Fetches Steam Workshop comments for an item, optionally restricted to a date range.
/// Pure HTTP against steamcommunity.com's comment AJAX endpoint — no SteamAPI.Init,
/// so the Steam client does not need to be running or logged in for public items.
///
/// The PublishedFile_Public render endpoint can be blocked for anonymous/datacenter
/// egress ("This profile is private."). In that case pass --cookie with a logged-in
/// steamcommunity session cookie (browser DevTools → Network → steamcommunity.com
/// request → Cookie header), or run through a residential network.
/// </summary>
public static class CommentsCommand
{
    private const string RenderUrlTemplate = "https://steamcommunity.com/comment/PublishedFile_Public/render/{0}/";
    private const int PageSize = 100;
    private const string UserAgent =
        "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0 Safari/537.36";

    public static async Task<int> GetComments(DirectoryInfo? workspaceDirectory, ulong? itemIdArg, string? since,
        string? until, string? outFile, string? cookie, string? proxy)
    {
        ulong itemId;
        if (itemIdArg != null)
        {
            itemId = itemIdArg.Value;
        }
        else if (workspaceDirectory != null)
        {
            FileInfo modIdFile = new(Path.Combine(workspaceDirectory.FullName, "mod_id.txt"));
            if (!modIdFile.Exists)
            {
                Log.Error($"No mod_id.txt in {workspaceDirectory.FullName}! Pass -i <id> instead.");
                return 1;
            }

            string modIdStr = (await File.ReadAllTextAsync(modIdFile.FullName)).Trim();
            if (!ulong.TryParse(modIdStr, out itemId))
            {
                Log.Error($"Could not parse item ID from {modIdFile.FullName}: '{modIdStr}'");
                return 1;
            }
        }
        else
        {
            Log.Error("Either -i <id> or -w <workspace> must be provided.");
            return 1;
        }

        DateTimeOffset? sinceUtc = ParseDate(since, "since");
        DateTimeOffset? untilUtc = ParseDate(until, "until");
        if (since != null && sinceUtc == null || until != null && untilUtc == null)
        {
            return 1;
        }

        using HttpClient client = BuildClient(proxy);

        List<WorkshopComment> comments = [];
        int totalCount = 0;

        for (int start = 0; ; start += PageSize)
        {
            string url = string.Format(RenderUrlTemplate, itemId) +
                         $"?start={start}&count={PageSize}&totalcount=0";

            HttpRequestMessage request = new(HttpMethod.Get, url);
            request.Headers.TryAddWithoutValidation("User-Agent", UserAgent);
            request.Headers.TryAddWithoutValidation("Referer", $"https://steamcommunity.com/sharedfiles/filedetails/?id={itemId}");
            if (!string.IsNullOrWhiteSpace(cookie))
            {
                request.Headers.TryAddWithoutValidation("Cookie", cookie);
            }

            string raw;
            try
            {
                using HttpResponseMessage response = await client.SendAsync(request);
                raw = await response.Content.ReadAsStringAsync();
            }
            catch (HttpRequestException e)
            {
                Log.Error($"Network error while fetching comments: {e.Message}");
                return 1;
            }

            using JsonDocument doc = JsonDocument.Parse(raw);
            JsonElement root = doc.RootElement;

            if (root.TryGetProperty("error", out JsonElement errorEl))
            {
                Log.Error($"Steam rejected the request: {errorEl.GetString()}");
                Log.Error("The PublishedFile comment endpoint may require a logged-in session. " +
                          "Retry with --cookie \"<steamcommunity cookie header>\" (browser DevTools → Network → steamcommunity.com → Cookie) " +
                          "and/or --proxy <url> if you are behind a firewall/GFW.");
                return 2;
            }

            if (!IsSuccess(root))
            {
                Log.Error($"Steam returned success=false for the comments request.");
                return 2;
            }

            if (root.TryGetProperty("total_count", out JsonElement totalEl) && totalEl.ValueKind == JsonValueKind.Number)
            {
                totalCount = totalEl.GetInt32();
            }

            int pageComments = 0;

            // Structured form (older endpoint): "comments": [ { commentid, author, author_steamid, timestamp, body } ]
            if (root.TryGetProperty("comments", out JsonElement commentsEl) && commentsEl.ValueKind == JsonValueKind.Array)
            {
                foreach (JsonElement c in commentsEl.EnumerateArray())
                {
                    comments.Add(ParseStructuredComment(c));
                    pageComments++;
                }
            }

            // HTML form (current endpoint): "comments_html": "<div class=\"commentthread_comment\">..."
            if (root.TryGetProperty("comments_html", out JsonElement htmlEl) && htmlEl.ValueKind == JsonValueKind.String)
            {
                List<WorkshopComment> parsed = ParseHtmlComments(htmlEl.GetString() ?? "");
                comments.AddRange(parsed);
                pageComments += parsed.Count;
            }

            Log.Info($"Fetched page {start / PageSize + 1}: {pageComments} comment(s) (total available: {totalCount}).");

            if (pageComments == 0)
            {
                break;
            }

            // The endpoint returns comments newest-first; once a whole page is older than
            // --since we can stop early. Requires parseable unix timestamps.
            if (sinceUtc != null && comments.Count >= PageSize)
            {
                bool pageAllOlder = true;
                foreach (WorkshopComment c in comments.TakeLast(pageComments))
                {
                    if (c.timestamp == null || c.timestamp >= sinceUtc.Value.ToUnixTimeSeconds())
                    {
                        pageAllOlder = false;
                        break;
                    }
                }

                if (pageAllOlder)
                {
                    Log.Info("Reached comments older than --since; stopping pagination.");
                    break;
                }
            }

            if (start + PageSize >= totalCount && totalCount > 0)
            {
                break;
            }

            // Defensive cap in case total_count is missing or misreported.
            if (start >= 2000)
            {
                Log.Warn("Hit pagination safety cap (2000); stopping.");
                break;
            }
        }

        // Date filtering (unix seconds, UTC).
        List<WorkshopComment> filtered = comments;
        if (sinceUtc != null || untilUtc != null)
        {
            filtered = comments.Where(c =>
            {
                if (c.timestamp == null)
                {
                    return true; // no timestamp → keep, but noted below
                }

                long ts = c.timestamp.Value;
                return (sinceUtc == null || ts >= sinceUtc.Value.ToUnixTimeSeconds()) &&
                       (untilUtc == null || ts <= untilUtc.Value.ToUnixTimeSeconds());
            }).ToList();

            int withoutTs = comments.Count(c => c.timestamp == null);
            if (withoutTs > 0)
            {
                Log.Warn($"{withoutTs} comment(s) had no parseable timestamp and were kept unfiltered.");
            }
        }

        filtered = filtered.OrderByDescending(c => c.timestamp ?? long.MinValue).ToList();

        Log.Info($"Fetched {filtered.Count} comment(s) for item {itemId} "
                 + (sinceUtc != null ? $"since {sinceUtc:yyyy-MM-dd}" : "")
                 + (untilUtc != null ? $"until {untilUtc:yyyy-MM-dd}" : "")
                 + ".");

        if (!string.IsNullOrWhiteSpace(outFile))
        {
            CommentExport export = new()
            {
                item_id = itemId,
                since = sinceUtc?.ToString("yyyy-MM-dd"),
                until = untilUtc?.ToString("yyyy-MM-dd"),
                total_available = totalCount,
                fetched = filtered.Count,
                comments = filtered
            };

            await using FileStream fs = new(outFile, FileMode.Create);
            await JsonSerializer.SerializeAsync(fs, export, SourceGenerationContext.Default.CommentExport);
            Log.Info($"Wrote {filtered.Count} comment(s) to {outFile}");
        }
        else
        {
            foreach (WorkshopComment c in filtered)
            {
                string when = c.timestamp != null
                    ? DateTimeOffset.FromUnixTimeSeconds(c.timestamp.Value).ToLocalTime().ToString("yyyy-MM-dd HH:mm")
                    : c.timestamp_text ?? "?";
                Log.Info($"[{when}] {c.author}: {Truncate(c.body, 120)}");
            }
        }

        return 0;
    }

    private static bool IsSuccess(JsonElement root)
    {
        if (!root.TryGetProperty("success", out JsonElement el))
        {
            return false;
        }

        return el.ValueKind switch
        {
            JsonValueKind.True => true,
            JsonValueKind.False => false,
            JsonValueKind.Number => el.GetInt32() != 0,
            JsonValueKind.String => el.GetString() == "1" || string.Equals(el.GetString(), "true", StringComparison.OrdinalIgnoreCase),
            _ => false
        };
    }

    private static WorkshopComment ParseStructuredComment(JsonElement c)
    {
        WorkshopComment comment = new();
        if (c.TryGetProperty("commentid", out JsonElement idEl))
        {
            comment.comment_id = idEl.GetString();
        }

        if (c.TryGetProperty("author", out JsonElement authorEl))
        {
            comment.author = authorEl.GetString();
        }

        if (c.TryGetProperty("author_steamid", out JsonElement steamIdEl))
        {
            comment.author_steamid = steamIdEl.GetString();
        }

        if (c.TryGetProperty("timestamp", out JsonElement tsEl) && tsEl.ValueKind == JsonValueKind.Number)
        {
            comment.timestamp = tsEl.GetInt64();
        }

        if (c.TryGetProperty("body", out JsonElement bodyEl))
        {
            comment.body = bodyEl.GetString();
        }

        return comment;
    }

    private static readonly Regex CommentBlockRegex =
        new(@"<div class=""commentthread_comment\b[^""]*"">(.*?)(?=<div class=""commentthread_comment\b|$)",
            RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex AuthorLinkRegex =
        new(@"commentthread_author_link"" href=""https://steamcommunity\.com/(?:profiles|id)/([^""]+)""[^>]*>(?:<bdi>)?([^<]+)",
            RegexOptions.Compiled);

    private static readonly Regex AuthorLinkNoHrefRegex =
        new(@"commentthread_author_link[^>]*>(?:<bdi>)?([^<]+)", RegexOptions.Compiled);

    private static readonly Regex TimestampDataRegex =
        new(@"commentthread_comment_timestamp[^>]*data-timestamp=""(\d+)""", RegexOptions.Compiled);

    private static readonly Regex TimestampTextRegex =
        new(@"commentthread_comment_timestamp[^""]*""[^>]*>(.*?)</div>", RegexOptions.Singleline | RegexOptions.Compiled);

    private static readonly Regex BodyRegex =
        new(@"commentthread_comment_text[^""]*"">(.*?)(?=<div class=""commentthread_comment\b|$)",
            RegexOptions.Singleline | RegexOptions.Compiled);

    private static List<WorkshopComment> ParseHtmlComments(string html)
    {
        List<WorkshopComment> comments = [];

        foreach (Match block in CommentBlockRegex.Matches(html))
        {
            string chunk = block.Groups[1].Value;

            WorkshopComment comment = new();

            Match authorMatch = AuthorLinkRegex.Match(chunk);
            if (authorMatch.Success)
            {
                comment.author_steamid = authorMatch.Groups[1].Value;
                comment.author = WebUtility.HtmlDecode(authorMatch.Groups[2].Value.Trim());
            }
            else
            {
                Match authorFallback = AuthorLinkNoHrefRegex.Match(chunk);
                if (authorFallback.Success)
                {
                    comment.author = WebUtility.HtmlDecode(authorFallback.Groups[1].Value.Trim());
                }
            }

            Match tsData = TimestampDataRegex.Match(chunk);
            if (tsData.Success && long.TryParse(tsData.Groups[1].Value, out long unixTs))
            {
                comment.timestamp = unixTs;
            }

            Match tsText = TimestampTextRegex.Match(chunk);
            if (tsText.Success)
            {
                comment.timestamp_text = WebUtility.HtmlDecode(tsText.Groups[1].Value.Trim());
                if (comment.timestamp == null)
                {
                    comment.timestamp = ParseHumanTimestamp(comment.timestamp_text);
                }
            }

            Match bodyMatch = BodyRegex.Match(chunk);
            if (bodyMatch.Success)
            {
                comment.body = StripHtml(bodyMatch.Groups[1].Value);
            }

            comments.Add(comment);
        }

        return comments;
    }

    private static string StripHtml(string html)
    {
        string text = Regex.Replace(html, @"<br\s*/?>", "\n", RegexOptions.IgnoreCase);
        text = Regex.Replace(text, @"<[^>]+>", " ");
        text = WebUtility.HtmlDecode(text);
        return Regex.Replace(text, @"\s+", " ").Trim();
    }

    /// <summary>
    /// Best-effort parsing of the localized timestamp text shown on the page. Supports
    /// English ("22 Jul, 2023 @ 10:21pm") and Chinese ("2023年7月22日 下午10:21") forms.
    /// Returns null when unrecognized; the comment is then kept but not date-filtered.
    /// </summary>
    private static long? ParseHumanTimestamp(string? text)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            return null;
        }

        // "2023年7月22日 下午10:21" / "2023年7月22日 上午10:21"
        Match cn = Regex.Match(text, @"(\d{4})年(\d{1,2})月(\d{1,2})日(?:\s*(上午|下午|晚上|中午)?\s*(\d{1,2}):(\d{2}))?");
        if (cn.Success)
        {
            int hour = cn.Groups[4].Success ? int.Parse(cn.Groups[5].Value) : 0;
            int minute = cn.Groups[4].Success ? int.Parse(cn.Groups[6].Value) : 0;
            string period = cn.Groups[4].Value;
            if (period == "下午" || period == "晚上")
            {
                hour += 12;
            }

            if (hour >= 24)
            {
                hour -= 12;
            }

            try
            {
                DateTime dt = new(int.Parse(cn.Groups[1].Value), int.Parse(cn.Groups[2].Value),
                    int.Parse(cn.Groups[3].Value), hour, minute, 0, DateTimeKind.Utc);
                return new DateTimeOffset(dt).ToUnixTimeSeconds();
            }
            catch (ArgumentOutOfRangeException)
            {
                return null;
            }
        }

        // "22 Jul, 2023 @ 10:21pm" / "22 Jul 2023" (en-US)
        if (DateTimeOffset.TryParseExact(text, "d MMM, yyyy @ h:mmtt", CultureInfo.InvariantCulture,
                DateTimeStyles.AssumeUniversal, out DateTimeOffset en1))
        {
            return en1.ToUnixTimeSeconds();
        }

        if (DateTimeOffset.TryParse(text, CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out DateTimeOffset en2))
        {
            return en2.ToUnixTimeSeconds();
        }

        return null;
    }

    private static DateTimeOffset? ParseDate(string? value, string label)
    {
        if (value == null)
        {
            return null;
        }

        if (DateTimeOffset.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal,
                out DateTimeOffset dt))
        {
            return dt;
        }

        Log.Error($"Could not parse {label} date '{value}'. Expected format YYYY-MM-DD (e.g. 2026-08-01).");
        return null;
    }

    private static HttpClient BuildClient(string? proxy)
    {
        HttpClientHandler handler = new()
        {
            AllowAutoRedirect = true,
            AutomaticDecompression = DecompressionMethods.GZip | DecompressionMethods.Deflate
        };

        if (!string.IsNullOrWhiteSpace(proxy))
        {
            handler.Proxy = new WebProxy(proxy);
            handler.UseProxy = true;
        }
        else
        {
            string? envProxy = Environment.GetEnvironmentVariable("HTTPS_PROXY")
                               ?? Environment.GetEnvironmentVariable("HTTP_PROXY")
                               ?? Environment.GetEnvironmentVariable("ALL_PROXY");
            if (!string.IsNullOrWhiteSpace(envProxy))
            {
                handler.Proxy = new WebProxy(envProxy);
                handler.UseProxy = true;
            }
            else
            {
                handler.Proxy = WebRequest.GetSystemWebProxy();
                handler.UseProxy = true;
            }
        }

        return new HttpClient(handler);
    }

    private static string? Truncate(string? s, int max)
    {
        if (s == null)
        {
            return null;
        }

        return s.Length <= max ? s : s[..max] + "…";
    }
}

public class WorkshopComment
{
    public string? comment_id;
    public string? author;
    public string? author_steamid;
    public long? timestamp;
    public string? timestamp_text;
    public string? body;
}

public class CommentExport
{
    public ulong item_id;
    public string? since;
    public string? until;
    public int total_available;
    public int fetched;
    public List<WorkshopComment> comments = [];
}