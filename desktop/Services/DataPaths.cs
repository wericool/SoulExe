using System;
using System.IO;

namespace SoulTextWpf.Services;

public sealed class DataPaths
{
    public const string PortableDataFolderName = "SoulExeData";
    private const string LegacyPortableDataFolderName = "SoulTextData";
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

    public DataPaths()
    {
        Root = Path.Combine(AppContext.BaseDirectory, PortableDataFolderName);
        MigrateLegacyPortableData();
        MigrateLegacyFileNames();
        EnsureDirectories();
    }

    private void MigrateLegacyPortableData()
    {
        var legacyRoot = Path.Combine(AppContext.BaseDirectory, LegacyPortableDataFolderName);
        if (!Directory.Exists(legacyRoot)) return;
        if (!Directory.Exists(Root))
        {
            Directory.Move(legacyRoot, Root);
            return;
        }

        foreach (var source in Directory.EnumerateFileSystemEntries(legacyRoot))
        {
            var target = Path.Combine(Root, Path.GetFileName(source));
            if (File.Exists(target) || Directory.Exists(target)) continue;
            if (Directory.Exists(source)) Directory.Move(source, target);
            else File.Move(source, target);
        }
    }

    private void MigrateLegacyFileNames()
    {
        MoveLegacyFile("soultext.json", "soulexe.json");
        MoveLegacyFile("soultext.db", "soulexe.db");
    }

    private void MoveLegacyFile(string oldName, string newName)
    {
        var oldPath = Path.Combine(Root, oldName);
        var newPath = Path.Combine(Root, newName);
        if (File.Exists(oldPath) && !File.Exists(newPath)) File.Move(oldPath, newPath);
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
