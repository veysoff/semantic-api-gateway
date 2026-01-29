using InventoryService.Models;
using Microsoft.AspNetCore.Http.HttpResults;
using Microsoft.AspNetCore.Mvc;

namespace InventoryService.Endpoints;

public static class InventoryEndpoints
{
    private static readonly List<InventoryItem> Items = new();

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        SeedInventory();

        var group = endpoints.MapGroup("/api/inventory")
            .RequireAuthorization()
            .WithTags("Inventory");

        group.MapGet("/{productId}", GetInventory)
            .WithName("GetInventory")
            .Produces<InventoryItem>(200)
            .Produces(404);

        group.MapGet("", GetAllInventory)
            .WithName("GetAllInventory")
            .Produces<List<InventoryItem>>(200);

        group.MapPost("/{productId}/reserve", ReserveInventory)
            .WithName("ReserveInventory")
            .Produces(200)
            .Produces<ProblemDetails>(400)
            .Produces(404);

        group.MapPost("/{productId}/release", ReleaseInventory)
            .WithName("ReleaseInventory")
            .Produces<InventoryItem>(200)
            .Produces<ProblemDetails>(400)
            .Produces(404);

        group.MapPut("/{productId}", UpdateInventory)
            .WithName("UpdateInventory")
            .Produces<InventoryItem>(200)
            .Produces(404);

        group.MapGet("/check-stock/{productId}/{quantity}", CheckStock)
            .WithName("CheckStock")
            .Produces(200)
            .Produces(404);

        return endpoints;
    }

    private static IResult GetInventory(string productId, ILogger<Program> logger)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null)
            return Results.NotFound();

        return Results.Ok(item);
    }

    private static IResult GetAllInventory(ILogger<Program> logger)
    {
        return Results.Ok(Items);
    }

    private static IResult ReserveInventory(
        string productId,
        ReserveInventoryRequest request,
        ILogger<Program> logger)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null)
            return Results.NotFound();

        if (item.QuantityAvailable < request.Quantity)
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Insufficient Inventory",
                Detail = "Insufficient inventory",
                Status = 400
            });

        item.QuantityAvailable -= request.Quantity;
        item.QuantityReserved += request.Quantity;

        // SECURITY: Audit log of sensitive operation
        logger.LogInformation("Inventory reserved - Product: {ProductId}, Quantity: {Quantity}, OrderId: {OrderId}",
            productId, request.Quantity, request.OrderId);

        return Results.Ok(new { message = "Inventory reserved", item });
    }

    private static IResult ReleaseInventory(
        string productId,
        UpdateInventoryRequest request,
        ILogger<Program> logger)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null)
            return Results.NotFound();

        if (item.QuantityReserved < Math.Abs(request.QuantityChange))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Invalid Release",
                Detail = "Cannot release more than reserved",
                Status = 400
            });

        item.QuantityAvailable += request.QuantityChange;
        item.QuantityReserved -= Math.Abs(request.QuantityChange);

        // SECURITY: Audit log of sensitive operation
        logger.LogInformation("Inventory released - Product: {ProductId}, Quantity: {Quantity}, Reason: {Reason}",
            productId, request.QuantityChange, request.Reason);

        return Results.Ok(item);
    }

    private static IResult UpdateInventory(
        string productId,
        UpdateInventoryRequest request,
        ILogger<Program> logger)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null)
            return Results.NotFound();

        item.QuantityAvailable += request.QuantityChange;

        // SECURITY: Audit log of sensitive operation
        logger.LogInformation("Inventory updated - Product: {ProductId}, Change: {Change}, Reason: {Reason}",
            productId, request.QuantityChange, request.Reason);

        return Results.Ok(item);
    }

    private static IResult CheckStock(string productId, int quantity, ILogger<Program> logger)
    {
        var item = Items.FirstOrDefault(i => i.ProductId == productId);
        if (item == null)
            return Results.NotFound();

        var available = item.QuantityAvailable >= quantity;
        return Results.Ok(new
        {
            productId,
            requestedQuantity = quantity,
            availableQuantity = item.QuantityAvailable,
            inStock = available
        });
    }

    private static void SeedInventory()
    {
        if (Items.Count == 0)
        {
            Items.AddRange(new[]
            {
                new InventoryItem
                {
                    ProductId = "PROD-001",
                    ProductName = "Widget A",
                    QuantityAvailable = 100,
                    QuantityReserved = 20,
                    ReorderLevel = 10,
                    UnitPrice = 75.00m,
                    Status = "Active"
                },
                new InventoryItem
                {
                    ProductId = "PROD-002",
                    ProductName = "Widget B",
                    QuantityAvailable = 50,
                    QuantityReserved = 5,
                    ReorderLevel = 10,
                    UnitPrice = 200.00m,
                    Status = "Active"
                },
                new InventoryItem
                {
                    ProductId = "PROD-003",
                    ProductName = "Widget C",
                    QuantityAvailable = 0,
                    QuantityReserved = 0,
                    ReorderLevel = 20,
                    UnitPrice = 150.00m,
                    Status = "OutOfStock"
                }
            });
        }
    }
}
