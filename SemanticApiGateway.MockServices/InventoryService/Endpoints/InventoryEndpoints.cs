using InventoryService.Models;
using Microsoft.AspNetCore.Mvc;

namespace InventoryService.Endpoints;

public static class InventoryEndpoints
{
    private static readonly List<InventoryItem> Items = new();

    public static IEndpointRouteBuilder MapInventoryEndpoints(this IEndpointRouteBuilder endpoints)
    {
        SeedInventory();

        var group = endpoints.MapGroup(prefix: "/api/inventory")
            .RequireAuthorization()
            .WithTags(tags: "Inventory");

        group.MapGet(pattern: "/{productId}", handler: GetInventory)
            .WithName(endpointName: "GetInventory")
            .Produces<InventoryItem>(statusCode: 200)
            .Produces(statusCode: 404);

        group.MapGet(pattern: "", handler: GetAllInventory)
            .WithName(endpointName: "GetAllInventory")
            .Produces<List<InventoryItem>>(statusCode: 200);

        group.MapPost(pattern: "/{productId}/reserve", handler: ReserveInventory)
            .WithName(endpointName: "ReserveInventory")
            .Produces(statusCode: 200)
            .Produces<ProblemDetails>(statusCode: 400)
            .Produces(statusCode: 404);

        group.MapPost(pattern: "/{productId}/release", handler: ReleaseInventory)
            .WithName(endpointName: "ReleaseInventory")
            .Produces<InventoryItem>(statusCode: 200)
            .Produces<ProblemDetails>(statusCode: 400)
            .Produces(statusCode: 404);

        group.MapPut(pattern: "/{productId}", handler: UpdateInventory)
            .WithName(endpointName: "UpdateInventory")
            .Produces<InventoryItem>(statusCode: 200)
            .Produces(statusCode: 404);

        group.MapGet(pattern: "/check-stock/{productId}/{quantity}", handler: CheckStock)
            .WithName(endpointName: "CheckStock")
            .Produces(statusCode: 200)
            .Produces(statusCode: 404);

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
