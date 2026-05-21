using LibraryAPI.Services;
using LibraryAPI.Services.Interfaces;
using LibraryAPI.Services.Graph;
using LibrarySQLBackend.Models;
using LibrarySQLBackend.Repositories;
using LibrarySQLBackend.Repositories.Interfaces;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Microsoft.OpenApi.Models;
using System.Text;
using Neo4j.Driver;
using graphBackend.Repositories;
using LibrarySQLBackend.Context;
using graphBackend.Repositories.Interfaces;
using LibraryAPI.Services.Graph.Interfaces;

var builder = WebApplication.CreateBuilder(args);

var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");

// DbContext
builder.Services.AddDbContext<AppDbContext>(options =>
    options.UseMySql(connectionString, ServerVersion.AutoDetect(connectionString)));

builder.Services.AddSingleton<IDriver>(sp =>
{
    var config = sp.GetRequiredService<IConfiguration>();

    return GraphDatabase.Driver(
        config["Neo4j:Uri"],
        AuthTokens.Basic(
            config["Neo4j:User"],
            config["Neo4j:Password"]));
});

builder.Services.AddSingleton<MongoDbContext>(sp =>
{
    var configuration = sp.GetRequiredService<IConfiguration>();

    var connectionString = configuration["MongoDb:ConnectionString"];
    var databaseName = configuration["MongoDb:DatabaseName"];

    return new MongoDbContext(connectionString, databaseName);
});

// Repositories + services
builder.Services.AddScoped<IItemRepository, ItemRepository>();
builder.Services.AddScoped<IItemService, ItemService>();

builder.Services.AddScoped<ILoanerRepository, LoanerRepository>();
builder.Services.AddScoped<ILoanerService, LoanerService>();
builder.Services.AddScoped<IPasswordHasher<Loaner>, PasswordHasher<Loaner>>();

builder.Services.AddScoped<ILoanRepository, LoanRepository>();
builder.Services.AddScoped<ILoanService, LoanService>();

builder.Services.AddScoped<IReviewRepository, ReviewRepository>();

// AI service
var aiEnabled = builder.Configuration.GetValue<bool>("Ai:Enabled");

if (aiEnabled)
{
    builder.Services.AddHttpClient<IAiSummaryService, OllamaAiSummaryService>((serviceProvider, client) =>
    {
        var configuration = serviceProvider.GetRequiredService<IConfiguration>();
        var baseUrl = configuration["Ai:BaseUrl"] ?? "http://localhost:11434";

        client.BaseAddress = new Uri(baseUrl);
    });
}
else
{
    builder.Services.AddScoped<IAiSummaryService, DisabledAiSummaryService>();
}

builder.Services.AddScoped<ILoanServiceGraph, LoanServiceGraph>();
builder.Services.AddScoped<ILoanerServiceGraph, LoanerServiceGraph>();
builder.Services.AddScoped<IItemServiceGraph, ItemServiceGraph>();

builder.Services.AddScoped<ILoanRepositoryGraph, LoanRepositoryGraph>();
builder.Services.AddScoped<ILoanerRepositoryGraph, LoanerRepositoryGraph>();
builder.Services.AddScoped<IItemRepositoryGraph, ItemRepositoryGraph>();

// JWT settings
var jwtKey = builder.Configuration["Jwt:Key"]
             ?? throw new InvalidOperationException("Missing Jwt:Key");
var jwtIssuer = builder.Configuration["Jwt:Issuer"]
                ?? throw new InvalidOperationException("Missing Jwt:Issuer");
var jwtAudience = builder.Configuration["Jwt:Audience"]
                  ?? throw new InvalidOperationException("Missing Jwt:Audience");

// Authentication
builder.Services
    .AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(options =>
    {
        options.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = jwtIssuer,
            ValidAudience = jwtAudience,
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();

// Swagger with JWT support
builder.Services.AddSwaggerGen(options =>
{
    options.SwaggerDoc("v1", new OpenApiInfo
    {
        Title = "Library API",
        Version = "v1"
    });

    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
        Name = "Authorization",
        Type = SecuritySchemeType.Http,
        Scheme = "bearer",
        BearerFormat = "JWT",
        In = ParameterLocation.Header,
        Description = "Paste your JWT token here"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
        {
            new OpenApiSecurityScheme
            {
                Reference = new OpenApiReference
                {
                    Type = ReferenceType.SecurityScheme,
                    Id = "Bearer"
                }
            },
            Array.Empty<string>()
        }
    });
});

var app = builder.Build();


 app.UseSwagger();
 app.UseSwaggerUI();


app.UseHttpsRedirection();

app.UseAuthentication();
app.UseAuthorization();

app.MapControllers();

app.Run();
