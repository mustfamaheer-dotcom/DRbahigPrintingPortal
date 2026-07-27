namespace PrintingBooksPortal.Services;

public interface IPdfSecurityService
{
    byte[] EncryptPdfWithPassword(byte[] pdfBytes, string userPassword, string ownerPassword);
}
