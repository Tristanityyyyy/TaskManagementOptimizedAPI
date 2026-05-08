using Microsoft.AspNetCore.Mvc;
using TaskManagement.Auth;

namespace TaskManagement.Controllers;

public abstract class ApiControllerBase : ControllerBase
{
    protected ResolvedAccount CurrentAccount =>
        HttpContext.Items.TryGetValue(TokenAuthMiddleware.AccountHttpContextKey, out var acc) && acc is ResolvedAccount ra
            ? ra
            : throw new InvalidOperationException(
                "Resolved account missing; TokenAuthMiddleware must run before controller actions.");
}
