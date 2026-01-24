using Markdig.Wpf;
using System.Text;
using System.Windows;
using System.Windows.Documents;
using System.Windows.Media;
using System.Windows.Threading;

namespace ReciteHelper.Utils;

/// <summary>
/// Provides functionality to apply consistent Markdown styling to a document displayed in a MarkdownViewer, including
/// heading detection and block-level formatting.
/// </summary>
/// <remarks>The Renderer class is designed to ensure that Markdown content is visually formatted according to
/// predefined styles, handling headings, lists, and general block spacing. If the document is not ready for styling,
/// the class uses the Dispatcher to retry applying styles until the document becomes available or a retry limit is
/// reached. This approach helps maintain UI responsiveness and ensures that styles are applied only when the document
/// is fully loaded.</remarks>
/// <param name="viewer">The MarkdownViewer instance that displays the document to be styled.</param>
/// <param name="localThread">The Dispatcher associated with the UI thread, used to schedule style application and retries when the document is
/// not yet ready.</param>
class Renderer(MarkdownViewer viewer, Dispatcher localThread)
{
    private readonly MarkdownViewer viewer = viewer;
    private readonly Dispatcher dispatcher = localThread;

    private string GetParagraphText(Paragraph paragraph)
    {
        var text = new StringBuilder();
        foreach (var inline in paragraph.Inlines)
        {
            if (inline is Run run)
            {
                text.Append(run.Text);
            }
            else if (inline is Span span)
            {
                foreach (var subInline in span.Inlines)
                {
                    if (subInline is Run subRun)
                    {
                        text.Append(subRun.Text);
                    }
                }
            }
        }
        return text.ToString();
    }


    private bool DetectIfHeading(Paragraph paragraph)
    {
        if (paragraph.FontSize > 16 ||
            paragraph.FontWeight == FontWeights.Bold ||
            paragraph.FontWeight == FontWeights.SemiBold)
        {
            return true;
        }

        var text = GetParagraphText(paragraph).Trim();
        if (text.StartsWith("#"))
        {
            return true;
        }

        return false;
    }

    private void ApplyParagraphStyles(Paragraph paragraph)
    {
        bool isHeading = DetectIfHeading(paragraph);

        if (isHeading)
        {
            paragraph.FontFamily = new FontFamily("Arial, Microsoft YaHei UI");
            paragraph.FontWeight = FontWeights.Bold;

            if (paragraph.FontSize >= 20)
            {
                paragraph.Foreground = new SolidColorBrush(Color.FromRgb(26, 54, 93));
            }
            else
            {
                paragraph.Foreground = new SolidColorBrush(Color.FromRgb(45, 55, 72));
            }
        }
        else
        {
            // Body text style
            paragraph.FontFamily = new FontFamily("Times New Roman, Simsun");
            paragraph.LineHeight = 1.6;
            paragraph.TextAlignment = TextAlignment.Left;
            paragraph.Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68));
        }
    }

    private void ApplyBlockStylesRecursive(BlockCollection blocks)
    {
        foreach (var block in blocks)
        {
            if (block is Paragraph paragraph)
            {
                ApplyParagraphStyles(paragraph);
            }
            else if (block is List list)
            {
                // Recursively process list items
                foreach (var listItem in list.ListItems)
                {
                    ApplyBlockStylesRecursive(listItem.Blocks);
                }
            }
            else if (block is Section section)
            {
                // Recursive processing block
                ApplyBlockStylesRecursive(section.Blocks);
            }

            // Set general block spacing
            block.Margin = new Thickness(0, 8, 0, 8);
        }
    }

    private void ApplyStylesToDocument()
    {
        var document = viewer.Document;
        if (document == null) return;

        // Set basic document styles
        document.FontFamily = new FontFamily("Times New Roman, 宋体");
        document.FontSize = 14;
        document.Foreground = new SolidColorBrush(Color.FromRgb(68, 68, 68));

        // Apply block-level styles
        ApplyBlockStylesRecursive(document.Blocks);
    }

    public void ApplyMarkdownStylesWithRetry(int retryCount = 0)
    {
        if (retryCount > 10)
        {
            System.Diagnostics.Debug.WriteLine("应用样式失败：重试次数超限");
            return;
        }

        var document = viewer.Document;
        if (document == null || document.Blocks.Count == 0)
        {
            // Document not ready yet, please retry later
            dispatcher.BeginInvoke(new Action(() =>
            {
                ApplyMarkdownStylesWithRetry(retryCount + 1);
            }), System.Windows.Threading.DispatcherPriority.ApplicationIdle);
            return;
        }

        // Documentation is ready, apply styles
        ApplyStylesToDocument();
    }

}
