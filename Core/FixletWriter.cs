using System.IO;
using System.Text;
using System.Text.Json;
using System.Xml.Linq;
using Microsoft.Extensions.Logging;

namespace FixletBuilder.Core;

public sealed class FixletWriter : IFixletWriter
{
    private readonly ILogger<FixletWriter> _logger;

    public FixletWriter(ILogger<FixletWriter> logger)
    {
        _logger = logger;
    }

    public string GenerateXml(FixletModel m)
    {
        ArgumentNullException.ThrowIfNull(m);

        var fixlet = new XElement("Fixlet");

        if (!string.IsNullOrWhiteSpace(m.Title))
            fixlet.Add(new XElement("Title", SanitizeForXml(m.Title)));

        if (!string.IsNullOrWhiteSpace(m.Description))
            fixlet.Add(new XElement("Description", new XCData(SanitizeForXml(m.Description))));

        foreach (var rel in m.Relevance)
        {
            var r = rel?.Trim() ?? "";
            if (r.Length > 0)
            {
                if (r.Length > MaxRelevanceLength)
                    throw new InvalidException($"Relevance expression too long ({r.Length} > {MaxRelevanceLength}).");
                fixlet.Add(new XElement("Relevance", r));
            }
        }

        if (!string.IsNullOrWhiteSpace(m.Category))
            fixlet.Add(new XElement("Category", SanitizeForXml(m.Category)));

        if (m.DownloadSizeMb is > 0 and <= MaxDownloadSizeMb)
            fixlet.Add(new XElement("DownloadSize",
                new XAttribute("Units", "M"),
                m.DownloadSizeMb.Value));

        if (!string.IsNullOrWhiteSpace(m.Source))
            fixlet.Add(new XElement("Source", SanitizeForXml(m.Source)));

        if (!string.IsNullOrWhiteSpace(m.SourceId))
            fixlet.Add(new XElement("SourceID", SanitizeForXml(m.SourceId)));

        if (!string.IsNullOrWhiteSpace(m.Domain))
            fixlet.Add(new XElement("Domain", SanitizeForXml(m.Domain)));

        var actionId = string.IsNullOrWhiteSpace(m.ActionId) ? "Action1" : SanitizeForXml(m.ActionId);
        var action = new XElement("Action",
            new XAttribute("ID", actionId),
            new XAttribute("MIMEField", "\"action1\":1"),
            new XElement("Description", SanitizeForXml(m.ActionDescription ?? "")),
            new XElement("ActionScript",
                new XAttribute("MIMEType", "application/x-Fixlet-Win32-Script"),
                new XCData(m.ActionScript ?? "")));

        if (!string.IsNullOrWhiteSpace(m.SuccessCriteria))
            action.Add(new XElement("SuccessCriteria", new XCData(m.SuccessCriteria)));

        fixlet.Add(action);

        var xsi = XNamespace.Get("http://www.w3.org/2001/XMLSchema-instance");
        var body = new XElement("BES",
            new XAttribute(XNamespace.Xmlns + "xsi", xsi),
            new XAttribute(xsi + "noNamespaceSchemaLocation", "BES.xsd"),
            fixlet);

        return "<?xml version=\"1.0\" encoding=\"utf-8\"?>" + Environment.NewLine + body.ToString();
    }

    public void WriteBes(string filePath, FixletModel model)
    {
        var fullPath = ValidateOutputPath(filePath);
        var xml = GenerateXml(model);
        File.WriteAllText(fullPath, xml, new UTF8Encoding(false));
        _logger.LogInformation("Wrote fixlet: {Path} ({Bytes} bytes)", fullPath, xml.Length);
    }

    public void WriteJson(string filePath, FixletModel model)
    {
        var fullPath = ValidateOutputPath(filePath);
        var json = JsonSerializer.Serialize(model, new JsonSerializerOptions { WriteIndented = true });
        File.WriteAllText(fullPath, json, new UTF8Encoding(false));
        _logger.LogInformation("Wrote JSON: {Path}", fullPath);
    }

    private const int MaxRelevanceLength = 8192;
    private const int MaxDownloadSizeMb = 10 * 1024;

    private static string SanitizeForXml(string value)
    {
        if (string.IsNullOrEmpty(value)) return value;
        var invalid = new[] { '\x00', '\x01', '\x02', '\x03', '\x04', '\x05', '\x06', '\x07',
                              '\x08', '\x0B', '\x0C', '\x0E', '\x0F', '\x10', '\x11', '\x12',
                              '\x13', '\x14', '\x15', '\x16', '\x17', '\x18', '\x19', '\x1A',
                              '\x1B', '\x1C', '\x1D', '\x1E', '\x1F' };
        return invalid.Aggregate(value, (current, c) => current.Replace(c, ' '));
    }

    internal static string ValidateOutputPath(string filePath)
    {
        if (string.IsNullOrWhiteSpace(filePath))
            throw new ArgumentException("File path cannot be empty.", nameof(filePath));

        var fullPath = Path.GetFullPath(filePath);
        var ext = Path.GetExtension(fullPath).ToLowerInvariant();
        if (ext != ".bes" && ext != ".json" && ext != ".xml")
            throw new InvalidException($"Refusing to write to '{ext}' file - only .bes, .json, .xml allowed.");

        var dir = Path.GetDirectoryName(fullPath);
        if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
            Directory.CreateDirectory(dir);

        return fullPath;
    }
}

public class InvalidException : Exception
{
    public InvalidException(string message) : base(message) { }
    public InvalidException(string message, Exception inner) : base(message, inner) { }
}