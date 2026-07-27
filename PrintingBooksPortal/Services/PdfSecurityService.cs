using System.Text;
using iText.Kernel.Pdf;

namespace PrintingBooksPortal.Services;

public class PdfSecurityService : IPdfSecurityService
{
    public byte[] EncryptPdfWithPassword(byte[] pdfBytes, string userPassword, string ownerPassword)
    {
        using var inputStream = new MemoryStream(pdfBytes);
        using var outputStream = new MemoryStream();

        var reader = new PdfReader(inputStream);
        var writerProperties = new WriterProperties()
            .SetStandardEncryption(
                Encoding.UTF8.GetBytes(userPassword),
                Encoding.UTF8.GetBytes(ownerPassword),
                EncryptionConstants.ALLOW_PRINTING,
                EncryptionConstants.ENCRYPTION_AES_128
            );
        var writer = new PdfWriter(outputStream, writerProperties);

        using var pdfDoc = new PdfDocument(reader, writer);
        pdfDoc.Close();

        return outputStream.ToArray();
    }
}
