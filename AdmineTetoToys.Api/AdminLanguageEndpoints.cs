using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using AdmineTetoToys.Domain.Interfaces;

public static class AdminLanguageEndpoints
{
    public static void MapAdminLanguageEndpoints(this IEndpointRouteBuilder app)
    {
        // GET /api/admin/languages — returns all available system languages
        app.MapGet("/api/admin/languages", async (HttpContext context) =>
        {
            var authCheck = await AdminSessionValidator.ValidateSessionAsync(context);
            if (!authCheck.Authorized) return authCheck.ErrorResult!;

            var productRepo = context.RequestServices.GetRequiredService<IProductRepository>();
            var languages = await productRepo.GetLanguagesAsync();

            return Results.Ok(languages.Select(l => new
            {
                code = l.Code,
                name = l.Name,
                is_rtl = l.IsRtl
            }));
        });
    }
}
