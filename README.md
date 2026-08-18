# Fulfillment & Inventory Management Platform

The **Fulfillment & Inventory Management Platform** is an enterprise-grade Web API built with .NET 10 and C# 13 for managing categories, products, warehouses, multi-warehouse stock inventory, stock adjustments, audit history, and role-based access control.

---

## 1. Project Overview

This platform powers fulfillment logistics by enforcing core business domain rules:
- **Category Management**: Case-sensitive unique category names with soft deletion protections.
- **Warehouse Management**: Case-sensitive unique warehouse names, address registration, location metadata, and single active warehouse constraint.
- **Product Management**: Case-sensitive unique SKUs, price formatting (`decimal(18,2)`), mandatory initial warehouse association, and automatic stock zero-initialization.
- **Inventory Management**: Product stock per warehouse, atomic stock adjustments, conditional SQL concurrency control, non-negative quantity invariants (`Quantity >= 0`), and immutable audit adjustment history.
- **Warehouse Inventory Query**: Read-only product inventory views per warehouse (`GET /api/warehouses/{warehouseId}/inventory`).
- **Recent Inventory Changes**: Historical audit log queries (`GET /api/inventory/changes`) returning the latest 20 stock adjustments ordered descending by timestamp and ID.
- **Identity & Authorization**: ASP.NET Core Identity authentication with JWT Bearer tokens and 4 distinct role-based access levels (`Admin`, `Manager`, `Warehouse Operator`, `Sales Agent`).

---

## 2. Architecture

The solution adheres strictly to **Clean Architecture** principles, maintaining clear boundaries and unidirectional dependencies:

```
                  ┌────────────────────────┐
                  │    Fulfillment.Api     │
                  └───────────┬────────────┘
                              │
               ┌──────────────┴──────────────┐
               ▼                             ▼
┌─────────────────────────────┐   ┌─────────────────────────────┐
│ Fulfillment.Infrastructure  │──>│   Fulfillment.Application   │
└─────────────────────────────┘   └──────────────┬──────────────┘
                                                 │
                                                 ▼
                                  ┌─────────────────────────────┐
                                  │     Fulfillment.Domain      │
                                  └─────────────────────────────┘
```

- **`Fulfillment.Domain`**: Core enterprise domain entities (`Category`, `Product`, `Warehouse`, `Inventory`, `InventoryAdjustment`). Contains zero framework or database dependencies.
- **`Fulfillment.Application`**: Service contracts (`IInventoryService`, `IProductService`, etc.), DTO records, domain exceptions (`ValidationException`, `NotFoundException`, `ConflictException`, `ForbiddenException`). References only `Fulfillment.Domain`.
- **`Fulfillment.Infrastructure`**: Persistence implementation via Entity Framework Core 10, SQL Server `ApplicationDbContext`, Identity stores (`ApplicationUser`), repository implementations, and `JwtTokenGenerator`. Implements `Fulfillment.Application` abstractions.
- **`Fulfillment.Api`**: Presentation layer featuring ASP.NET Core Web API controllers, RFC 9457 `GlobalExceptionHandler`, policy-based authorization, and interactive Swagger UI documentation.

---

## 3. Technology Stack

- **Framework**: .NET 10.0 (`net10.0`) & C# 13
- **Web API**: ASP.NET Core Web API
- **ORM / Persistence**: Entity Framework Core 10 (`Microsoft.EntityFrameworkCore.SqlServer`)
- **Database Engine**: Microsoft SQL Server / LocalDB (`(localdb)\mssqllocaldb`)
- **Identity & Security**: ASP.NET Core Identity & JWT Bearer (`Microsoft.AspNetCore.Authentication.JwtBearer`)
- **API Documentation & UI**: Swashbuckle ASP.NET Core (`Swashbuckle.AspNetCore`) with Swagger UI
- **Testing**: xUnit, `Microsoft.AspNetCore.Mvc.Testing` (WebApplicationFactory), EF Core In-Memory / Test DB Context

---

## 4. Prerequisites

Before running the application, ensure your environment has:
- **.NET 10 SDK** (v10.0.100 or higher)
- **SQL Server** or **SQL Server Express LocalDB** installed and running
- **Git** (for source control)

---

## 5. Solution Structure

```
fulfillment-inventory-platform/
├── Fulfillment.slnx                  # Solution File
├── README.md                          # Root Documentation
├── src/
│   ├── Fulfillment.Domain/            # Domain Entities & Business Rules
│   ├── Fulfillment.Application/       # DTOs, Service Contracts & Exceptions
│   ├── Fulfillment.Infrastructure/    # DbContext, Repositories & Identity
│   └── Fulfillment.Api/               # Controllers, Middleware & Swagger UI
└── tests/
    ├── Fulfillment.UnitTests/         # Unit Tests for Application & Domain Logic
    └── Fulfillment.IntegrationTests/  # Integration Tests for Web API Endpoints
```

---

## 6. Database Configuration

The application uses Entity Framework Core with SQL Server. Connection strings are configured in `appsettings.json` or `appsettings.Development.json`:

```json
{
  "ConnectionStrings": {
    "DefaultConnection": "Server=(localdb)\\mssqllocaldb;Database=FulfillmentDb_Dev;Trusted_Connection=True;MultipleActiveResultSets=true"
  }
}
```

---

## 7. EF Core Migrations

The database schema is managed using Entity Framework Core Migrations. The baseline schema is defined in migration `20260818111956_InitialCreate`.

To apply pending migrations and create/update your local database:

```powershell
dotnet ef database update --project src/Fulfillment.Infrastructure --startup-project src/Fulfillment.Api
```

---

## 8. Running the API

To launch the Web API server locally:

```powershell
dotnet run --project src/Fulfillment.Api
```

The application will start and listen on the configured HTTP/HTTPS ports (e.g., `https://localhost:7001` or `http://localhost:5286`).

---

## 9. Running Tests

The test suite includes **205 automated tests** (74 Unit Tests and 131 Integration Tests):

```powershell
# Run all unit and integration tests
dotnet test Fulfillment.slnx
```

To run individual test projects:

```powershell
# Unit tests
dotnet test tests/Fulfillment.UnitTests

# Integration tests
dotnet test tests/Fulfillment.IntegrationTests
```

---

## 10. JWT Configuration

JWT settings must be configured in `appsettings.json` or secure User Secrets / Environment Variables.

> [!IMPORTANT]
> The `SigningKey` MUST be at least 256 bits (32 characters) long. The application will fail fast at startup if the `SigningKey` is missing or empty.

```json
{
  "Jwt": {
    "Issuer": "FulfillmentApi",
    "Audience": "FulfillmentClients",
    "SigningKey": "<your-secure-jwt-signing-key-at-least-256-bits>",
    "ExpirationMinutes": 60
  }
}
```

### Configuring Secrets via User Secrets (Development)

```powershell
dotnet user-secrets set "Jwt:SigningKey" "<your-secure-jwt-signing-key-at-least-256-bits>" --project src/Fulfillment.Api
```

---

## 11. Admin Bootstrap

Upon application startup, `IdentityInitializer` automatically creates default Identity Roles (`Admin`, `Manager`, `Warehouse Operator`, `Sales Agent`).

If no users exist in the database, `IdentityInitializer` will seed an initial **Admin** user if environment configuration is supplied:

### Environment Variables for Bootstrap
- `SEED_ADMIN_EMAIL`: `<admin-email>`
- `SEED_ADMIN_PASSWORD`: `<strong-admin-password>`

### Example PowerShell Setup before `dotnet run`:
```powershell
$env:SEED_ADMIN_EMAIL="<admin-email>"
$env:SEED_ADMIN_PASSWORD="<strong-admin-password>"
dotnet run --project src/Fulfillment.Api
```

*Note: Role assignment is atomic. If role assignment fails during bootstrap, user creation is safely rolled back.*

---

## 12. Authentication Flow

1. **Login**: Send a `POST /api/auth/login` request with credentials:
   ```json
   {
     "email": "<user-email>",
     "password": "<user-password>"
   }
   ```
2. **Token Response**: Receive HTTP 200 OK with `LoginResponse`:
   ```json
   {
     "token": "eyJhbGciOiJIUzI1NiIsInR5cCI6...",
     "expiresAt": "2026-08-18T15:30:00Z"
   }
   ```
3. **Authenticated Requests**: Include the token in the `Authorization` HTTP header for subsequent calls:
   ```http
   Authorization: Bearer eyJhbGciOiJIUzI1NiIsInR5cCI6...
   ```

---

## 13. Authorization Matrix & Roles

The system defines 4 roles and policy-based authorization:

| Role | User Creation | Catalog Manage | Warehouse Manage | Inventory View | Stock Adjustment |
| :--- | :---: | :---: | :---: | :---: | :---: |
| **Admin** | ✅ | ✅ | ✅ | ✅ | ✅ |
| **Manager** | ❌ | ✅ | ✅ | ✅ | ✅ |
| **Warehouse Operator** | ❌ | ❌ | ❌ | ✅ | ✅ |
| **Sales Agent** | ❌ | ❌ | ❌ | ✅ | ❌ |

- `AdminOnly`: Required for `POST /api/users`.
- `CatalogManage`: Required for creating/deleting categories and products (`Admin`, `Manager`).
- `WarehouseManage`: Required for creating/deleting warehouses (`Admin`, `Manager`).
- `InventoryView`: Required for querying inventory, warehouse inventory, and recent changes (`Admin`, `Manager`, `Warehouse Operator`, `Sales Agent`).
- `InventoryAdjust`: Required for stock adjustments (`Admin`, `Manager`, `Warehouse Operator`). **Forbidden (`403`) for Sales Agent**.

---

## 14. API Endpoint Overview

All 19 production Web API endpoints:

| Domain | Method | Route | Authorization Policy | Description |
| :--- | :--- | :--- | :--- | :--- |
| **Auth** | `POST` | `/api/auth/login` | Anonymous | User authentication & JWT generation |
| **Users** | `POST` | `/api/users` | `AdminOnly` | Create new system user with exactly 1 role |
| **Categories** | `POST` | `/api/categories` | `CatalogManage` | Create category |
| | `GET` | `/api/categories` | `CatalogView` | List all active categories |
| | `GET` | `/api/categories/{id}` | `CatalogView` | Get category by ID |
| | `DELETE` | `/api/categories/{id}` | `CatalogManage` | Soft-delete category |
| **Warehouses** | `POST` | `/api/warehouses` | `WarehouseManage` | Create warehouse |
| | `GET` | `/api/warehouses` | `WarehouseView` | List active warehouses |
| | `GET` | `/api/warehouses/{id}` | `WarehouseView` | Get warehouse by ID |
| | `GET` | `/api/warehouses/{warehouseId}/inventory` | `InventoryView` | Get products stored in warehouse |
| | `DELETE` | `/api/warehouses/{id}` | `WarehouseManage` | Soft-delete warehouse |
| **Products** | `POST` | `/api/products` | `CatalogManage` | Create product with warehouse stock |
| | `GET` | `/api/products` | `CatalogView` | List active products |
| | `GET` | `/api/products/{id}` | `CatalogView` | Get product by ID |
| | `DELETE` | `/api/products/{id}` | `CatalogManage` | Soft-delete product |
| **Inventory** | `GET` | `/api/inventory` | `InventoryView` | List active product stock |
| | `GET` | `/api/inventory/changes` | `InventoryView` | List latest 20 stock adjustments |
| | `GET` | `/api/inventory/{productId}` | `InventoryView` | Get product stock |
| | `POST` | `/api/inventory/{productId}/adjust` | `InventoryAdjust` | Adjust stock quantity atomically |

---

## 15. Swagger UI & OpenAPI Specification

The application provides interactive API documentation and testing via **Swagger UI** in Development mode.

### Distinguishing Swagger UI vs. OpenAPI Specification

- **OpenAPI Specification**: Machine-readable JSON document describing all API routes, parameters, request/response DTO schemas, and security schemes. Available at:
  ```
  http://localhost:<port>/swagger/v1/swagger.json
  ```
- **Swagger UI**: Interactive browser UI for visual exploration and executing requests against live endpoints. Available at:
  ```
  http://localhost:<port>/swagger
  ```

### Authorizing Requests in Swagger UI

1. Open `http://localhost:<port>/swagger` in your browser.
2. Execute `POST /api/auth/login` to obtain your JWT bearer token.
3. Click the **Authorize** button at the top right of the Swagger UI page.
4. In the text field, type `Bearer <your-token>` (e.g., `Bearer eyJhbGciOi...`).
5. Click **Authorize** and then **Close**. All protected endpoints can now be invoked directly within the UI.

---

## 16. Error Handling

Centralized exception handling is governed by [`GlobalExceptionHandler.cs`](file:///c:/Users/Moe/Desktop/fulfillment-inventory-platform/src/Fulfillment.Api/Middleware/GlobalExceptionHandler.cs) and formatted according to **RFC 9457 ProblemDetails**:

```json
{
  "type": "https://tools.ietf.org/html/rfc9110#section-15.5.1",
  "title": "Validation Error",
  "status": 400,
  "detail": "NewQuantity cannot be negative.",
  "traceId": "0hn78abc123:00000001"
}
```

### Standard HTTP Status Mappings
- **400 Bad Request**: Request validation errors (`ValidationException`).
- **401 Unauthorized**: Unauthenticated or invalid token (`UnauthorizedAccessException`).
- **403 Forbidden**: Insufficient role policy (`ForbiddenException`).
- **404 Not Found**: Nonexistent or soft-deleted entity (`NotFoundException`).
- **409 Conflict**: Unique key violation or concurrency conflict (`ConflictException`).
- **500 Internal Server Error**: Unhandled server exceptions.

---

## 17. Development vs. Production Behavior

- **Development (`ASPNETCORE_ENVIRONMENT=Development`)**:
  - Detailed exception messages, inner exceptions, and stack traces included in `ProblemDetails.Detail`.
  - Interactive **Swagger UI** (`/swagger`) and OpenAPI document (`/swagger/v1/swagger.json`) enabled.
  - Verbose logging (`Debug` / `Information`).
- **Production (`ASPNETCORE_ENVIRONMENT=Production`)**:
  - `ProblemDetails.Detail` hides stack traces, internal SQL details, connection strings, and server paths for 500 errors.
  - Swagger UI middleware disabled for security.
  - Strict security and User Secrets / Environment Variable key requirements.

---

## 18. Repository & Governance Notes

- **Database Integrity**: Exactly 1 baseline migration (`20260818111956_InitialCreate`).
- **Clean Git Index**: `.gitignore` prevents check-in of binaries (`bin/`, `obj/`), user settings, test results, local `.db` files, or `.env` secrets.
- **Audit Preservation**: `InventoryAdjustment` audit history records persist independently of `Product`/`Warehouse` soft-deletion status.
