using System;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Documents;
using System.Windows.Input;
using System.Windows.Media;
using SOUP.Features.OrderLog.Models;
using SOUP.Features.OrderLog.ViewModels;

namespace SOUP.Features.OrderLog.Helpers;

/// <summary>
/// Helper class for text formatting operations on note content TextBoxes.
/// Eliminates duplicate code between OrderLogView and OrderLogWidgetView.
/// </summary>
public static class TextFormattingHelper
{
    /// <summary>
    /// Finds the NoteContent RichTextBox by walking up the visual tree from a button.
    /// </summary>
    public static RichTextBox? FindNoteContentRichTextBox(object sender, FrameworkElement? view = null)
    {
        if (sender is not DependencyObject start) return null;
        // If a view is provided, prefer searching the view's visual tree for an rtb
        // whose DataContext matches the OrderItem (common case when buttons live in a separate toolbar).
        OrderItem? targetOrder = null;
        if (sender is Button b && b.Tag is OrderItem bo) targetOrder = bo;
        if (sender is FrameworkElement fe && fe.DataContext is OrderItem fde) targetOrder ??= fde;

        if (view != null && targetOrder != null)
        {
            var found = FindDescendantRichTextBoxes(view).FirstOrDefault(r => r.DataContext == (object?)targetOrder);
            if (found != null) return found;
        }

        // Fallback: walk up the visual tree from the sender and look for a RichTextBox child on each ancestor
        var parent = VisualTreeHelper.GetParent(start);
        while (parent != null)
        {
            for (int i = 0; i < VisualTreeHelper.GetChildrenCount(parent); i++)
            {
                var child = VisualTreeHelper.GetChild(parent, i);
                if (child is RichTextBox rtb) return rtb;
            }

            parent = VisualTreeHelper.GetParent(parent);
        }

        // As a last resort, if a view was provided search it for any RichTextBox
        if (view != null)
            return FindDescendantRichTextBoxes(view).FirstOrDefault();

        return null;
    }

    private static System.Collections.Generic.IEnumerable<RichTextBox> FindDescendantRichTextBoxes(DependencyObject root)
    {
        if (root == null) yield break;
        var count = VisualTreeHelper.GetChildrenCount(root);
        for (int i = 0; i < count; i++)
        {
            var child = VisualTreeHelper.GetChild(root, i);
            if (child is RichTextBox rtb) yield return rtb;
            foreach (var desc in FindDescendantRichTextBoxes(child))
                yield return desc;
        }
    }

    /// <summary>
    /// Save the RichTextBox document as XAML string.
    /// </summary>
    private static string GetDocumentXaml(RichTextBox rtb)
    {
        var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        using var ms = new MemoryStream();
        range.Save(ms, DataFormats.Xaml);
        ms.Position = 0;
        using var sr = new StreamReader(ms, Encoding.UTF8);
        return sr.ReadToEnd();
    }

    /// <summary>
    /// Load the RichTextBox document from XAML string.
    /// </summary>
    private static void LoadDocumentXaml(RichTextBox rtb, string? xaml)
    {
        rtb.Document.Blocks.Clear();
        if (string.IsNullOrEmpty(xaml)) return;

        // Extract the actual plain text content, handling any level of double-encoding
        var plainText = ExtractPlainTextFromXaml(xaml);
        
        // If we got plain text (not XAML), just insert it directly
        if (!string.IsNullOrEmpty(plainText) && 
            !plainText.TrimStart().StartsWith("<Section", StringComparison.OrdinalIgnoreCase) &&
            !plainText.TrimStart().StartsWith("<Paragraph", StringComparison.OrdinalIgnoreCase))
        {
            // Insert as plain text with proper line breaks (single paragraph with LineBreaks)
            var para = new Paragraph { Margin = new Thickness(0) };
            var lines = plainText.Split(new[] { "\r\n", "\r", "\n" }, StringSplitOptions.None);
            for (int i = 0; i < lines.Length; i++)
            {
                para.Inlines.Add(new Run(lines[i]));
                if (i < lines.Length - 1)
                    para.Inlines.Add(new LineBreak());
            }
            rtb.Document.Blocks.Add(para);
            ResetTextForeground(rtb);
            return;
        }

        // Try to load as XAML (for non-corrupted content)
        try
        {
            var bytes = Encoding.UTF8.GetBytes(xaml);
            using var ms = new MemoryStream(bytes);
            var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
            range.Load(ms, DataFormats.Xaml);
            
            // Check if loaded result is XAML text (double-encoded) - if so, show plain text instead
            var loadedText = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd).Text;
            if (loadedText.TrimStart().StartsWith("<Section", StringComparison.OrdinalIgnoreCase) ||
                loadedText.TrimStart().StartsWith("<Paragraph", StringComparison.OrdinalIgnoreCase))
            {
                // Still showing XAML - extract and show plain text
                rtb.Document.Blocks.Clear();
                var finalText = ExtractPlainTextFromXaml(loadedText);
                rtb.Document.Blocks.Add(new Paragraph(new Run(finalText)));
            }
            
            ResetTextForeground(rtb);
        }
        catch
        {
            // Fallback: insert plain text
            rtb.Document.Blocks.Clear();
            rtb.Document.Blocks.Add(new Paragraph(new Run(plainText ?? xaml)));
        }
    }

    /// <summary>
    /// Extracts plain text from potentially double-encoded XAML content.
    /// </summary>
    private static string ExtractPlainTextFromXaml(string content, int maxDepth = 10)
    {
        if (string.IsNullOrEmpty(content) || maxDepth <= 0) return content;
        
        // If it doesn't look like XAML, return as-is
        if (!content.TrimStart().StartsWith("<Section", StringComparison.OrdinalIgnoreCase) &&
            !content.TrimStart().StartsWith("<Paragraph", StringComparison.OrdinalIgnoreCase))
        {
            return content;
        }

        try
        {
            var bytes = Encoding.UTF8.GetBytes(content);
            using var ms = new MemoryStream(bytes);
            var doc = new FlowDocument();
            var range = new TextRange(doc.ContentStart, doc.ContentEnd);
            range.Load(ms, DataFormats.Xaml);
            
            var extractedText = range.Text.Trim();
            
            // If extracted text is still XAML, recurse
            if (extractedText.TrimStart().StartsWith("<Section", StringComparison.OrdinalIgnoreCase) ||
                extractedText.TrimStart().StartsWith("<Paragraph", StringComparison.OrdinalIgnoreCase))
            {
                return ExtractPlainTextFromXaml(extractedText, maxDepth - 1);
            }
            
            return extractedText;
        }
        catch
        {
            // If XAML parsing fails, try regex extraction as last resort
            return StripXmlTagsRegex(content);
        }
    }

    /// <summary>
    /// Strips XML tags using regex as a fallback.
    /// </summary>
    private static string StripXmlTagsRegex(string xaml)
    {
        // Remove XML tags
        var text = System.Text.RegularExpressions.Regex.Replace(xaml, "<[^>]+>", "");
        // Decode common HTML entities
        text = text.Replace("&lt;", "<")
                   .Replace("&gt;", ">")
                   .Replace("&amp;", "&")
                   .Replace("&quot;", "\"")
                   .Replace("&#xD;", "\r")
                   .Replace("&#xA;", "\n");
        
        // If still has XAML tags after decoding, strip again
        if (text.Contains("<Section") || text.Contains("<Paragraph") || text.Contains("<Run"))
        {
            text = System.Text.RegularExpressions.Regex.Replace(text, "<[^>]+>", "");
        }
        
        return text.Trim();
    }

    /// <summary>
    /// Resets all text foreground colors in the document to inherit from the RichTextBox.
    /// This ensures theme-adaptive text colors.
    /// </summary>
    private static void ResetTextForeground(RichTextBox rtb)
    {
        var range = new TextRange(rtb.Document.ContentStart, rtb.Document.ContentEnd);
        range.ApplyPropertyValue(TextElement.ForegroundProperty, DependencyProperty.UnsetValue);
    }

    /// <summary>
    /// Inserts text at the caret of a RichTextBox.
    /// </summary>
    public static void InsertTextAtCaret(RichTextBox rtb, string insertText, bool newLine = false)
    {
        if (rtb == null) return;
        var pos = rtb.CaretPosition ?? rtb.Document.ContentEnd;
        if (newLine)
        {
            // Move to end of current paragraph and insert a new paragraph
            var para = pos.Paragraph ?? rtb.Document.Blocks.LastBlock as Paragraph;
            var insertPos = para != null ? para.ContentEnd : rtb.Document.ContentEnd;
            insertPos.InsertTextInRun("\n" + insertText);
            rtb.CaretPosition = insertPos.GetPositionAtOffset(1 + insertText.Length) ?? rtb.Document.ContentEnd;
        }
        else
        {
            pos.InsertTextInRun(insertText);
            rtb.CaretPosition = pos.GetPositionAtOffset(insertText.Length) ?? rtb.Document.ContentEnd;
        }
        rtb.Focus();
    }

    /// <summary>
    /// Inserts a prefix (bullet, checkbox, number) at the start of the current line.
    /// If the line already has this prefix, removes it (toggle behavior).
    /// </summary>
    public static void InsertPrefixAtLineStart(RichTextBox rtb, string prefix)
    {
        if (rtb == null) return;
        
        var para = rtb.CaretPosition?.Paragraph;
        if (para == null)
        {
            // No paragraph - create one with the prefix
            para = new Paragraph(new Run(prefix)) { Margin = new Thickness(0) };
            rtb.Document.Blocks.Add(para);
            rtb.CaretPosition = para.ContentEnd;
            return;
        }

        // Get current line text
        var lineRange = new TextRange(para.ContentStart, para.ContentEnd);
        var lineText = lineRange.Text;

        // Check if line already starts with this prefix - toggle it off
        if (lineText.StartsWith(prefix))
        {
            // Remove the prefix
            var startPos = para.ContentStart.GetPositionAtOffset(0, LogicalDirection.Forward);
            var endPos = para.ContentStart.GetPositionAtOffset(prefix.Length, LogicalDirection.Forward);
            if (startPos != null && endPos != null)
            {
                var prefixRange = new TextRange(startPos, endPos);
                prefixRange.Text = "";
            }
        }
        // Check if line starts with a different prefix - replace it
        else if (lineText.StartsWith("• ") || lineText.StartsWith("☐ ") || lineText.StartsWith("☑ ") || 
                 System.Text.RegularExpressions.Regex.IsMatch(lineText, @"^\d+\. "))
        {
            // Find existing prefix length
            int existingPrefixLen = 2; // Default for bullet/checkbox
            var numMatch = System.Text.RegularExpressions.Regex.Match(lineText, @"^(\d+\. )");
            if (numMatch.Success)
                existingPrefixLen = numMatch.Groups[1].Length;
            
            // Replace the prefix
            var startPos = para.ContentStart.GetPositionAtOffset(0, LogicalDirection.Forward);
            var endPos = para.ContentStart.GetPositionAtOffset(existingPrefixLen, LogicalDirection.Forward);
            if (startPos != null && endPos != null)
            {
                var prefixRange = new TextRange(startPos, endPos);
                prefixRange.Text = prefix;
            }
        }
        else
        {
            // Insert prefix at the start of the line
            var insertPos = para.ContentStart.GetInsertionPosition(LogicalDirection.Forward);
            insertPos?.InsertTextInRun(prefix);
        }
        
        rtb.Focus();
    }

    /// <summary>
    /// Updates the NoteContent property on the OrderItem by serializing the RichTextBox document.
    /// </summary>
    public static async void UpdateNoteContent(object sender, FrameworkElement view)
    {
        OrderItem? order = null;
        if (sender is Button btn && btn.Tag is OrderItem o) order = o;
        if (order == null && sender is FrameworkElement fe && fe.DataContext is OrderItem oc) order = oc;
        if (order == null) return;

        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            order.NoteContent = GetDocumentXaml(rtb);
            if (view.DataContext is OrderLogViewModel vm)
            {
                try
                {
                    await vm.SaveAsync();
                }
                catch (Exception ex)
                {
                    Serilog.Log.Warning(ex, "Failed to save note content");
                }
            }
        }
    }

    /// <summary>
    /// Updates the NoteContent property directly from a RichTextBox.
    /// </summary>
    public static void UpdateNoteContentFromRichTextBox(RichTextBox rtb, OrderItem order)
    {
        if (rtb == null || order == null) return;
        order.NoteContent = GetDocumentXaml(rtb);
    }

    /// <summary>
    /// Handles auto-continuation of list items (bullets, checkboxes, numbered lists) when Enter is pressed.
    /// </summary>
    public static void HandleListAutoContinuation(RichTextBox rtb, KeyEventArgs e)
    {
        if (e.Key != Key.Enter) return;

        try
        {
            var para = rtb.CaretPosition.Paragraph;
            if (para == null) return;
            var currentLine = new TextRange(para.ContentStart, para.ContentEnd).Text ?? string.Empty;
            string? prefix = DetectListPrefix(currentLine);
            if (prefix != null)
            {
                // Move caret to end of paragraph and insert new paragraph with prefix
                var end = para.ContentEnd;
                end.InsertTextInRun("\n" + prefix);
                rtb.CaretPosition = end.GetPositionAtOffset(1 + prefix.Length) ?? rtb.Document.ContentEnd;
                e.Handled = true;
            }
        }
        catch
        {
            // ignore
        }
    }

    private static string? DetectListPrefix(string currentLine)
    {
        var indent = currentLine.Length - currentLine.TrimStart().Length;
        var trimmed = currentLine.TrimStart();

        // Check for bullet point (• )
        if (trimmed.StartsWith("• "))
            return new string(' ', indent) + "• ";

        // Check for checkbox empty (☐ )
        if (trimmed.StartsWith("☐ "))
            return new string(' ', indent) + "☐ ";

        // Check for checkbox checked (☑ ) - reset to empty checkbox
        if (trimmed.StartsWith("☑ "))
            return new string(' ', indent) + "☐ ";

        // Check for dash list (- )
        if (trimmed.StartsWith("- "))
            return new string(' ', indent) + "- ";

        // Check for numbered list (1. , 2. , etc.)
        var match = Regex.Match(trimmed, @"^(\d+)\.\s");
        if (match.Success && int.TryParse(match.Groups[1].Value, out int num))
            return new string(' ', indent) + $"{num + 1}. ";

        return null;
    }

    private static bool IsEmptyListItem(string trimmedLine)
    {
        return trimmedLine == "• " ||
               trimmedLine == "☐ " ||
               trimmedLine == "☑ " ||
               trimmedLine == "- " ||
               Regex.IsMatch(trimmedLine, @"^\d+\.\s*$");
    }

    // Click handler methods that can be called from code-behind
    public static void FormatBold(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            rtb.Focus();
            EditingCommands.ToggleBold.Execute(null, rtb);
            UpdateNoteContent(sender, view);
        }
    }

    public static void FormatItalic(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            rtb.Focus();
            EditingCommands.ToggleItalic.Execute(null, rtb);
            UpdateNoteContent(sender, view);
        }
    }

    public static void FormatUnderline(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            rtb.Focus();
            EditingCommands.ToggleUnderline.Execute(null, rtb);
            UpdateNoteContent(sender, view);
        }
    }

    public static void InsertBullet(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            rtb.Focus();
            InsertPrefixAtLineStart(rtb, "• ");
            UpdateNoteContent(sender, view);
        }
    }

    public static void InsertCheckbox(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            rtb.Focus();
            InsertPrefixAtLineStart(rtb, "☐ ");
            UpdateNoteContent(sender, view);
        }
    }

    public static void InsertTimestamp(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender, view);
        if (rtb != null)
        {
            var timestamp = DateTime.Now.ToString("MM/dd HH:mm");
            InsertTextAtCaret(rtb, $"[{timestamp}] ");
            UpdateNoteContent(sender, view);
        }
    }

    public static void InsertDivider(object sender, FrameworkElement view)
    {
        var rtb = FindNoteContentRichTextBox(sender);
        if (rtb != null)
        {
            InsertTextAtCaret(rtb, "────────────", newLine: true);
            UpdateNoteContent(sender, view);
        }
    }

    /// <summary>
    /// Toggles checkbox state at the current line (☐ ↔ ☑).
    /// Returns true if a checkbox was toggled.
    /// </summary>
    public static bool ToggleCheckboxAtCurrentLine(RichTextBox rtb)
    {
        if (rtb == null) return false;
        
        var para = rtb.CaretPosition?.Paragraph;
        if (para == null) return false;

        var lineRange = new TextRange(para.ContentStart, para.ContentEnd);
        var lineText = lineRange.Text;

        // Check if line starts with unchecked checkbox
        if (lineText.StartsWith("☐ "))
        {
            // Toggle to checked
            var startPos = para.ContentStart.GetPositionAtOffset(0, LogicalDirection.Forward);
            var endPos = para.ContentStart.GetPositionAtOffset(1, LogicalDirection.Forward);
            if (startPos != null && endPos != null)
            {
                var checkboxRange = new TextRange(startPos, endPos);
                checkboxRange.Text = "☑";
                return true;
            }
        }
        // Check if line starts with checked checkbox
        else if (lineText.StartsWith("☑ "))
        {
            // Toggle to unchecked
            var startPos = para.ContentStart.GetPositionAtOffset(0, LogicalDirection.Forward);
            var endPos = para.ContentStart.GetPositionAtOffset(1, LogicalDirection.Forward);
            if (startPos != null && endPos != null)
            {
                var checkboxRange = new TextRange(startPos, endPos);
                checkboxRange.Text = "☐";
                return true;
            }
        }

        return false;
    }

    /// <summary>
    /// Checks if the given position in a RichTextBox is over a checkbox character.
    /// </summary>
    public static bool IsPositionOverCheckbox(RichTextBox rtb, Point position)
    {
        if (rtb == null) return false;
        
        var textPos = rtb.GetPositionFromPoint(position, true);
        if (textPos == null) return false;

        var para = textPos.Paragraph;
        if (para == null) return false;

        var lineRange = new TextRange(para.ContentStart, para.ContentEnd);
        var lineText = lineRange.Text;

        // Check if line starts with a checkbox
        if (!lineText.StartsWith("☐ ") && !lineText.StartsWith("☑ "))
            return false;

        // Check if click is near the start of the line (within first ~20 pixels)
        var lineStart = para.ContentStart.GetCharacterRect(LogicalDirection.Forward);
        return position.X < lineStart.X + 20;
    }

    /// <summary>
    /// Load note content from OrderItem into RichTextBox (call on Loaded)
    /// </summary>
    public static void LoadNoteContent(object sender)
    {
        RichTextBox? rtb = null;
        if (sender is RichTextBox direct) rtb = direct;
        else rtb = FindNoteContentRichTextBox(sender);

        if (rtb == null) return;

        if (rtb.DataContext is OrderItem order)
        {
            LoadDocumentXaml(rtb, order.NoteContent);
        }
    }
}
