using System.Text;
using AngleSharp.Dom;
using AngleSharp.Html.Dom;
using AngleSharp.Html.Parser;
using WebHealth.Application.Seo;
using WebHealth.Domain.Seo;

namespace WebHealth.Infrastructure.Seo;

internal sealed class SeoValueExtractor : ISeoValueExtractor
{
    private static readonly HtmlParser Parser = new();

    static SeoValueExtractor()
    {
        Encoding.RegisterProvider(CodePagesEncodingProvider.Instance);
    }

    public SeoExtraction Extract(SeoExtractionInput input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var reason = SeoExtractionRules.NotApplicableReason(new(
            input.TransportSucceeded, input.StatusCode, input.ContentType, input.Body.Length));
        if (reason is not null)
        {
            return SeoExtraction.NotApplicable(reason, input.BodyTruncated);
        }

        try
        {
            return ExtractFromDocument(input);
        }
        catch (Exception exception) when (exception is not OutOfMemoryException)
        {
            return SeoExtraction.NotApplicable(
                SeoNotApplicableReasons.ExtractionFailed, input.BodyTruncated);
        }
    }

    private static SeoExtraction ExtractFromDocument(SeoExtractionInput input)
    {
        using var document = Parse(input);

        var head = document.Head;
        var titles = Query(head, "title");
        var descriptions = MetaContents(head, "description");
        var canonicals = Query(head, "link[rel]")
            .Where(element => RelContains(element, "canonical"))
            .Select(element => element.GetAttribute("href"))
            .ToArray();
        var robots = MetaContents(head, "robots");

        var authoredCanonical = canonicals.FirstOrDefault(HasText);

        return new(
            SeoApplicabilities.Applicable,
            null,
            input.BodyTruncated,
            SeoExtractionRules.BoundedText(
                titles.Select(element => element.TextContent).FirstOrDefault(HasText),
                SeoValueLimits.Title),
            titles.Count,
            SeoExtractionRules.BoundedText(descriptions.FirstOrDefault(HasText), SeoValueLimits.MetaDescription),
            descriptions.Length,
            SeoExtractionRules.BoundedUrl(authoredCanonical, SeoValueLimits.CanonicalHref),
            canonicals.Length,
            BoundedAbsoluteUrl(SeoExtractionRules.ResolveAbsolute(authoredCanonical, input.FinalUrl)),
            SeoExtractionRules.BoundedText(
                string.Join(", ", robots.Where(HasText)).ToLowerInvariant(), SeoValueLimits.RobotsMeta),
            robots.Length);
    }

    private static string? BoundedAbsoluteUrl(string? absoluteUrl) =>
        absoluteUrl is not null && absoluteUrl.Length <= SeoValueLimits.CanonicalHref ? absoluteUrl : null;

    private static IHtmlDocument Parse(SeoExtractionInput input)
    {
        if (TryGetEncoding(SeoExtractionRules.CharSet(input.ContentType), out var encoding))
        {
            return Parser.ParseDocument(encoding.GetString(input.Body.Span));
        }

        using var stream = new MemoryStream(input.Body.ToArray(), writable: false);
        return Parser.ParseDocument(stream);
    }

    private static bool TryGetEncoding(string? charSet, out Encoding encoding)
    {
        encoding = Encoding.UTF8;
        if (string.IsNullOrWhiteSpace(charSet)) return false;
        try
        {
            encoding = Encoding.GetEncoding(charSet);
            return true;
        }
        catch (ArgumentException)
        {
            return false;
        }
    }

    private static IReadOnlyList<IElement> Query(IElement? head, string selector) =>
        head is null ? [] : head.QuerySelectorAll(selector);

    private static string?[] MetaContents(IElement? head, string name) =>
        Query(head, "meta[name]")
            .Where(element => string.Equals(
                element.GetAttribute("name")?.Trim(), name, StringComparison.OrdinalIgnoreCase))
            .Select(element => element.GetAttribute("content"))
            .ToArray();

    private static bool RelContains(IElement element, string token) =>
        element.GetAttribute("rel")?
            .Split((char[]?)null, StringSplitOptions.RemoveEmptyEntries)
            .Any(candidate => string.Equals(candidate, token, StringComparison.OrdinalIgnoreCase)) == true;

    private static bool HasText(string? value) => !string.IsNullOrWhiteSpace(value);
}
