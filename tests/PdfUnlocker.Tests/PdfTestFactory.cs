using PdfSharp.Pdf;
using PdfSharp.Pdf.IO;

namespace PdfUnlocker.Tests;

/// <summary>Builds throwaway PDFs for the tests and inspects their encryption state.</summary>
internal static class PdfTestFactory
{
    public static void CreatePlain(string path)
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        doc.Save(path);
    }

    /// <summary>Creates a PDF encrypted with the given user password using AES-256 (revision 5).</summary>
    public static void CreateEncryptedV5(string path, string userPassword)
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        var handler = doc.SecurityHandler;
        handler.SetEncryptionToV5();
        handler.UserPassword = userPassword;
        doc.Save(path);
    }

    /// <summary>Creates a PDF encrypted with the older AES-128 (revision 4) scheme.</summary>
    public static void CreateEncryptedV4Aes(string path, string userPassword)
    {
        using var doc = new PdfDocument();
        doc.AddPage();
        var handler = doc.SecurityHandler;
        handler.SetEncryptionToV4UsingAES();
        handler.UserPassword = userPassword;
        doc.Save(path);
    }

    /// <summary>True if the file opens with no password (i.e. it is not encrypted).</summary>
    public static bool OpensWithoutPassword(string path)
    {
        try
        {
            using var _ = PdfReader.Open(path, PdfDocumentOpenMode.Import);
            return true;
        }
        catch (PdfReaderException)
        {
            return false;
        }
    }
}
