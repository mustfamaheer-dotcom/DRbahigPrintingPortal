using PdfSharpCore.Pdf;
using PdfSharpCore.Pdf.IO;
using PdfSharpCore.Drawing;
using System.Text.RegularExpressions;

namespace PrintingBooksPortal.Services;

public class WatermarkService : IWatermarkService
{
    public byte[] AddHeavyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp)
    {
        return ApplyWatermark(pdfBytes, shopName, userName, timestamp, enabled: true);
    }

    public byte[] ApplyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp, bool enabled)
    {
        return ApplyWatermark(pdfBytes, shopName, userName, timestamp, enabled, null);
    }

    public byte[] ApplyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp, bool enabled, string? customText)
    {
        if (!enabled)
        {
            return pdfBytes;
        }

        string watermarkText = customText ?? $"LICENSED TO: {shopName}\nUSER: {userName}\nDATE: {timestamp:yyyy-MM-dd HH:mm}\nDO NOT DISTRIBUTE";

        // Replace placeholders
        watermarkText = watermarkText
            .Replace("{shopName}", shopName)
            .Replace("{userName}", userName)
            .Replace("{date}", timestamp.ToString("yyyy-MM-dd HH:mm"))
            .Replace("{timestamp}", timestamp.ToString("yyyy-MM-dd HH:mm:ss"));

        using var inputStream = new MemoryStream(pdfBytes);
        using var document = PdfReader.Open(inputStream, PdfDocumentOpenMode.Modify);

        foreach (var page in document.Pages)
        {
            using var gfx = XGraphics.FromPdfPage(page, XGraphicsPdfPageOptions.Append);

            var font = new XFont("Arial", 40, XFontStyle.Bold);
            var brush = new XSolidBrush(XColor.FromArgb(128, 128, 128, 128));

            var width = page.Width.Point;
            var height = page.Height.Point;

            var size = gfx.MeasureString(watermarkText, font);

            gfx.Save();
            gfx.TranslateTransform(width / 2, height / 2);
            gfx.RotateTransform(-45);
            gfx.DrawString(watermarkText, font, brush, -size.Width / 2, -size.Height / 2);
            gfx.Restore();
        }

        using var outputStream = new MemoryStream();
        document.Save(outputStream);
        return outputStream.ToArray();
    }
}
