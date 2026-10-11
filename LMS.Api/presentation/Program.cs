using LMS.Api.application;
using LMS.Api.infrastructure;
using LMS.Api.infrastructure.Persistence;
using LMS.Api.presentation;
using LMS.Api.presentation.hubs;
using Microsoft.EntityFrameworkCore;
using System.Text.Json.Serialization;

// Rider can start the project with the repository root as its working directory.
// Search parent directories for .env while preserving variables supplied by the host.
DotNetEnv.Env.NoClobber().TraversePath().Load();

var builder = WebApplication.CreateBuilder(args);
var frontendOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

// Add services to the container.
builder.Services.AddControllers().AddJsonOptions(options =>
    options.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
builder.Services.AddSignalR();
if (frontendOrigins.Length > 0)
{
    builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
        policy.WithOrigins(frontendOrigins)
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials()));
}
builder.Services.AddInfrastructure(builder.Configuration);
builder.Services.AddApplication();
builder.Services.AddPresentation();

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Learn more about configuring OpenAPI at https://aka.ms/aspnet/openapi
builder.Services.AddOpenApi();

var app = builder.Build();
// 
if (!app.Environment.IsEnvironment("Testing"))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    db.Database.Migrate();
    SystemAccountSeeder.SeedAsync(scope.ServiceProvider).GetAwaiter().GetResult();
}

if (app.Environment.IsDevelopment())
{
    app.MapOpenApi();
    app.UseSwagger();
    app.UseSwaggerUI();
}
// Configure the HTTP request pipeline.
app.UseExceptionHandler();
app.UseRateLimiter();
if (frontendOrigins.Length > 0)
{
    app.UseCors("Frontend");
}

if (!app.Environment.IsEnvironment("Testing"))
{
    app.UseHttpsRedirection();
}
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
app.MapHub<ChatHub>("/hubs/chat", options =>
{
    options.CloseOnAuthenticationExpiration = true;
});
app.Run();

public partial class Program;
