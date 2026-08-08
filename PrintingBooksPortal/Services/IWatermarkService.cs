namespace PrintingBooksPortal.Services;

public interface IWatermarkService
{
    byte[] AddHeavyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp);
    byte[] ApplyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp, bool enabled);
    byte[] ApplyWatermark(byte[] pdfBytes, string shopName, string userName, DateTime timestamp, bool enabled, string customText);
    byte[] ApplyWatermarkWithTenant(byte[] pdfBytes, string tenantName, string shopName, string userName, DateTime timestamp, bool enabled, string? customText);
}

