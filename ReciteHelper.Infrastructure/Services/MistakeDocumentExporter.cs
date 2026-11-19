using System.IO.Compression;
using System.Xml.Linq;
using ReciteHelper.Core.Entities;
using ReciteHelper.Core.Services;

namespace ReciteHelper.Infrastructure.Services;

public static class MistakeDocumentExporter
{
    private static readonly XNamespace W = "http://schemas.openxmlformats.org/wordprocessingml/2006/main";
    private static readonly XNamespace Types = "http://schemas.openxmlformats.org/package/2006/content-types";
    private static readonly XNamespace Rels = "http://schemas.openxmlformats.org/package/2006/relationships";
    private const string OfficeRel = "http://schemas.openxmlformats.org/officeDocument/2006/relationships/";

    public static void Export(string filePath, string projectName, IReadOnlyList<ReviewMistake> entries)
    {
        using var file = File.Create(filePath);
        using var package = new ZipArchive(file, ZipArchiveMode.Create);
        Write(package, "[Content_Types].xml", new XElement(Types + "Types",
            new XElement(Types + "Default", new XAttribute("Extension", "rels"), new XAttribute("ContentType", "application/vnd.openxmlformats-package.relationships+xml")),
            new XElement(Types + "Override", new XAttribute("PartName", "/word/document.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.document.main+xml")),
            new XElement(Types + "Override", new XAttribute("PartName", "/word/styles.xml"), new XAttribute("ContentType", "application/vnd.openxmlformats-officedocument.wordprocessingml.styles+xml"))));
        Write(package, "_rels/.rels", Relationship("officeDocument", "word/document.xml"));
        Write(package, "word/_rels/document.xml.rels", Relationship("styles", "styles.xml"));
        Write(package, "word/styles.xml", new XElement(W + "styles",
            Style("Normal", "Normal", 23, 100),
            Style("Title", "Title", 44, 220),
            Style("Question", "Question", 26, 90, bold: true, before: 220),
            Style("Metadata", "Metadata", 20, 75),
            Style("Correction", "Correction", 23, 65)));

        var body = new XElement(W + "body",
            Paragraph("Title", Run($"{projectName} 错误列表")),
            Paragraph("Metadata", Run($"共 {entries.Count} 条作答记录，按作答时间倒序排列。")),
            Paragraph("Metadata", Run("遗漏", MistakeTextStyle.Missing), Run("  "), Run("需订正", MistakeTextStyle.Incorrect)));
        foreach (var entry in entries)
        {
            body.Add(Paragraph("Question", Run(entry.Question)));
            body.Add(Paragraph("Metadata", Run($"{entry.Time.ToLocalTime():yyyy-MM-dd HH:mm}  {(entry.IsExam ? "考试" : "练习")}")));
            body.Add(Paragraph("Correction", MistakeParagraph.Format(entry.Corrections).Select(run => Run(run.Text, run.Style)).ToArray()));
        }
        body.Add(Element("sectPr", Element("pgSz", Attribute("w", 12240), Attribute("h", 15840)),
            Element("pgMar", Attribute("top", 1080), Attribute("right", 1260), Attribute("bottom", 1080), Attribute("left", 1260),
                Attribute("header", 720), Attribute("footer", 720), Attribute("gutter", 0))));
        Write(package, "word/document.xml", new XElement(W + "document", body));
    }

    private static XElement Style(string id, string name, int size, int after, bool bold = false, int before = 0)
        => Element("style", Attribute("type", "paragraph"), Attribute("styleId", id),
            Element("name", Attribute("val", name)),
            id == "Normal" ? null : Element("basedOn", Attribute("val", "Normal")),
            Element("pPr", id is "Question" or "Metadata" or "Title" ? Element("keepNext") : null, Element("keepLines"),
                Element("spacing", Attribute("before", before), Attribute("after", after), Attribute("line", 295), Attribute("lineRule", "auto"))),
            Element("rPr", Element("rFonts", Attribute("ascii", "Calibri"), Attribute("hAnsi", "Calibri"),
                Attribute("eastAsia", "Microsoft YaHei")), Element("sz", Attribute("val", size)),
                Element("color", Attribute("val", id == "Metadata" ? "585858" : "000000")),
                bold ? Element("b") : null));

    private static XElement Paragraph(string style, params XElement[] runs)
        => Element("p", Element("pPr", Element("pStyle", Attribute("val", style))), runs);

    private static XElement Run(string text, MistakeTextStyle style = MistakeTextStyle.Plain)
    {
        var parts = text.Replace("\r\n", "\n").Split('\n');
        var properties = style switch
        {
            MistakeTextStyle.Missing => Element("rPr", Element("color", Attribute("val", MistakeParagraph.MissingForeground)),
                Element("shd", Attribute("val", "clear"), Attribute("fill", MistakeParagraph.MissingBackground))),
            MistakeTextStyle.Incorrect => Element("rPr", Element("color", Attribute("val", MistakeParagraph.IncorrectForeground))),
            _ => null
        };
        var run = Element("r", properties);
        for (var index = 0; index < parts.Length; index++)
        {
            if (index > 0) run.Add(Element("br"));
            run.Add(Element("t", new XAttribute(XNamespace.Xml + "space", "preserve"), parts[index]));
        }
        return run;
    }

    private static XElement Element(string name, params object?[] content) => new(W + name, content);
    private static XAttribute Attribute(string name, object value) => new(W + name, value);

    private static XElement Relationship(string type, string target)
        => new(Rels + "Relationships", new XElement(Rels + "Relationship",
            new XAttribute("Id", "rId1"), new XAttribute("Type", OfficeRel + type), new XAttribute("Target", target)));

    private static void Write(ZipArchive package, string name, XElement root)
    {
        using var stream = package.CreateEntry(name).Open();
        new XDocument(root).Save(stream);
    }
}
