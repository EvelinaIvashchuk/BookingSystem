using BookingSystem.Data;
using BookingSystem.Data.Repositories;
using BookingSystem.Mappings;
using BookingSystem.Models;
using BookingSystem.Services;
using BookingSystem.Services.Interfaces;
using BookingSystem.Validators;
using BookingSystem.ViewModels;
using FluentValidation;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Localization;
using Microsoft.EntityFrameworkCore;
using Serilog;

// ── Serilog bootstrap ─────────────────────────────────────────────────────────
Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft.AspNetCore", Serilog.Events.LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.EntityFrameworkCore", Serilog.Events.LogEventLevel.Warning)
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] {SourceContext}{NewLine}  {Message:lj}{NewLine}{Exception}")
    .WriteTo.File("Logs/log-.txt",
        rollingInterval: RollingInterval.Day,
        retainedFileCountLimit: 14,
        outputTemplate: "{Timestamp:yyyy-MM-dd HH:mm:ss.fff zzz} [{Level:u3}] ({SourceContext}) {Message:lj}{NewLine}{Exception}")
    .Enrich.FromLogContext()
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

// ── Database & Identity ───────────────────────────────────────────────────────
var connectionString = builder.Configuration.GetConnectionString("DefaultConnection")
    ?? throw new InvalidOperationException("Connection string 'DefaultConnection' not found.");

builder.Services.AddDbContext<ApplicationDbContext>(options =>
    options.UseMySql(
        connectionString,
        new MySqlServerVersion(new Version(8, 0, 0)),
        mySqlOptions => mySqlOptions.EnableRetryOnFailure(
            maxRetryCount: 10,
            maxRetryDelay: TimeSpan.FromSeconds(10),
            errorNumbersToAdd: null)));

builder.Services.AddIdentity<ApplicationUser, IdentityRole>(options =>
    {
        options.Password.RequireDigit           = true;
        options.Password.RequiredLength         = 8;
        options.Password.RequireUppercase       = true;
        options.Password.RequireNonAlphanumeric = false;

        options.Lockout.DefaultLockoutTimeSpan  = TimeSpan.FromMinutes(10);
        options.Lockout.MaxFailedAccessAttempts = 5;

        options.User.RequireUniqueEmail = true;
    })
    .AddEntityFrameworkStores<ApplicationDbContext>()
    .AddDefaultTokenProviders();

builder.Services.ConfigureApplicationCookie(options =>
{
    options.LoginPath        = "/Account/Login";
    options.AccessDeniedPath = "/Account/AccessDenied";
    options.ExpireTimeSpan   = TimeSpan.FromHours(8);
});

// ── Repositories ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IRentalRepository, RentalRepository>();
builder.Services.AddScoped<ICarRepository,    CarRepository>();

// ── Unit of Work ─────────────────────────────────────────────────────────────
builder.Services.AddScoped<IUnitOfWork, UnitOfWork>();

// ── Services ──────────────────────────────────────────────────────────────────
builder.Services.AddScoped<IRentalService, RentalService>();
builder.Services.AddScoped<ICarService,    CarService>();
builder.Services.AddScoped<IUserService,   UserService>();
builder.Services.AddScoped<IEmailService,   MockEmailService>();
builder.Services.AddHttpClient("s3");
builder.Services.AddSingleton<IStorageService, S3StorageService>();

// ── AutoMapper ────────────────────────────────────────────────────────────────
builder.Services.AddAutoMapper(typeof(RentalMappingProfile).Assembly);

// ── FluentValidation ──────────────────────────────────────────────────────────
builder.Services.AddScoped<IValidator<RentalCreateViewModel>, RentalCreateViewModelValidator>();

// ── Localization ──────────────────────────────────────────────────────────────
builder.Services.AddLocalization();

// ── MVC ───────────────────────────────────────────────────────────────────────
builder.Services.AddControllersWithViews()
    .AddViewLocalization()
    .AddDataAnnotationsLocalization(options =>
    {
        options.DataAnnotationLocalizerProvider = (_, factory) =>
            factory.Create(typeof(BookingSystem.Resources.SharedResources));
    });

var app = builder.Build();

// ── Seed roles and admin user on startup ──────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    await DbSeeder.SeedRolesAndAdminAsync(scope.ServiceProvider);
}

// ── Pipeline ──────────────────────────────────────────────────────────────────
if (app.Environment.IsDevelopment())
{
    app.UseDeveloperExceptionPage();
}
else
{
    app.UseExceptionHandler("/Home/Error");
    app.UseHsts();
}

app.UseStatusCodePagesWithReExecute("/Home/StatusCode", "?code={0}");

app.UseSerilogRequestLogging();

// ── Localization middleware ────────────────────────────────────────────────────
var supportedCultures = new[] { "uk", "en-US", "en" };
var localizationOptions = new RequestLocalizationOptions()
    .SetDefaultCulture("en-US")               // fallback if all providers return null
    .AddSupportedCultures(supportedCultures)
    .AddSupportedUICultures(supportedCultures);

// Remove the default Accept-Language provider — we replace it with our own logic:
// cookie (manual user choice) → region detection (uk header → Ukrainian, else → English)
localizationOptions.RequestCultureProviders.Remove(
    localizationOptions.RequestCultureProviders.OfType<AcceptLanguageHeaderRequestCultureProvider>().FirstOrDefault()!);

// Custom provider: if no cookie set yet, pick language by Accept-Language header.
// Ukrainian browser ("uk*") → uk, everything else → en-US.
localizationOptions.RequestCultureProviders.Add(new CustomRequestCultureProvider(context =>
{
    var acceptLang = context.Request.Headers.AcceptLanguage.ToString();
    var culture = acceptLang.Split(',')
        .Select(l => l.Split(';')[0].Trim())
        .Any(l => l.StartsWith("uk", StringComparison.OrdinalIgnoreCase))
        ? "uk"
        : "en-US";
    return Task.FromResult<ProviderCultureResult?>(new ProviderCultureResult(culture));
}));

app.UseRequestLocalization(localizationOptions);

app.UseHttpsRedirection();
app.UseRouting();

app.UseAuthentication();
app.UseAuthorization();

app.MapStaticAssets();

app.MapControllerRoute(
        name: "default",
        pattern: "{controller=Home}/{action=Index}/{id?}")
    .WithStaticAssets();

app.Run();
