using System;
using System.IO;

namespace SoulExe.Services;

public sealed class DataPaths
{
    public const string PortableDataFolderName = "SoulExeData";
    public string Root { get; }
    public string DatabasePath => Path.Combine(Root, "soulexe.db");
    public string AvatarDirectory => Path.Combine(Root, "avatars");
    public string AttachmentDirectory => Path.Combine(Root, "attachments");
    public string ExportDirectory => Path.Combine(Root, "exports");
    public string ImportBackupDirectory => Path.Combine(Root, "import_backups");
    public string EngineDirectory => Path.Combine(Root, "engine");
    public string ModelDirectory => Path.Combine(Root, "models");
    public string EmbeddingDirectory => Path.Combine(ModelDirectory, "embeddings");
    public string LogDirectory => Path.Combine(Root, "logs");
    public string BackupDirectory => Path.Combine(Root, "backups");
    public string DataFilePath => Path.Combine(Root, "soulexe.json");

    public DataPaths(string? baseDirectory = null)
    {
        Root = Path.Combine(baseDirectory ?? AppContext.BaseDirectory, PortableDataFolderName);
        EnsureDirectories();
    }

    public void EnsureDirectories()
    {
        try
        {
            foreach (var directory in new[]
                     {
                         Root, AvatarDirectory, AttachmentDirectory, ExportDirectory, ImportBackupDirectory,
                         EngineDirectory, ModelDirectory, EmbeddingDirectory, LogDirectory
                     })
            {
                Directory.CreateDirectory(directory);
            }
            var probe = Path.Combine(Root, $".write-probe-{Guid.NewGuid():N}");
            File.WriteAllText(probe, "ok");
            File.Delete(probe);
        }
        catch (Exception exception)
        {
            throw new IOException($"SoulExe должен хранить данные рядом с программой, но не может записать в «{Root}». Переместите приложение в папку с правом записи (например, C:\\Apps\\SoulExe) и запустите его снова.", exception);
        }
    }
}
