using System.IO;
using System.IO.Compression;
using System.Text.Json;
using System.Xml.Linq;
using GraphDBViewerWeb.Code;

namespace GraphDBViewerWeb.Tests;

public class ExcelExportTests
{
    private static string ReadEntry(ZipArchive zip, string path)
    {
        var entry = zip.GetEntry(path);
        Assert.NotNull(entry);

        using var stream = entry.Open();
        using var reader = new StreamReader(stream);
        return reader.ReadToEnd();
    }

    [Fact]
    public void BuildXlsx_ProducesValidZip_WithColoredNodeRow()
    {
        var json = """
        [
          { "id": "1", "label": "person", "properties": { "name": "Alice" } },
          { "id": "2", "label": "product", "properties": { } }
        ]
        """;
        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);
        var styles = new Dictionary<string, LabelStyle> { ["person"] = new LabelStyle { Color = "#ff0000" } };

        var bytes = ExcelExport.BuildXlsx(table, styles);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        Assert.NotNull(zip.GetEntry("[Content_Types].xml"));
        Assert.NotNull(zip.GetEntry("xl/workbook.xml"));

        var stylesXml = ReadEntry(zip, "xl/styles.xml");
        var sheetXml = ReadEntry(zip, "xl/worksheets/sheet1.xml");

        //Both parts are well-formed XML.
        XDocument.Parse(stylesXml);
        XDocument.Parse(sheetXml);

        //The person label's color is emitted as an ARGB solid fill.
        Assert.Contains("FFFF0000", stylesXml);

        //Vertex data is present in the sheet.
        Assert.Contains("person", sheetXml);
        Assert.Contains("Alice", sheetXml);
    }

    [Fact]
    public void BuildXlsx_NoStyles_StillValid()
    {
        var json = """
        [ { "id": "1", "label": "person", "properties": {} } ]
        """;
        var table = GraphDataConverter.ToTable(JsonDocument.Parse(json).RootElement);

        var bytes = ExcelExport.BuildXlsx(table, null);

        using var ms = new MemoryStream(bytes);
        using var zip = new ZipArchive(ms, ZipArchiveMode.Read);

        XDocument.Parse(ReadEntry(zip, "xl/worksheets/sheet1.xml"));
        XDocument.Parse(ReadEntry(zip, "xl/styles.xml"));
    }
}
