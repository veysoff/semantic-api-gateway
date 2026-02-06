var builder = DistributedApplication.CreateBuilder(args);

// Register OrderService mock microservice
var orderService = builder
    .AddProject("order-service", @"..\SemanticApiGateway.MockServices\OrderService\OrderService.csproj")
    .WithHttpEndpoint(port: 5100);

// Register InventoryService mock microservice
var inventoryService = builder
    .AddProject("inventory-service", @"..\SemanticApiGateway.MockServices\InventoryService\InventoryService.csproj")
    .WithHttpEndpoint(port: 5200);

// Register UserService mock microservice
var userService = builder
    .AddProject("user-service", @"..\SemanticApiGateway.MockServices\UserService\UserService.csproj")
    .WithHttpEndpoint(port: 5300);

// Register the semantic API gateway with service discovery environment variables
builder
    .AddProject("gateway", @"..\SemanticApiGateway.Gateway\SemanticApiGateway.Gateway.csproj")
    .WithHttpEndpoint(port: 5000)
    .WithEnvironment("ORDER_SERVICE_URL", () => orderService.GetEndpoint("http").Url)
    .WithEnvironment("INVENTORY_SERVICE_URL", () => inventoryService.GetEndpoint("http").Url)
    .WithEnvironment("USER_SERVICE_URL", () => userService.GetEndpoint("http").Url);

// Build and run
var app = builder.Build();

await app.RunAsync();
