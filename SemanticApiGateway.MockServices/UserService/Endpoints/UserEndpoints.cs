using System.Security.Claims;
using UserService.Models;
using Microsoft.AspNetCore.Mvc;

namespace UserService.Endpoints;

public static class UserEndpoints
{
    private static readonly List<User> Users = new();

    public static IEndpointRouteBuilder MapUserEndpoints(this IEndpointRouteBuilder endpoints)
    {
        SeedUsers();

        var group = endpoints.MapGroup(prefix: "/api/users")
            .WithTags(tags: "Users");

        group.MapGet(pattern: "/{id}", handler: GetUser)
            .RequireAuthorization()
            .WithName(endpointName: "GetUser")
            .Produces<User>(statusCode: 200)
            .Produces(statusCode: 404)
            .Produces(statusCode: 403);

        group.MapGet(pattern: "", handler: GetAllUsers)
            .RequireAuthorization(configurePolicy: policy => policy.RequireRole(roles: "Admin"))
            .WithName(endpointName: "GetAllUsers")
            .Produces<List<User>>(statusCode: 200);

        group.MapGet(pattern: "/email/{email}", handler: GetUserByEmail)
            .RequireAuthorization(configurePolicy: policy => policy.RequireRole(roles: "Admin"))
            .WithName(endpointName: "GetUserByEmail")
            .Produces<User>(statusCode: 200)
            .Produces(statusCode: 404);

        group.MapPost(pattern: "", handler: CreateUser)
            .RequireAuthorization(configurePolicy: policy => policy.RequireRole(roles: "Admin"))
            .WithName(endpointName: "CreateUser")
            .Produces<User>(statusCode: 201)
            .Produces<ProblemDetails>(statusCode: 400);

        group.MapPut(pattern: "/{id}", handler: UpdateUser)
            .RequireAuthorization()
            .WithName(endpointName: "UpdateUser")
            .Produces<User>(statusCode: 200)
            .Produces(statusCode: 404)
            .Produces(statusCode: 403);

        group.MapDelete(pattern: "/{id}", handler: DeleteUser)
            .RequireAuthorization(configurePolicy: policy => policy.RequireRole(roles: "Admin"))
            .WithName(endpointName: "DeleteUser")
            .Produces(statusCode: 204)
            .Produces(statusCode: 404);

        group.MapPost(pattern: "/{id}/login", handler: LoginUser)
            .AllowAnonymous()
            .WithName(endpointName: "LoginUser")
            .Produces(statusCode: 200)
            .Produces(statusCode: 404)
            .Produces<ProblemDetails>(statusCode: 400);

        group.MapGet(pattern: "/{id}/profile", handler: GetUserProfile)
            .RequireAuthorization()
            .WithName(endpointName: "GetUserProfile")
            .Produces(statusCode: 200)
            .Produces(statusCode: 404)
            .Produces(statusCode: 403);

        return endpoints;
    }

    private static IResult GetUser(string id, ClaimsPrincipal user, ILogger<Program> logger)
    {
        var userData = Users.FirstOrDefault(u => u.Id == id);
        if (userData == null)
            return Results.NotFound();

        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanAccessUser(currentUserId, id))
        {
            logger.LogWarning("Unauthorized access attempt to user {UserId} by user {CurrentUserId}", id, currentUserId);
            return Results.Forbid();
        }

        return Results.Ok(userData);
    }

    private static IResult GetAllUsers(ILogger<Program> logger)
    {
        return Results.Ok(Users);
    }

    private static IResult GetUserByEmail(string email, ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(email) || !email.Contains("@"))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Invalid email format",
                Status = 400
            });

        var userData = Users.FirstOrDefault(u => u.Email == email);
        if (userData == null)
            return Results.NotFound();

        return Results.Ok(userData);
    }

    private static IResult CreateUser(CreateUserRequest request, ClaimsPrincipal user, ILogger<Program> logger)
    {
        if (string.IsNullOrWhiteSpace(request.Email) || string.IsNullOrWhiteSpace(request.FirstName))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Email and FirstName are required",
                Status = 400
            });

        if (!request.Email.Contains("@"))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Invalid email format",
                Status = 400
            });

        if (Users.Any(u => u.Email == request.Email))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "User with this email already exists",
                Status = 400
            });

        var userData = new User
        {
            Id = $"user{Users.Count + 1}",
            Email = request.Email,
            FirstName = request.FirstName,
            LastName = request.LastName,
            Role = request.Role ?? "User",
            IsActive = true,
            CreatedAt = DateTime.UtcNow
        };

        Users.Add(userData);

        var adminId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        logger.LogInformation("User created by admin {AdminId}: {UserId}, Email: {Email}", adminId, userData.Id, userData.Email);

        return Results.CreatedAtRoute("GetUser", new { id = userData.Id }, userData);
    }

    private static IResult UpdateUser(
        string id,
        UpdateUserRequest request,
        ClaimsPrincipal user,
        ILogger<Program> logger)
    {
        var userData = Users.FirstOrDefault(u => u.Id == id);
        if (userData == null)
            return Results.NotFound();

        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        var isAdmin = user.FindFirst(ClaimTypes.Role)?.Value == "Admin";

        if (!isAdmin && !CanAccessUser(currentUserId, id))
        {
            logger.LogWarning("Unauthorized update attempt to user {UserId} by user {CurrentUserId}", id, currentUserId);
            return Results.Forbid();
        }

        if (!isAdmin && !string.IsNullOrEmpty(request.Role))
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Validation Error",
                Detail = "Users cannot change their own role",
                Status = 400
            });

        if (!string.IsNullOrEmpty(request.FirstName))
            userData.FirstName = request.FirstName;

        if (!string.IsNullOrEmpty(request.LastName))
            userData.LastName = request.LastName;

        if (!string.IsNullOrEmpty(request.Role) && isAdmin)
            userData.Role = request.Role;

        if (request.IsActive.HasValue && isAdmin)
            userData.IsActive = request.IsActive.Value;

        logger.LogInformation("User updated by {UpdatedBy} - UserId: {UserId}, Fields: FirstName={FirstName}, LastName={LastName}", currentUserId, id, request.FirstName, request.LastName);

        return Results.Ok(userData);
    }

    private static IResult DeleteUser(string id, ClaimsPrincipal user, ILogger<Program> logger)
    {
        var userData = Users.FirstOrDefault(u => u.Id == id);
        if (userData == null)
            return Results.NotFound();

        Users.Remove(userData);

        var adminId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        logger.LogInformation("User deleted by admin {AdminId}: {UserId}, Email: {Email}", adminId, userData.Id, userData.Email);

        return Results.NoContent();
    }

    private static IResult LoginUser(string id, ILogger<Program> logger)
    {
        var userData = Users.FirstOrDefault(u => u.Id == id);
        if (userData == null)
            return Results.NotFound();

        if (!userData.IsActive)
            return Results.BadRequest(new ProblemDetails
            {
                Title = "Login Failed",
                Detail = "User is not active",
                Status = 400
            });

        userData.LastLoginAt = DateTime.UtcNow;

        logger.LogInformation("User logged in: {UserId}", id);

        return Results.Ok(new { message = "Login successful", user = userData });
    }

    private static IResult GetUserProfile(string id, ClaimsPrincipal user, ILogger<Program> logger)
    {
        var userData = Users.FirstOrDefault(u => u.Id == id);
        if (userData == null)
            return Results.NotFound();

        var currentUserId = user.FindFirst(ClaimTypes.NameIdentifier)?.Value;
        if (!CanAccessUser(currentUserId, id))
        {
            logger.LogWarning("Unauthorized profile access attempt for user {UserId} by user {CurrentUserId}", id, currentUserId);
            return Results.Forbid();
        }

        return Results.Ok(new
        {
            userData.Id,
            userData.Email,
            userData.FirstName,
            userData.LastName,
            userData.Role,
            userData.IsActive,
            userData.CreatedAt,
            userData.LastLoginAt
        });
    }

    private static bool CanAccessUser(string? currentUserId, string requestedUserId)
    {
        return currentUserId == requestedUserId;
    }

    private static void SeedUsers()
    {
        if (Users.Count == 0)
        {
            Users.AddRange(new[]
            {
                new User
                {
                    Id = "user1",
                    Email = "john@example.com",
                    FirstName = "John",
                    LastName = "Doe",
                    Role = "User",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-30),
                    LastLoginAt = DateTime.UtcNow.AddHours(-2)
                },
                new User
                {
                    Id = "user2",
                    Email = "jane@example.com",
                    FirstName = "Jane",
                    LastName = "Smith",
                    Role = "Admin",
                    IsActive = true,
                    CreatedAt = DateTime.UtcNow.AddDays(-60),
                    LastLoginAt = DateTime.UtcNow.AddHours(-1)
                },
                new User
                {
                    Id = "user3",
                    Email = "bob@example.com",
                    FirstName = "Bob",
                    LastName = "Johnson",
                    Role = "User",
                    IsActive = false,
                    CreatedAt = DateTime.UtcNow.AddDays(-90),
                    LastLoginAt = null
                }
            });
        }
    }
}
