using System;
using System.Collections.Generic;

namespace SOUP.Features.OrderLog.Models;

/// <summary>
/// Represents a reusable template for creating orders
/// </summary>
public class OrderTemplate
{
    public Guid Id { get; set; }
    public string Name { get; set; } = string.Empty;
    public string? VendorName { get; set; }
    public string? TransferNumbers { get; set; }
    public string? WhsShipmentNumbers { get; set; }
    public string ColorHex { get; set; } = "#B56576";
    public OrderItem.OrderStatus DefaultStatus { get; set; } = OrderItem.OrderStatus.NotReady;
    public DateTime CreatedAt { get; set; }
    public int UseCount { get; set; }

    /// <summary>
    /// Create an OrderItem from this template
    /// </summary>
    public OrderItem CreateOrder()
    {
        return new OrderItem
        {
            Id = Guid.NewGuid(),
            NoteType = NoteType.Order,
            VendorName = VendorName ?? string.Empty,
            TransferNumbers = TransferNumbers ?? string.Empty,
            WhsShipmentNumbers = WhsShipmentNumbers ?? string.Empty,
            ColorHex = ColorHex,
            Status = DefaultStatus,
            IsArchived = false,
            CreatedAt = DateTime.UtcNow,
            StartedAt = null,
            CompletedAt = null,
            AccumulatedTimeTicks = 0
        };
    }

    /// <summary>
    /// Create a template from an existing OrderItem
    /// </summary>
    public static OrderTemplate FromOrder(OrderItem order, string templateName)
    {
        return new OrderTemplate
        {
            Id = Guid.NewGuid(),
            Name = templateName,
            VendorName = order.VendorName,
            TransferNumbers = order.TransferNumbers,
            WhsShipmentNumbers = order.WhsShipmentNumbers,
            ColorHex = order.ColorHex ?? "#B56576",
            DefaultStatus = order.Status,
            CreatedAt = DateTime.UtcNow,
            UseCount = 0
        };
    }
}

/// <summary>
/// Container for template persistence
/// </summary>
public class OrderTemplateCollection
{
    public int Version { get; set; } = 1;
    public List<OrderTemplate> Templates { get; set; } = new();
}
