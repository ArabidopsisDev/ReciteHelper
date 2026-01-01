using Docnet.Core;
using Docnet.Core.Models;
using Spire.Doc;
using Spire.Presentation;
using System.IO;
using System.Text;

namespace ReciteHelper.Utils;

public class ExtractText
{
    private static string FromPDF(string filePath)
    {
        var docNetInstance = DocLib.Instance;
        var pageDimensions = new PageDimensions(1080, 1920);

        string fullText = "";

        // Start reading the document
        using (var docReader = docNetInstance.GetDocReader(filePath, pageDimensions))
        {
            int pageCount = docReader.GetPageCount();

            for (int pageIndex = 0; pageIndex < pageCount; pageIndex++)
            {
                using var pageReader = docReader.GetPageReader(pageIndex);
                string pageText = pageReader.GetText();
                fullText += pageText + "\n";
            }
        }

        return fullText;
    }

    private static string FromPresentation(string filePath)
    {
        // If you are not sure whether a method works or not, just assume that it works well
        using var presentation = new Presentation();

        presentation.LoadFromFile(filePath);
        StringBuilder text = new StringBuilder();

        foreach (ISlide slide in presentation.Slides)
            foreach (IShape shape in slide.Shapes)
                if (shape is IAutoShape)
                    foreach (TextParagraph paragraph in ((IAutoShape)shape).TextFrame.Paragraphs)
                        text.AppendLine(paragraph.Text);

        return text.ToString();
    }

    private static string FromDocument(string filePath)
    {
        using var doc = new Document();
        doc.LoadFromFile(filePath);

        return doc.GetText();
    }

    private static string FromText(string filePath) 
    {
        return File.ReadAllText(filePath);
    }

    public static string FromAutomatic(string filePath)
    {
        // In fact, dependency inversion would be better
        // but there aren't that many types of files to support, right?

        var extension = Path.GetExtension(filePath).ToLower();

        var text = extension[1..] switch
        {
            "pdf" => ExtractText.FromPDF(filePath),
            "docx" or "doc" => ExtractText.FromDocument(filePath),
            "pptx" or "ppt" => ExtractText.FromPresentation(filePath),
            "txt" or "meg" => ExtractText.FromText(filePath),
            _ => string.Empty
        };

        return text;
    }
}
