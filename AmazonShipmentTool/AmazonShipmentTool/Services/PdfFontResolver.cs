using PdfSharp.Fonts;

namespace AmazonShipmentTool.Services;

public sealed class PdfFontResolver : IFontResolver
{
    private const string RegularFace = "Arial#Regular";
    private const string BoldFace = "Arial#Bold";

    private static readonly Lazy<PdfFontResolver> Instance = new(() => new PdfFontResolver());

    public static void EnsureRegistered()
    {
        if (GlobalFontSettings.FontResolver == null)
            GlobalFontSettings.FontResolver = Instance.Value;
    }

    public FontResolverInfo ResolveTypeface(string familyName, bool isBold, bool isItalic)
    {
        return new FontResolverInfo(isBold ? BoldFace : RegularFace);
    }

    public byte[] GetFont(string faceName)
    {
        var path = faceName == BoldFace ? FindBoldFont() : FindRegularFont();
        return File.ReadAllBytes(path);
    }

    private static string FindRegularFont()
    {
        return FirstExisting(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arial.ttf"),
            @"C:\Windows\Fonts\arial.ttf",
            "/usr/share/fonts/truetype/msttcorefonts/Arial.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Regular.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans.ttf");
    }

    private static string FindBoldFont()
    {
        return FirstExisting(
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Fonts), "arialbd.ttf"),
            @"C:\Windows\Fonts\arialbd.ttf",
            "/usr/share/fonts/truetype/msttcorefonts/Arial_Bold.ttf",
            "/usr/share/fonts/truetype/liberation2/LiberationSans-Bold.ttf",
            "/usr/share/fonts/truetype/dejavu/DejaVuSans-Bold.ttf");
    }

    private static string FirstExisting(params string[] paths)
    {
        foreach (var path in paths)
        {
            if (!string.IsNullOrWhiteSpace(path) && File.Exists(path))
                return path;
        }

        throw new FileNotFoundException("No usable Arial-compatible font was found. Install Arial, Liberation Sans, or DejaVu Sans.");
    }
}
