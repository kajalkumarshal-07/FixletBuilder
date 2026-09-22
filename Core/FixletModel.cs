namespace FixletBuilder.Core;

public class FixletModel
{
    public string Title { get; set; } = "";
    public string Category { get; set; } = "Applications";
    public string Source { get; set; } = "";
    public string SourceId { get; set; } = "";
    public string Domain { get; set; } = "";
    public double? DownloadSizeMb { get; set; }

    public string Description { get; set; } = "";
    public List<string> Relevance { get; set; } = new();

    public string ActionId { get; set; } = "Action1";
    public string ActionDescription { get; set; } = "";
    public string ActionScript { get; set; } = "";
    public string SuccessCriteria { get; set; } = "";

    public string Sha1 { get; set; } = "";
    public string Sha256 { get; set; } = "";
    public long? FileSizeBytes { get; set; }

    public string RegistryKeyPath { get; set; } = "";
    public string RegistryValueName { get; set; } = "DisplayVersion";
    public string RegistryValue { get; set; } = "";
}
