using System.Security.Claims;
using OrderService.Models;
using Microsoft.AspNetCore.Mvc;

namespace OrderService.Endpoints;

public static class OrderEndpoints
{
    private static readonly List<Order> Orders = new();

    public static IEndpointRouteBuilder MapOrderEndpoints(this IEndpointRouteBuilder endpoints)
    {
        SeedOrders();

        var group = endpoints.MapGroup(prefix: "/api/orders")
            .RequireAuthorization()
            .WithTags(tags: "Orders");

        group.MapGet(pattern: "/{id}", handler: GetOrder)
            .WithName(endpointName: "GetOrder")
            .Produces<Order>(statusCode: 200)
            .Produces(statusCode: 404)
            .Produces(statusCode: 403);

        group.MapGet(pattern: "/user/{userId}", handler: GetUserOrders)
            .WithName(endpointName: "GetUserOrders")
            .Produces<List<Order>>(statusCode: 200)
            .Produces(statusCode: 403);

        group.MapPost(pattern: "", handler: CreateOrder)
            .WithName(endpointName: "CreateOrder")
            .Produces<Order>(statusCode: 201)
            .Produces<ProblemDetails>(statusCode: 400)
            .Produces(statusCode: 403);

        group.MapPut(pattern: "/{id}", handler: UpdateOrder)
            .WithName(endpointName: "UpdateOrder")
            .Produces<Order>(statusCode: 200)
            .Produces(statusCode: 404)
            .Produces(statusCode: 403);

        group.MapDelete(pattern: "/{id}", handler: DeleteOrder)
            .WithName(endpointName: "DeleteOrder")
            .Produces(statusCode: 204)
            .Produces(statusCode: 404)
            .Produces(statusCode: 403);

        return endpoints;
    }

    private static IResult GetOrder(int id, ClaimsPrincipal user, ILogger<Program> logger)
    {
        var order = Orders.FirstOrDefault(o => o.Id == id);
        if (order == null)
            return Results.NotFound();

        // SECURITY: Verify user owns this order
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanAccessOrder(userId, order.UserId))
        {
            logger.LogWarning("Unauthorized access attempt to order {OrderId} by user {UserId}", id, userId);
            return Results.Forbid();
        }

        return Results.Ok(order);
    }

    private static IResult GetUserOrders(string userId, ClaimsPrincipal user, ILogger<Program> logger)
    {
        // SECURITY: User can only view their own orders
        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanAccessUserOrders(currentUserId, userId))
        {
            logger.LogWarning("Unauthorized access attempt to orders for user {RequestedUserId} by user {CurrentUserId}", userId, currentUserId);
            return Results.Forbid();
        }

        var orders = Orders.Where(o => o.UserId == userId).ToList();
        return Results.Ok(orders);
    }

    private static IResult CreateOrder(
        CreateOrderRequest request,
        ClaimsPrincipal user,
        ILogger<Program> logger)
    {
        // SECURITY: Validate request data
        if (string.IsNullOrWhiteSpace(request.UserId) || !request.Items.Any())
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "UserId and Items are required",
                Status = 400
            });

        // SECURITY: Verify user can only create orders for themselves
        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanCreateOrderForUser(currentUserId, request.UserId))
        {
            logger.LogWarning("Unauthorized order creation attempt by user {CurrentUserId} for user {RequestedUserId}", currentUserId, request.UserId);
            return Results.Forbid();
        }

        // SECURITY: Validate order amounts are positive
        if (request.Items.Any(i => i.Quantity <= 0 || i.UnitPrice <= 0))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Quantity and UnitPrice must be positive",
                Status = 400
            });

        var order = new Order
        {
            Id = Orders.Count + 1,
            OrderNumber = $"ORD-{Orders.Count + 1:D3}",
            UserId = request.UserId,
            CreatedAt = DateTime.UtcNow,
            TotalAmount = request.Items.Sum(i => i.Quantity * i.UnitPrice),
            Status = "Pending",
            Items = request.Items.Select((item, idx) => new OrderItem
            {
                Id = idx + 1,
                ProductId = item.ProductId,
                Quantity = item.Quantity,
                UnitPrice = item.UnitPrice
            }).ToList()
        };

        Orders.Add(order);

        // SECURITY: Audit log
        logger.LogInformation("Order created by user {UserId}: {OrderNumber}, Amount: {Amount}", currentUserId, order.OrderNumber, order.TotalAmount);

        return Results.CreatedAtRoute("GetOrder", new { id = order.Id }, order);
    }

    private static IResult UpdateOrder(
        int id,
        Order updated,
        ClaimsPrincipal user,
        ILogger<Program> logger)
    {
        var order = Orders.FirstOrDefault(o => o.Id == id);
        if (order == null)
            return Results.NotFound();

        // SECURITY: Verify user owns this order
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanAccessOrder(userId, order.UserId))
        {
            logger.LogWarning("Unauthorized update attempt to order {OrderId} by user {UserId}", id, userId);
            return Results.Forbid();
        }

        order.Status = updated.Status;
        order.TotalAmount = updated.TotalAmount;

        // SECURITY: Audit log
        logger.LogInformation("Order updated by user {UserId} - OrderId: {OrderId}, NewStatus: {Status}", userId, id, order.Status);
        return Results.Ok(order);
    }

    private static IResult DeleteOrder(int id, ClaimsPrincipal user, ILogger<Program> logger)
    {
        var order = Orders.FirstOrDefault(o => o.Id == id);
        if (order == null)
            return Results.NotFound();

        // SECURITY: Verify user owns this order
        var userId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanAccessOrder(userId, order.UserId))
        {
            logger.LogWarning("Unauthorized delete attempt to order {OrderId} by user {UserId}", id, userId);
            return Results.Forbid();
        }

        Orders.Remove(order);

        // SECURITY: Audit log
        logger.LogInformation("Order deleted by user {UserId}: {OrderId}", userId, id);

        return Results.NoContent();
    }

    // SECURITY: Helper methods for authorization
    private static bool CanAccessOrder(string? currentUserId, string orderUserId)
    {
        // Users can only access their own orders
        return currentUserId == orderUserId;
    }

    private static bool CanAccessUserOrders(string? currentUserId, string requestedUserId)
    {
        // Users can only view their own orders
        return currentUserId == requestedUserId;
    }

    private static bool CanCreateOrderForUser(string? currentUserId, string targetUserId)
    {
        // Users can only create orders for themselves
        return currentUserId == targetUserId;
    }

    private static void SeedOrders()
    {
        if (Orders.Count == 0)
        {
            Orders.AddRange(new[]
            {
                new Order
                {
                    Id = 1,
                    OrderNumber = "ORD-001",
                    UserId = "user1",
                    CreatedAt = DateTime.UtcNow.AddDays(-1),
                    TotalAmount = 150.00m,
                    Status = "Completed",
                    Items = new List<OrderItem>
                    {
                        new() { Id = 1, ProductId = "PROD-001", Quantity = 2, UnitPrice = 75.00m }
                    }
                },
                new Order
                {
                    Id = 2,
                    OrderNumber = "ORD-002",
                    UserId = "user2",
                    CreatedAt = DateTime.UtcNow,
                    TotalAmount = 200.00m,
                    Status = "Pending",
                    Items = new List<OrderItem>
                    {
                        new() { Id = 1, ProductId = "PROD-002", Quantity = 1, UnitPrice = 200.00m }
                    }
                }
            });
        }
    }
}
