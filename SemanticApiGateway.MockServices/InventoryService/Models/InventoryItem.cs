namespace InventoryService.Models;

public class InventoryItem
{
    public string ProductId { get; set; } = string.Empty;
    public string ProductName { get; set; } = string.Empty;
    public int QuantityAvailable { get; set; }
    public int QuantityReserved { get; set; }
    public int ReorderLevel { get; set; }
    public decimal UnitPrice { get; set; }
    public string Status { get; set; } = "Active";
}

public class UpdateInventoryRequest
{
    public int QuantityChange { get; set; }
    public string Reason { get; set; } = string.Empty;
}

public class ReserveInventoryRequest
{
    public string ProductId { get; set; } = string.Empty;
    public int Quantity { get; set; }
    public string OrderId { get; set; } = string.Empty;
}
