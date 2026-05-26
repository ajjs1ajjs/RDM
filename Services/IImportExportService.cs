using RemoteManager.Models;

namespace RemoteManager.Services;

public interface IImportExportService
{
    void ExportToFile(string filePath);
    void ImportFromFile(string filePath);
    void ExportEncrypted(string filePath, string password);
    void ImportEncrypted(string filePath, string password);
    ImportPreview PreviewImport(string filePath);
    ImportPreview PreviewImportEncrypted(string filePath, string password);
    ExportData? LoadFromFile(string filePath);
}
