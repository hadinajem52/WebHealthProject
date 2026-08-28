using WebHealth.Domain.Crawling;

namespace WebHealth.Infrastructure.PngAudits;

internal static class PngPagePathPolicy
{
    private static readonly HashSet<string> NonHtmlExtensions =
        new(StringComparer.OrdinalIgnoreCase)
        {
            "pdf", "doc", "docx", "xls", "xlsx", "ppt", "pptx",
            "odt", "ods", "odp", "rtf", "csv",
            "zip", "rar", "7z", "gz", "tar", "bz2", "xz",
            "mp3", "mp4", "m4a", "m4v", "avi", "mov", "wmv", "wav",
            "webm", "ogg", "ogv", "flv", "mkv",
            "jpg", "jpeg", "png", "gif", "webp", "svg", "bmp",
            "ico", "tif", "tiff", "avif",
            "woff", "woff2", "ttf", "otf", "eot",
            "exe", "dmg", "msi", "apk", "iso", "bin", "pkg", "deb", "rpm"
        };

    public static bool IsNonHtmlDocument(CrawlUrl url)
    {
        ArgumentNullException.ThrowIfNull(url);
        return TryReadExtension(url.Path, out var extension)
            && NonHtmlExtensions.Contains(extension);
    }

    private static bool TryReadExtension(string path, out string extension)
    {
        extension = string.Empty;
        var lastSegmentStart = path.LastIndexOf('/') + 1;
        var lastDot = path.LastIndexOf('.');
        if (lastDot <= lastSegmentStart || lastDot == path.Length - 1)
        {
            return false;
        }

        extension = path[(lastDot + 1)..];
        return true;
    }
}
