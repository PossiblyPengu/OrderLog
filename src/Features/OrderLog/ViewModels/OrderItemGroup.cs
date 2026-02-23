using System.Collections.ObjectModel;
using System.Linq;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.ViewModels;

public class OrderItemGroup
{
    public ObservableCollection<OrderItem> Members { get; } = new();

    public OrderItem? First => Members.FirstOrDefault();

    public int Count => Members.Count;

    public bool IsGroup => Count > 1;

    public Guid? LinkedGroupId => First?.LinkedGroupId;

    /// <summary>
    /// Determines whether this group should use the merged/linked UI.
    /// Only true when a LinkedGroupId exists and there is more than one member.
    /// This avoids rendering the merged template for single items with empty/null IDs.
    /// </summary>
    public bool ShouldRenderMerged => LinkedGroupId != null && Count > 1;

    public OrderItemGroup() { }

    public OrderItemGroup(IEnumerable<OrderItem> items)
    {
        foreach (var it in items)
            Members.Add(it);
    }
}
