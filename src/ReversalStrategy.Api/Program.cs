using ReversalStrategy.Api.Services;

var builder = WebApplication.CreateBuilder(args);

// --- Services ---
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new()
    {
        Title = "Reversal Strategy AI Assistant",
        Version = "v1",
        Description = "FX Reversal Strategy signal engine with RSI, Pivot Points, Weekly S/R levels and Claude AI narrative explanations."
    });
});

// HttpClient for Twelve Data
builder.Services.AddHttpClient("TwelveData", client =>
{
    client.BaseAddress = new Uri("https://api.twelvedata.com/");
    client.Timeout = TimeSpan.FromSeconds(30);
});

// Application services
builder.Services.AddSingleton<IndicatorEngine>();
builder.Services.AddScoped<MarketDataService>();
builder.Services.AddScoped<ReversalStrategyEngine>();
builder.Services.AddScoped<ClaudeExplainerService>();

// CORS for frontend
builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.AllowAnyOrigin().AllowAnyHeader().AllowAnyMethod());
});

var app = builder.Build();

app.UseCors();
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "Reversal Strategy API v1");
    c.RoutePrefix = "swagger";
});

// Serve the frontend static files
app.UseDefaultFiles();
app.UseStaticFiles();

app.MapControllers();

// Health check
app.MapGet("/health", () => Results.Ok(new { status = "healthy", timestamp = DateTime.UtcNow }));

app.Run();
