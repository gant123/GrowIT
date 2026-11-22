using System.Net;
using System.Text.Json;
using FluentValidation;
using Microsoft.AspNetCore.Mvc;

namespace GrowIT.Backend.Middleware;

public class GlobalExceptionHandlerMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<GlobalExceptionHandlerMiddleware> _logger;

    public GlobalExceptionHandlerMiddleware(RequestDelegate next, ILogger<GlobalExceptionHandlerMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (Exception ex)
        {
            await HandleExceptionAsync(context, ex);
        }
    }

    private async Task HandleExceptionAsync(HttpContext context, Exception exception)
    {
        context.Response.ContentType = "application/problem+json";
        
        var problemDetails = new ProblemDetails
        {
            Instance = context.Request.Path
        };

        switch (exception)
        {
            case ValidationException validationEx:
                context.Response.StatusCode = (int)HttpStatusCode.BadRequest;
                problemDetails.Title = "Validation Failed";
                problemDetails.Status = (int)HttpStatusCode.BadRequest;
                problemDetails.Detail = "One or more validation errors occurred.";
                problemDetails.Extensions["errors"] = validationEx.Errors
                    .GroupBy(e => e.PropertyName)
                    .ToDictionary(
                        g => g.Key, 
                        g => g.Select(e => e.ErrorMessage).ToArray()
                    );
                break;

            case KeyNotFoundException:
                context.Response.StatusCode = (int)HttpStatusCode.NotFound;
                problemDetails.Title = "Resource Not Found";
                problemDetails.Status = (int)HttpStatusCode.NotFound;
                problemDetails.Detail = "The requested resource could not be found.";
                break;

            case UnauthorizedAccessException:
                context.Response.StatusCode = (int)HttpStatusCode.Unauthorized;
                problemDetails.Title = "Unauthorized";
                problemDetails.Status = (int)HttpStatusCode.Unauthorized;
                problemDetails.Detail = "You do not have permission to access this resource.";
                break;

            default:
                // Log the REAL error for you (the developer)
                _logger.LogError(exception, "An unhandled exception occurred.");

                // A person loading a page should get a readable page, not raw problem+json.
                if (IsBrowserPageRequest(context.Request) && !context.Response.HasStarted)
                {
                    context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                    context.Response.ContentType = "text/html; charset=utf-8";
                    context.Response.Headers.CacheControl = "no-store";
                    await context.Response.WriteAsync(ErrorPageHtml);
                    return;
                }

                // Return a generic error for the user (Security Best Practice)
                context.Response.StatusCode = (int)HttpStatusCode.InternalServerError;
                problemDetails.Title = "Internal Server Error";
                problemDetails.Status = (int)HttpStatusCode.InternalServerError;
                problemDetails.Detail = "An internal error occurred. Please contact support.";
                break;
        }

        var json = JsonSerializer.Serialize(problemDetails);
        await context.Response.WriteAsync(json);
    }

    // API/BFF callers keep the problem+json contract; only top-level page navigations get HTML.
    private static bool IsBrowserPageRequest(HttpRequest request)
    {
        if (!HttpMethods.IsGet(request.Method) && !HttpMethods.IsHead(request.Method))
        {
            return false;
        }

        if (request.Path.StartsWithSegments("/api", StringComparison.OrdinalIgnoreCase) ||
            request.Path.StartsWithSegments("/bff", StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return request.Headers.Accept.ToString().Contains("text/html", StringComparison.OrdinalIgnoreCase);
    }

    // Self-contained on purpose (no layout, scripts or API calls) so it still renders when the
    // failure is in the app shell itself. Inline styles are permitted by the CSP's style-src.
    private const string ErrorPageHtml = """
        <!doctype html>
        <html lang="en">
        <head>
            <meta charset="utf-8">
            <meta name="viewport" content="width=device-width, initial-scale=1">
            <meta name="robots" content="noindex">
            <title>Something went wrong - grow.IT</title>
            <style>
                body { margin: 0; min-height: 100vh; display: grid; place-items: center; background: #f4f7f1; color: #1c2a24; font-family: system-ui, -apple-system, "Segoe UI", Roboto, sans-serif; }
                main { max-width: 30rem; margin: 1.5rem; padding: 2.25rem; background: #fff; border: 1px solid #dfe7dc; border-radius: 1rem; text-align: center; box-shadow: 0 12px 32px rgba(28, 42, 36, .08); }
                .brand { font-weight: 700; font-size: 1.15rem; margin-bottom: 1.25rem; }
                .brand span { color: #27ae60; }
                h1 { font-size: 1.4rem; margin: 0 0 .6rem; }
                p { margin: 0 0 1.4rem; line-height: 1.55; color: #4b5b53; }
                a { display: inline-block; padding: .65rem 1.15rem; border-radius: .6rem; font-weight: 600; text-decoration: none; }
                a.primary { background: #27ae60; color: #fff; margin-right: .5rem; }
                a.secondary { color: #1c2a24; border: 1px solid #cfd9cc; }
            </style>
        </head>
        <body>
            <main>
                <div class="brand">grow<span>.IT</span></div>
                <h1>Something went wrong on our side</h1>
                <p>The page could not be loaded. Nothing you entered was lost by this error. Try again, and if it keeps happening, let us know what you were doing.</p>
                <a class="primary" href="">Try again</a>
                <a class="secondary" href="/">Go to the start page</a>
            </main>
        </body>
        </html>
        """;
}