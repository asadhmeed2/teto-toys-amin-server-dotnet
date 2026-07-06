using DotNetEnv;
using AdmineTetoToys.Application;
using AdmineTetoToys.Infrastructure;

// Load .env file before building the host.
Env.Load(options: new LoadOptions(setEnvVars: true, clobberExistingVars: false));

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
var allowedOrigin = builder.Configuration["CorsOrigin"] ?? "http://localhost:4201";

builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAdminUI", policy =>
    {
        policy.WithOrigins(allowedOrigin)
              .AllowCredentials()
              .AllowAnyMethod()
              .AllowAnyHeader();
    });
});

builder.Services.AddApplication(builder.Configuration);
builder.Services.AddInfrastructure(builder.Configuration);

var app = builder.Build();

app.UseCors("AllowAdminUI");
app.UseHttpsRedirection();

app.MapAdminAuthEndpoints();
app.MapAdminUserEndpoints();
app.MapAdminProductEndpoints();

app.Run();
