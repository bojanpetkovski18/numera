namespace Numera.Application.Entities;

public enum OrderStatus
{
    Completed,
    Refunded,
    Cancelled,
}

/// <summary>One order line. An order is the set of lines sharing an <see cref="OrderId"/>.</summary>
public class Order
{
    public long Id { get; set; }
    public required string OrderId { get; init; }
    public required DateOnly OrderDate { get; init; }
    public required string CustomerId { get; init; }
    public required string Region { get; init; }
    public required string Channel { get; init; }
    public required string ProductSku { get; init; }
    public required string ProductCategory { get; init; }
    public required int Quantity { get; init; }
    public required decimal UnitPrice { get; init; }
    public required decimal Discount { get; init; }
    public required OrderStatus Status { get; init; }
    public Guid? ImportJobId { get; init; }
}
