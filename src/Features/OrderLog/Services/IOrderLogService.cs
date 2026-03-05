using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using OrderLog.Features.Models;

namespace OrderLog.Features.Services;

public interface IOrderLogService : IDisposable
{
    Task<List<OrderItem>> LoadAsync();
    Task SaveAsync(List<OrderItem> items);
}
