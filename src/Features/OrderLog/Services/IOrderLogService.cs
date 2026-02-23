using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using SOUP.Features.OrderLog.Models;

namespace SOUP.Features.OrderLog.Services;

public interface IOrderLogService : IDisposable
{
    Task<List<OrderItem>> LoadAsync();
    Task SaveAsync(List<OrderItem> items);
}
