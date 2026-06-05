using RemoteManager.Models;

namespace RemoteManager.Services;

public interface IImportExportService
{
    Task ExportToFileAsync(string filePath);
    Task ImportFromFileAsync(string filePath);
    Task ExportEncryptedAsync(string filePath, string password);
    Task ImportEncryptedAsync(string filePath, string password);
    Task<ImportPreview> PreviewImportAsync(string filePath);
    Task<ImportPreview> PreviewImportEncryptedAsync(string filePath, string password);
    Task<ExportData?> LoadFromFileAsync(string filePath);
}
