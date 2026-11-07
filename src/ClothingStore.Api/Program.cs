using ClothingStore.Api;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddDbContext<StoreDb>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Store")
    ?? "Host=localhost;Database=clothing_store;Username=clothing_store"));
builder.Services.AddProblemDetails();
builder.Services.AddOpenApi();

var app = builder.Build();
app.UseExceptionHandler();
app.UseStatusCodePages();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapGet("/health", () => Results.Ok());
app.Run();
