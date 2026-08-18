using System.Security.Claims;
using Fulfillment.Application.Inventory;
using Fulfillment.Application.Inventory.DTOs;
using Microsoft.AspNetCore.Mvc;

namespace Fulfillment.Api.Controllers;

[ApiController]
[Route("api/[controller]")]
public class InventoryController : ControllerBase
{
    private readonly IInventoryService _inventoryService;

    public InventoryController(IInventoryService inventoryService)
    {
        _inventoryService = inventoryService;
    }

    [HttpGet]
    [ProducesResponseType(typeof(IEnumerable<InventoryResponse>), StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<InventoryResponse>>> GetAll(CancellationToken cancellationToken)
    {
        var response = await _inventoryService.GetAllAsync(cancellationToken);
        return Ok(response);
    }

    [HttpGet("{productId:guid}")]
    [ProducesResponseType(typeof(InventoryResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    public async Task<ActionResult<InventoryResponse>> GetByProductId(Guid productId, CancellationToken cancellationToken)
    {
        var response = await _inventoryService.GetByProductIdAsync(productId, cancellationToken);
        return Ok(response);
    }

    [HttpPost("{productId:guid}/adjust")]
    [ProducesResponseType(typeof(InventoryAdjustmentResponse), StatusCodes.Status200OK)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status400BadRequest)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status403Forbidden)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status404NotFound)]
    [ProducesResponseType(typeof(ProblemDetails), StatusCodes.Status409Conflict)]
    public async Task<ActionResult<InventoryAdjustmentResponse>> AdjustStock(
        Guid productId,
        [FromBody] AdjustStockRequest request,
        CancellationToken cancellationToken)
    {
        var userId = User.FindFirstValue(ClaimTypes.NameIdentifier);
        if (string.IsNullOrWhiteSpace(userId))
        {
            throw new UnauthorizedAccessException("User identity claim is missing or unauthenticated.");
        }

        var response = await _inventoryService.AdjustStockAsync(productId, request, userId, cancellationToken);
        return Ok(response);
    }
}
