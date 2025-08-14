using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.Text;
using KpiMgmtApi.Data;
using Microsoft.Extensions.Options;
using Microsoft.EntityFrameworkCore;
using Microsoft.OpenApi.Models;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.IdentityModel.Tokens;
using KpiMgmtApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add CORS services
builder.Services.AddCors(options =>
{
    options.AddPolicy("AllowAllOrigins",
        builder =>
        {
            builder.WithOrigins("*") // Allow any origin
                   .AllowAnyMethod() // Allow any method (GET, POST, etc.)
                   .AllowAnyHeader(); // Allow any header
        });
});

// Configure the database context for SQL Server
builder.Services.AddDbContext<TenantInfoContext>(options => options.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

// Register the TenantInfoService for dependency injection
//builder.Services.AddScoped<TenantInfoService>();

// Register BlobStorageService
builder.Services.AddScoped<BlobStorageService>();
builder.Services.AddScoped<CsvFileReader>();
builder.Services.AddScoped<LogAnalyticsService>();
builder.Services.AddScoped<AzureMetricsService>();
builder.Services.AddScoped<GraphAuthProvider>();

builder.Services.AddHttpClient("ApiClient", client =>
{
    //client.BaseAddress = new Uri("https://localhost:7799/em/");
    var baseuri = builder.Configuration["Credentials:BaseAddress"];
    client.BaseAddress = new Uri(baseuri); // Replace with your API base URL

    // Set Basic Auth header
    var username = builder.Configuration["Credentials:Username"];
    var password = builder.Configuration["Credentials:Password"];
    var authToken = Convert.ToBase64String(Encoding.ASCII.GetBytes($"{username}:{password}"));
    client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", authToken);

    // Set Content-Type header
    client.DefaultRequestHeaders.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));
})
.ConfigurePrimaryHttpMessageHandler(() =>
{
    var handler = new HttpClientHandler();

    // Bypass SSL certificate validation (similar to -k in curl)
    handler.ServerCertificateCustomValidationCallback = (message, cert, chain, sslPolicyErrors) => true;

    return handler;
});

// Add JWT Bearer authentication
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        var authSettings = builder.Configuration.GetSection("AuthenticationSettings");
        options.Authority = authSettings["Authority"];
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidAudience = authSettings["ValidAudience"], // API's SPN Application ID URI
        };

        // Customize token extraction
        options.Events = new JwtBearerEvents
        {
            OnMessageReceived = context =>
            {
                var authorizationHeader = context.Request.Headers["Authorization"].FirstOrDefault();

                if (!string.IsNullOrEmpty(authorizationHeader) && !authorizationHeader.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
                {
                    // If the Authorization header is provided without "Bearer", treat it as the token
                    context.Token = authorizationHeader;
                }

                return Task.CompletedTask;
            }
        };
    });

// Add controllers with JSON options
builder.Services.AddControllers()
    .AddJsonOptions(options =>
    {
        options.JsonSerializerOptions.PropertyNamingPolicy = null; // Keep property names as is
    });

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(c =>
{
    c.SwaggerDoc("v1", new OpenApiInfo { Title = "KPI Management API", Version = "v1" });

    // Update Swagger description
    c.AddSecurityDefinition("JWT", new OpenApiSecurityScheme
    {
        Description = "JWT Authorization header. Please Provide the JWT token to authorise : {token}",
        Name = "Authorization",
        In = ParameterLocation.Header,
        Type = SecuritySchemeType.ApiKey,
        Scheme = "JWT"
    });

    c.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "JWT"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();


// Test the database connection on startup
using (var scope = app.Services.CreateScope())
{
    var dbContext = scope.ServiceProvider.GetRequiredService<TenantInfoContext>();
    try
    {
        // This ensures that the connection can be established and schema is valid
        dbContext.Database.CanConnect(); // Check if the connection can be made
        Console.WriteLine("Database connection successful.");
    }
    catch (Exception ex)
    {
        Console.WriteLine($"Database connection failed: {ex.Message}");
    }
}

// Configure the HTTP request pipeline.
//if (app.Environment.IsDevelopment())
//{
//    app.UseSwagger();
//    app.UseSwaggerUI();
//}

// Configure the HTTP request pipeline.
app.UseSwagger();
app.UseSwaggerUI(c =>
{
    c.SwaggerEndpoint("/swagger/v1/swagger.json", "DMS API V1");
});


app.UseCors("AllowAllOrigins");
app.UseHttpsRedirection();
app.UseAuthentication(); // Enable authentication middleware
app.UseAuthorization();
app.MapControllers();
app.Run();
