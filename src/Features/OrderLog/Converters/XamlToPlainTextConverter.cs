using System;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Data;
using System.Windows.Documents;

namespace SOUP.Features.OrderLog.Converters;

/// <summary>
/// Converts XAML FlowDocument content to plain text for display in TextBox/TextBlock.
/// Handles double-encoded XAML content gracefully.
/// </summary>
public partial class XamlToPlainTextConverter : IValueConverter
{
    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        if (value is not string xaml || string.IsNullOrWhiteSpace(xaml))
            return string.Empty;

        return ExtractPlainText(xaml);
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture)
    {
        return DependencyProperty.UnsetValue;
    }

    private static string ExtractPlainText(string content)
    {
        // If it's not XAML, return as-is
        if (!content.TrimStart().StartsWith("<Section", StringComparison.OrdinalIgnoreCase) &&
            !content.TrimStart().StartsWith("<Paragraph", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        try
        {
            // Try to load as XAML and extract text
            var bytes = Encoding.UTF8.GetBytes(content);
            using var ms = new MemoryStream(bytes);
            
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Load(ms, DataFormats.Xaml);
            
            var text = range.Text.Trim();
            
            // Check if the extracted text is also XAML (double-encoded)
            if (text.TrimStart().StartsWith("<Section", StringComparison.OrdinalIgnoreCase) ||
                text.TrimStart().StartsWith("<Paragraph", StringComparison.OrdinalIgnoreCase))
            {
                // Recursively extract from the inner XAML
                return ExtractPlainText(text);
            }
            
            return text;
        }
        catch
        {
            // Fallback: strip XML tags using regex
            return StripXmlTags(content);
        }
    }

    private static string StripXmlTags(string xaml)
    {
        // Remove XML tags and decode HTML entities
        var text = XmlTagRegex().Replace(xaml, "");
        text = text.Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&amp;", "&")
                   .Replace("&quot;", "\"")
                   .Replace("&#xD;", "\r")
                   .Replace("&#xA;", "\n");
        
        // If still looks like XAML after decoding, strip again
        if (text.Contains("<Section") || text.Contains("<Paragraph"))
        {
            text = XmlTagRegex().Replace(text, "");
        }
        
        return text.Trim();
    }

    [GeneratedRegex("<[^>]+>")]
    private static partial Regex XmlTagRegex();
}
