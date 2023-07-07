namespace RplaceModtools.Models;

public class ServerPreset
{
    public string? Websocket { get; set; }
    public string? FileServer { get; set; }
    public string? AdminKey { get; set; }
    public string PlacePath { get; set; } = "/place";
    public string BackupListPath { get; set; } = "/backuplist";
    public string BackupsPath { get; set; } = "/backups/";
}