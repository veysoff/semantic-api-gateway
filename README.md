# Semantic API Gateway - Enterprise-Grade AI-Native Proxy

**An AI-powered microservice orchestration platform that translates natural language into orchestrated API calls**

---

## 🎯 Mission Statement

The Semantic API Gateway is a production-ready system that combines:
- **Natural Language Understanding** via Microsoft Semantic Kernel
- **Intelligent Microservice Orchestration** with stepwise function planning
- **Enterprise Security** with JWT propagation and semantic guardrails
- **Observability-First Architecture** integrated with .NET Aspire
- **Resilience Patterns** using Polly v8 and circuit breakers

Users speak their intent in natural language; the Gateway translates it into orchestrated calls across distributed microservices, pipes data between them, and returns aggregated results—all with full traceability.

---

## 🏗️ Solution Architecture Overview

```
Client (Natural Language)
  ↓
Gateway.Proxy
  ├─ Extract intent & authenticate
  ├─ Run guardrail validation
  ├─ Load semantic plugins
  └─ Invoke Stepwise Planner
      ↓
ReasoningEngine
  ├─ Plan execution steps
  ├─ Orchestrate service calls
  └─ Pipe data between steps
      ↓
Microservices (Order, Inventory, User, etc.)
  ├─ Receive JWT-propagated requests
  ├─ Return results
  └─ Tracked end-to-end in Aspire
      ↓
Aggregated Result
  └─ Full execution trace with metrics
```

### Key Components
1. **Dynamic Plugin System**: Fetches OpenAPI/Swagger specs from services, converts to Semantic Kernel plugins, hot-swaps without restart
2. **Reasoning Engine**: Stepwise planner orchestrates multi-step function calls with data piping
3. **Security Layer**: JWT propagation, prompt injection detection, rate limiting, RBAC
4. **Observability**: OpenTelemetry integration with Aspire Dashboard tracing
5. **Resilience**: Polly v8 pipelines (circuit breaker, retry, timeout)
6. **Minimal APIs**: High-performance .NET 10 endpoints organized by service feature

---

## 🔒 Security Architecture

### Token Propagation Flow
```
Client JWT
  ↓
Gateway (validates JWT via RequireAuthorization)
  ↓
IntentEndpoints.ExecuteIntent (extracts user ID from claims)
  ↓
ReasoningEngine (holds JWT in context)
  ↓
TokenPropagationHandler (injects into HttpRequestMessage)
  ↓
Downstream Service Endpoints (receives request with Authorization header)
```

### Guardrail Layers
1. **Prompt Injection Detection**: Regex patterns for common attacks
2. **Role-Based Access Control**: Intent validation against user role
3. **Rate Limiting**: Per-user throttling (100 requests/hour default)
4. **Sensitive Operation Detection**: Flag delete/admin operations
5. **Function Blacklisting**: Prevent invocation of restricted APIs

### Implementation
- **TokenPropagationService**: Extracts JWT from HttpContext, propagates to downstream services via DelegatingHandler
- **SemanticGuardrailService**: Validates intents, detects injection attempts, enforces RBAC
- **Audit Logging**: All operations logged with user, intent, result, timestamp

---

## 📁 Future Project Structure

```
semantic-api-gateway/
│
├── SemanticApiGateway.AppHost/                    # .NET Aspire Host & Orchestration
│   ├── Program.cs
│   ├── SemanticApiGateway.AppHost.csproj
│   └── docker-compose.yml (optional)
│
├── SemanticApiGateway.ServiceDefaults/            # Shared Configuration & Extensions
│   ├── Extensions/
│   │   └── DefaultServiceCollectionExtensions.cs
│   ├── ServiceDefaults.cs
│   └── SemanticApiGateway.ServiceDefaults.csproj
│
├── SemanticApiGateway.Gateway/                    # Main Gateway Application
│   ├── Program.cs                                 # DI setup, configuration
│   ├── appsettings.json
│   ├── appsettings.Development.json
│   │
│   ├── Endpoints/
│   │   └── IntentEndpoints.cs                    # POST /api/intent endpoints (execute, plan)
│   │
│   ├── Models/
│   │   └── IntentDtos.cs                         # DTOs (ExecuteIntentRequest, ExecuteIntentResponse, etc.)
│   │
│   ├── Features/
│   │   ├── PluginOrchestration/
│   │   │   ├── IPluginRegistry.cs
│   │   │   ├── PluginRegistry.cs
│   │   │   ├── OpenApiPluginLoader.cs             # Swagger→Plugin conversion
│   │   │   ├── PluginRefreshService.cs            # Background refresh loop
│   │   │   └── Models/
│   │   │       ├── PluginMetadata.cs
│   │   │       └── ServiceEndpointInfo.cs
│   │   │
│   │   ├── Reasoning/
│   │   │   ├── IReasoningEngine.cs
│   │   │   ├── StepwisePlannerEngine.cs           # Function calling orchestrator
│   │   │   ├── ExecutionPlan.cs
│   │   │   └── ExecutionStep.cs
│   │   │
│   │   ├── Security/
│   │   │   ├── ITokenPropagationService.cs
│   │   │   ├── TokenPropagationService.cs
│   │   │   ├── TokenPropagationHandler.cs
│   │   │   ├── ISemanticGuardrailService.cs
│   │   │   ├── SemanticGuardrailService.cs
│   │   │   └── Models/
│   │   │       └── GuardrailContext.cs
│   │   │
│   │   └── Observability/
│   │       ├── GatewayActivitySource.cs           # Custom ActivitySource
│   │       ├── DiagnosticsExtensions.cs
│   │       └── Models/
│   │           └── ActivitySpanContext.cs
│   │
│   ├── Middleware/
│   │   ├── ErrorHandlingMiddleware.cs
│   │   └── RequestLoggingMiddleware.cs
│   │
│   ├── Configuration/
│   │   ├── GatewayOptions.cs
│   │   ├── SemanticKernelOptions.cs
│   │   └── YarpOptions.cs
│   │
│   └── SemanticApiGateway.Gateway.csproj
│
├── SemanticApiGateway.MockServices/               # Reference Mock Services
│   ├── OrderService/
│   │   ├── Program.cs
│   │   ├── Endpoints/
│   │   │   └── OrderEndpoints.cs                 # GET, POST, PUT, DELETE /api/orders endpoints
│   │   ├── Models/
│   │   │   └── Order.cs                          # Order model + CreateOrderRequest DTO
│   │   └── OrderService.csproj
│   │
│   ├── InventoryService/
│   │   ├── Program.cs
│   │   ├── Endpoints/
│   │   │   └── InventoryEndpoints.cs             # GET, POST, PUT /api/inventory endpoints
│   │   ├── Models/
│   │   │   └── InventoryItem.cs                  # InventoryItem + Request DTOs
│   │   └── InventoryService.csproj
│   │
│   └── UserService/
│       ├── Program.cs
│       ├── Endpoints/
│       │   └── UserEndpoints.cs                  # GET, POST, PUT, DELETE /api/users endpoints
│       ├── Models/
│       │   └── User.cs                           # User model + Request DTOs
│       └── UserService.csproj
│
├── SemanticApiGateway.Tests/                      # Unit & Integration Tests
│   ├── Integration/
│   │   ├── GatewayIntegrationTests.cs
│   │   ├── ReasoningEngineTests.cs
│   │   └── SecurityTests.cs
│   │
│   ├── Unit/
│   │   ├── PluginLoaderTests.cs
│   │   ├── GuardrailTests.cs
│   │   ├── TokenPropagationTests.cs
│   │   └── ReasoningEngineTests.cs
│   │
│   ├── Fixtures/
│   │   ├── MockHttpClientFactory.cs
│   │   └── TestAuthHelper.cs
│   │
│   └── SemanticApiGateway.Tests.csproj
│
├── Documentation/                                  # Additional docs folder
│
├── README.md                                      # This file
├── .gitignore
├── LICENSE
└── SemanticApiGateway.sln
```

---

## 📦 Technology Stack

| Component | Technology | Version |
|-----------|-----------|---------|
| **Host** | .NET Aspire | 8.0+ |
| **Gateway** | ASP.NET Core | 10.0+ |
| **AI Orchestration** | Semantic Kernel | 1.18+ |
| **Reverse Proxy** | YARP | 2.0+ |
| **Resilience** | Polly | 8.0+ |
| **OpenAPI** | Microsoft.OpenApi | 1.6+ |
| **Observability** | OpenTelemetry | 1.7+ |
| **Authentication** | JWT Bearer | .NET 10 |

---

## 📄 License

MIT License - See LICENSE file

---

**Version**: 1.0
**Status**: Ready for Implementation
**Last Updated**: 2026-01-29
