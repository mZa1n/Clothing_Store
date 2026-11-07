using System.Text.Json;
using System.Text.Json.Serialization;
using ClothingStore.Api;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

var migrate = args.Contains("--migrate");
var createAdmin = args.Contains("--create-admin");
var builder = WebApplication.CreateBuilder(args.Where(arg => arg is not ("--migrate" or "--create-admin")).ToArray());
builder.Services.AddDbContext<StoreDb>(options => options.UseNpgsql(
    builder.Configuration.GetConnectionString("Store")
    ?? "Host=localhost;Database=clothing_store;Username=clothing_store"));
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<StoreExceptionHandler>();
builder.AddAccounts();
builder.Services.AddSingleton(TimeProvider.System);
builder.Services.AddControllers(options => options.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()))
    .AddJsonOptions(options => options.JsonSerializerOptions.Converters.Add(
        new JsonStringEnumConverter(JsonNamingPolicy.CamelCase, allowIntegerValues: false)));
builder.Services.AddOpenApi();

var app = builder.Build();
if (migrate || createAdmin)
{
    await Setup.ConfigureDatabase(app, createAdmin);
    return;
}
app.UseExceptionHandler();
app.UseStatusCodePages();
app.UseAuthentication();
app.UseAuthorization();
app.MapControllers();
if (app.Environment.IsDevelopment())
    app.MapOpenApi();
app.MapGet("/health", () => Results.Ok());
app.Run();
