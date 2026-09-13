namespace Dokpod.Bff.Authentication;

public sealed class OriginValidationMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context, RequestOriginValidator validator)
    {
        var requiresValidation = context.WebSockets.IsWebSocketRequest
            || (!HttpMethods.IsGet(context.Request.Method) && !HttpMethods.IsHead(context.Request.Method)
                && !HttpMethods.IsOptions(context.Request.Method));
        if (requiresValidation && !validator.IsAllowed(context.Request.Headers.Origin))
        {
            context.Response.StatusCode = StatusCodes.Status403Forbidden;
            return;
        }
        await next(context);
    }
}