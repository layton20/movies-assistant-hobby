using Microsoft.Extensions.AI;
using MovieAssistant.Api.Agents;
using MovieAssistant.Api.Chat;
using MovieAssistant.Api.Models;
using MovieAssistant.Api.Safety;
using MovieAssistant.Api.Tools;
using MovieAssistant.Api.Tracing;
using OpenAI;
using OpenTelemetry.Exporter;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using System.ClientModel;
using System.Text.Json;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

var builder = WebApplication.CreateBuilder(args);

// ── Secrets ──────────────────────────────────────────────────────────────────
builder.Configuration.AddUserSecrets<Program>();

// ── CORS ─────────────────────────────────────────────────────────────────────
var allowedOrigins = builder.Configuration
    .GetSection("Cors:AllowedOrigins")
    .Get<string[]>() ?? [];

builder.Services.AddCors(options =>
{
    options.AddDefaultPolicy(policy =>
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod());
});

// ── Rate limiting ────────────────────────────────────────────────────────────
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.OnRejected = (context, cancellationToken) =>
    {
        context.HttpContext.RequestServices.GetRequiredService<LangfuseClient>().RecordRateLimit();
        return ValueTask.CompletedTask;
    };
    options.AddPolicy("chat", context =>
        RateLimitPartition.GetFixedWindowLimiter(
            context.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 20,
                Window = TimeSpan.FromMinutes(1)
            }));
});

// ── Swagger ──────────────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// ── Movie dataset ─────────────────────────────────────────────────────────────
// Load once at startup — dataset is static, no point reading from disk per request
var moviesJson = File.ReadAllText(Path.Combine(AppContext.BaseDirectory, "Data", "movies.json"));
var movies = JsonSerializer.Deserialize<List<Movie>>(moviesJson)
    ?? throw new InvalidOperationException("Failed to load movies dataset.");

builder.Services.AddSingleton(movies);

// ── LLM, Tools, Chat, Tracing ─────────────────────────────────────────────────
var apiKey = builder.Configuration["OpenRouter:ApiKey"]
    ?? throw new InvalidOperationException(
        "OpenRouter:ApiKey not configured. Run: dotnet user-secrets set \"OpenRouter:ApiKey\" \"sk-or-...\"");

var model = builder.Configuration["Llm:Model"] ?? "openai/gpt-4o-mini";

// OpenRouter speaks the OpenAI chat-completions protocol, so only the endpoint changes
var openRouterClient = new OpenAIClient(
    new ApiKeyCredential(apiKey),
    new OpenAIClientOptions { Endpoint = new Uri("https://openrouter.ai/api/v1") });

builder.Services.AddChatClient(openRouterClient.GetChatClient(model).AsIChatClient())
    // First .Use* call is outermost: tracing wraps the whole tool loop, so one chat = one trace
    .Use((inner, sp) => new LangfuseTracingChatClient(
        inner,
        sp.GetRequiredService<LangfuseClient>(),
        sp.GetRequiredService<ILogger<LangfuseTracingChatClient>>()))
    .UseFunctionInvocation(configure: c =>  // handles tool call loop transparently, bounded
    {
        c.MaximumIterationsPerRequest = 5;
        c.FunctionInvoker = ToolFailureHandling.InvokeAsync; // wraps tool exceptions so tracing can count them
    })
    .UseLogging();            // logs every request/response via ILogger

// ── Tools ─────────────────────────────────────────────────────────────────────
// Register each ITool — ToolRegistry collects them all via IEnumerable<ITool>
builder.Services.AddSingleton<ITool, MovieSearchTool>();
builder.Services.AddSingleton<ITool, MovieCountTool>();
builder.Services.AddSingleton(sp => new ToolRegistry(sp.GetServices<ITool>()));

// ── Agents ────────────────────────────────────────────────────────────────────
// Same pattern — AgentRegistry collects all IAgent registrations
builder.Services.AddSingleton<IAgent, MovieAssistantAgent>();
builder.Services.AddSingleton(sp => new AgentRegistry(sp.GetServices<IAgent>()));

// ── Chat ──────────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IChatService, ChatService>();

// ── Risk check (Jev via OpenRouter) ───────────────────────────────────────────
builder.Services.Configure<JevOptions>(builder.Configuration.GetSection("Jev"));
builder.Services.PostConfigure<JevOptions>(o =>
{
    if (string.IsNullOrWhiteSpace(o.ApiKey)) o.ApiKey = apiKey;
});
builder.Services.AddHttpClient<IRiskChecker, JevRiskChecker>();

// ── Tracing (Langfuse) ────────────────────────────────────────────────────────
builder.Services.Configure<LangfuseOptions>(builder.Configuration.GetSection("Langfuse"));
builder.Services.AddSingleton<LangfuseClient>();

// Langfuse v4 ingests OTLP spans on /api/public/otel. The version header makes new data show up in
// real time. Spans are batched in memory and flushed when the host shuts down; with no keys
// configured no exporter is registered and tracing does nothing.
var langfuseOptions = builder.Configuration.GetSection("Langfuse").Get<LangfuseOptions>() ?? new LangfuseOptions();
if (langfuseOptions.IsConfigured)
{
    var langfuseAuth = Convert.ToBase64String(
        System.Text.Encoding.UTF8.GetBytes($"{langfuseOptions.PublicKey}:{langfuseOptions.SecretKey}"));

    builder.Services.AddOpenTelemetry()
        .ConfigureResource(r => r.AddService("movie-assistant"))
        .WithTracing(t => t
            .AddSource(LangfuseClient.SourceName)
            .AddOtlpExporter(o =>
            {
                o.Endpoint = new Uri($"{langfuseOptions.BaseUrl.TrimEnd('/')}/api/public/otel/v1/traces");
                o.Protocol = OtlpExportProtocol.HttpProtobuf;
                o.Headers = $"Authorization=Basic {langfuseAuth},x-langfuse-ingestion-version=4";
            }));
}

// ── Build ─────────────────────────────────────────────────────────────────────
var app = builder.Build();

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseCors();
app.UseRateLimiter();

// ── Endpoints ─────────────────────────────────────────────────────────────────
var jsonOptions = new JsonSerializerOptions
{
    PropertyNamingPolicy = JsonNamingPolicy.CamelCase
};

app.MapPost("/chat", async (
    ChatRequest request,
    HttpRequest httpRequest,
    IChatService chatService,
    LangfuseClient langfuse,
    HttpResponse response,
    CancellationToken cancellationToken) =>
{
    if (ChatRequestValidator.Validate(request) is { } validationError)
    {
        langfuse.RecordError(TraceError.From(TraceError.Validation, validationError));
        response.StatusCode = StatusCodes.Status400BadRequest;
        await response.WriteAsJsonAsync(new { error = validationError }, cancellationToken);
        return;
    }

    response.Headers.ContentType = "text/event-stream";
    response.Headers.CacheControl = "no-cache";
    response.Headers.Append("X-Accel-Buffering", "no"); // prevents Nginx buffering the stream

    await foreach (var sseEvent in chatService.StreamAsync(
        request.AgentId, request.History, isEval: httpRequest.Headers.ContainsKey("X-Eval"), cancellationToken))
    {
        await response.WriteAsync(
            $"data: {JsonSerializer.Serialize(sseEvent, jsonOptions)}\n\n", cancellationToken);
        await response.Body.FlushAsync(cancellationToken);
    }
}).RequireRateLimiting("chat");


app.Run();