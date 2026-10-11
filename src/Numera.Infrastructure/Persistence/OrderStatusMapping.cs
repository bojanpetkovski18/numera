using Numera.Application.Entities;

namespace Numera.Infrastructure.Persistence;

/// <summary>Maps <see cref="OrderStatus"/> to the lowercase strings used in the database and the CSV format.</summary>
public static class OrderStatusMapping
{
    public static string ToDatabase(OrderStatus status) => status switch
    {
        OrderStatus.Completed => "completed",
        OrderStatus.Refunded => "refunded",
        OrderStatus.Cancelled => "cancelled",
        _ => throw new ArgumentOutOfRangeException(nameof(status), status, null),
    };

    public static OrderStatus FromDatabase(string value) => value switch
    {
        "completed" => OrderStatus.Completed,
        "refunded" => OrderStatus.Refunded,
        "cancelled" => OrderStatus.Cancelled,
        _ => throw new ArgumentOutOfRangeException(nameof(value), value, null),
    };
}
