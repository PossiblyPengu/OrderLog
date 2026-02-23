using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.Extensions.Logging;
using SOUP.Core;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

/// <summary>
/// Service for managing order templates (create, read, update, delete)
/// </summary>
public class OrderTemplateService
{
    private readonly ILogger<OrderTemplateService>? _logger;
    private readonly string _templatesFilePath;
    private List<OrderTemplate> _templates = new();
    private readonly object _lock = new();

    public OrderTemplateService(ILogger<OrderTemplateService>? logger = null)
    {
        _logger = logger;
        _templatesFilePath = Path.Combine(AppPaths.OrderLogDir, "templates.json");
    }

    /// <summary>
    /// Load templates from file
    /// </summary>
    public async Task<List<OrderTemplate>> LoadTemplatesAsync()
    {
        try
        {
            if (!File.Exists(_templatesFilePath))
            {
                _logger?.LogInformation("Templates file not found, starting with empty list");
                _templates = new List<OrderTemplate>();
                return _templates;
            }

            var json = await File.ReadAllTextAsync(_templatesFilePath);
            if (string.IsNullOrWhiteSpace(json))
            {
                _logger?.LogWarning("Templates file is empty");
                _templates = new List<OrderTemplate>();
                return _templates;
            }

            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };

            var collection = JsonSerializer.Deserialize<OrderTemplateCollection>(json, options);
            if (collection == null || collection.Version != 1)
            {
                _logger?.LogWarning("Unsupported templates version: {Version}", collection?.Version ?? 0);
                _templates = new List<OrderTemplate>();
                return _templates;
            }

            lock (_lock)
            {
                _templates = collection.Templates ?? new List<OrderTemplate>();
            }

            _logger?.LogInformation("Loaded {Count} templates", _templates.Count);
            return _templates;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to load templates");
            _templates = new List<OrderTemplate>();
            return _templates;
        }
    }

    /// <summary>
    /// Save templates to file
    /// </summary>
    public async Task SaveTemplatesAsync()
    {
        try
        {
            // Ensure directory exists
            var directory = Path.GetDirectoryName(_templatesFilePath);
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var collection = new OrderTemplateCollection
            {
                Version = 1,
                Templates = _templates
            };

            var options = new JsonSerializerOptions
            {
                WriteIndented = true,
                PropertyNamingPolicy = JsonNamingPolicy.CamelCase
            };

            var json = JsonSerializer.Serialize(collection, options);

            // Atomic write: write to temp file, then move
            var tempFile = _templatesFilePath + ".tmp";
            await File.WriteAllTextAsync(tempFile, json);
            File.Move(tempFile, _templatesFilePath, overwrite: true);

            _logger?.LogInformation("Saved {Count} templates", _templates.Count);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to save templates");
            throw;
        }
    }

    /// <summary>
    /// Add new template
    /// </summary>
    public async Task<OrderTemplate> AddTemplateAsync(OrderTemplate template)
    {
        try
        {
            lock (_lock)
            {
                _templates.Add(template);
            }

            await SaveTemplatesAsync();
            _logger?.LogInformation("Added template: {Name}", template.Name);
            return template;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to add template");
            throw;
        }
    }

    /// <summary>
    /// Update existing template
    /// </summary>
    public async Task UpdateTemplateAsync(OrderTemplate template)
    {
        try
        {
            lock (_lock)
            {
                var existing = _templates.FirstOrDefault(t => t.Id == template.Id);
                if (existing == null)
                {
                    _logger?.LogWarning("Template not found for update: {Id}", template.Id);
                    return;
                }

                var index = _templates.IndexOf(existing);
                _templates[index] = template;
            }

            await SaveTemplatesAsync();
            _logger?.LogInformation("Updated template: {Name}", template.Name);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to update template");
            throw;
        }
    }

    /// <summary>
    /// Delete template
    /// </summary>
    public async Task DeleteTemplateAsync(Guid templateId)
    {
        try
        {
            lock (_lock)
            {
                var existing = _templates.FirstOrDefault(t => t.Id == templateId);
                if (existing == null)
                {
                    _logger?.LogWarning("Template not found for deletion: {Id}", templateId);
                    return;
                }

                _templates.Remove(existing);
            }

            await SaveTemplatesAsync();
            _logger?.LogInformation("Deleted template: {Id}", templateId);
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to delete template");
            throw;
        }
    }

    /// <summary>
    /// Create order from template (increments UseCount)
    /// </summary>
    public async Task<OrderItem> CreateOrderFromTemplateAsync(Guid templateId)
    {
        try
        {
            OrderTemplate? template;
            lock (_lock)
            {
                template = _templates.FirstOrDefault(t => t.Id == templateId);
            }

            if (template == null)
            {
                _logger?.LogWarning("Template not found: {Id}", templateId);
                throw new InvalidOperationException($"Template not found: {templateId}");
            }

            // Increment use count
            template.UseCount++;
            await SaveTemplatesAsync();

            // Create order from template
            var order = template.CreateOrder();
            _logger?.LogInformation("Created order from template: {Name}", template.Name);
            return order;
        }
        catch (Exception ex)
        {
            _logger?.LogError(ex, "Failed to create order from template");
            throw;
        }
    }

    /// <summary>
    /// Get top N templates by use count
    /// </summary>
    public List<OrderTemplate> GetTopTemplates(int count)
    {
        lock (_lock)
        {
            return _templates
                .OrderByDescending(t => t.UseCount)
                .ThenBy(t => t.Name)
                .Take(count)
                .ToList();
        }
    }

    /// <summary>
    /// Get all templates sorted by criteria
    /// </summary>
    public List<OrderTemplate> GetTemplatesSorted(TemplateSortBy sortBy)
    {
        lock (_lock)
        {
            return sortBy switch
            {
                TemplateSortBy.Name => _templates.OrderBy(t => t.Name).ToList(),
                TemplateSortBy.UseCount => _templates.OrderByDescending(t => t.UseCount).ThenBy(t => t.Name).ToList(),
                TemplateSortBy.DateCreated => _templates.OrderByDescending(t => t.CreatedAt).ToList(),
                _ => _templates.ToList()
            };
        }
    }
}

/// <summary>
/// Template sorting criteria
/// </summary>
public enum TemplateSortBy
{
    Name,
    UseCount,
    DateCreated
}
