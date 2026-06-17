using System.Text;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using SendGrid.Extensions.DependencyInjection;
using TheWell.API.Middleware;
using TheWell.API.Services;
using TheWell.Core.Entities;
using TheWell.Core.Interfaces;
using TheWell.Data;
using TheWell.Data.Repositories;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddDbContext<WellDbContext>(opt =>
    opt.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnections"),
        npgsql => npgsql.EnableRetryOnFailure(maxRetryCount: 5, maxRetryDelay: TimeSpan.FromSeconds(10), errorCodesToAdd: null)));

builder.Services.AddScoped<UserRepository>();
builder.Services.AddScoped<GoalRepository>();
builder.Services.AddScoped<DailyLogRepository>();
builder.Services.AddScoped<IntakeRepository>();
builder.Services.AddScoped<AuditRepository>();
builder.Services.AddScoped<MetadataCacheRepository>();
builder.Services.AddScoped<CourseConfigRepository>();
builder.Services.AddScoped<WeekLockRepository>();

builder.Services.AddScoped<IEncryptionService, EncryptionService>();
builder.Services.AddScoped<IOtpService, OtpService>();
builder.Services.AddScoped<IEmailService, EmailService>();
builder.Services.AddScoped<ILockRuleService, LockRuleService>();
builder.Services.AddScoped<IStreakService, StreakService>();
builder.Services.AddScoped<TokenService>();
builder.Services.AddScoped<ContentService>();
builder.Services.AddHttpClient<ContentService>(client =>
{
    client.Timeout = TimeSpan.FromSeconds(8);
});

builder.Services.AddSendGrid(opt =>
    opt.ApiKey = builder.Configuration["SendGrid:ApiKey"]
        ?? throw new InvalidOperationException("SendGrid:ApiKey not configured"));

var jwtSecret = builder.Configuration["Jwt:Secret"]
    ?? throw new InvalidOperationException("Jwt:Secret not configured");
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme)
    .AddJwtBearer(opt =>
    {
        opt.TokenValidationParameters = new TokenValidationParameters
        {
            ValidateIssuer = true,
            ValidateAudience = true,
            ValidateLifetime = true,
            ValidateIssuerSigningKey = true,
            ValidIssuer = builder.Configuration["Jwt:Issuer"],
            ValidAudience = builder.Configuration["Jwt:Audience"],
            IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtSecret))
        };
    });

builder.Services.AddAuthorization();

builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

    // 10 login attempts per IP per 15 minutes
    options.AddPolicy("auth-login", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));

    // 3 OTP requests per IP per 15 minutes
    options.AddPolicy("auth-otp-request", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 3,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));

    // 10 OTP verify attempts per IP per 15 minutes (per-user DB lockout at 5 handles the rest)
    options.AddPolicy("auth-otp-verify", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));

    // 5 password-reset submissions per IP per 15 minutes
    options.AddPolicy("auth-password-reset", ctx =>
        RateLimitPartition.GetFixedWindowLimiter(
            ctx.Connection.RemoteIpAddress?.ToString() ?? "unknown",
            _ => new FixedWindowRateLimiterOptions
            {
                PermitLimit = 5,
                Window = TimeSpan.FromMinutes(15),
                QueueLimit = 0
            }));
});

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddOpenApi();

var app = builder.Build();

using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<WellDbContext>();

    // Create all tables explicitly so startup is idempotent regardless of DB state.
    // Users must exist before any table that references it via FK.
    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""Users"" (
            ""UserID""                      uuid                        NOT NULL DEFAULT gen_random_uuid(),
            ""UniversityEID""               character varying(500)      NOT NULL,
            ""UniversityEIDHash""           character varying(64)       NOT NULL,
            ""Email""                       character varying(500)      NOT NULL,
            ""EmailHash""                   text                        NOT NULL DEFAULT '',
            ""PasswordHash""                character varying(255)      NOT NULL,
            ""IsPasswordResetRequired""     boolean                     NOT NULL DEFAULT true,
            ""AccountStatus""               character varying(20)       NOT NULL DEFAULT 'Pending',
            ""CreatedAt""                   timestamp with time zone    NOT NULL DEFAULT now(),
            ""ProvisionedBy""               uuid                        NULL,
            ""RefreshTokenHash""            text                        NULL,
            ""RefreshTokenExpiresAt""       timestamp with time zone    NULL,
            ""PasswordResetTokenHash""      text                        NULL,
            ""PasswordResetTokenExpiresAt"" timestamp with time zone    NULL,
            CONSTRAINT ""PK_Users"" PRIMARY KEY (""UserID"")
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Users_UniversityEID""     ON ""Users""(""UniversityEID"");
        CREATE        INDEX IF NOT EXISTS ""IX_Users_UniversityEIDHash""  ON ""Users""(""UniversityEIDHash"");
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""CourseConfigs"" (
            ""ConfigID""        uuid                     NOT NULL DEFAULT gen_random_uuid(),
            ""CourseStartDate"" date                     NOT NULL,
            ""CourseEndDate""   date                     NOT NULL,
            ""SetAt""           timestamp with time zone NOT NULL DEFAULT now(),
            ""SetByAdminID""    uuid                     NOT NULL,
            CONSTRAINT ""PK_CourseConfigs"" PRIMARY KEY (""ConfigID"")
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""MetadataCache"" (
            ""CacheID""     uuid                     NOT NULL DEFAULT gen_random_uuid(),
            ""ContentType"" character varying(50)    NOT NULL,
            ""Payload""     text                     NOT NULL,
            ""ExpiryDate""  timestamp with time zone NOT NULL,
            CONSTRAINT ""PK_MetadataCache"" PRIMARY KEY (""CacheID"")
        );
        CREATE INDEX IF NOT EXISTS ""IX_MetadataCache_ContentType"" ON ""MetadataCache""(""ContentType"");
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""WeekLocks"" (
            ""WeekNumber"" integer NOT NULL,
            ""IsLocked""   boolean NOT NULL DEFAULT true,
            CONSTRAINT ""PK_WeekLocks"" PRIMARY KEY (""WeekNumber"")
        );
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""Goals"" (
            ""GoalID""         uuid                     NOT NULL DEFAULT gen_random_uuid(),
            ""UserID""         uuid                     NOT NULL,
            ""GoalDefinition"" text                     NOT NULL,
            ""CreatedAt""      timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT ""PK_Goals""      PRIMARY KEY (""GoalID""),
            CONSTRAINT ""FK_Goals_User"" FOREIGN KEY (""UserID"") REFERENCES ""Users""(""UserID"") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_Goals_UserID"" ON ""Goals""(""UserID"");
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""IntakeQuestions"" (
            ""UserID""                  uuid    NOT NULL,
            ""MyHabit""                 text    NOT NULL DEFAULT '',
            ""MyGoal""                  text    NOT NULL DEFAULT '',
            ""IAmPersonWho""            text    NOT NULL DEFAULT '',
            ""Strategy1""               text    NOT NULL DEFAULT '',
            ""Strategy2""               text    NOT NULL DEFAULT '',
            ""ToImproveMyselfIWill""    text    NOT NULL DEFAULT '',
            ""RewardMyselfWith""        text    NOT NULL DEFAULT '',
            ""PeopleForEncouragement""  text    NOT NULL DEFAULT '',
            ""IsUnlocked""              boolean NOT NULL DEFAULT false,
            ""CompletedAt""             timestamp with time zone NULL,
            CONSTRAINT ""PK_IntakeQuestions""      PRIMARY KEY (""UserID""),
            CONSTRAINT ""FK_IntakeQuestions_User"" FOREIGN KEY (""UserID"") REFERENCES ""Users""(""UserID"") ON DELETE CASCADE
        );
        -- Remove old columns if a previous schema version added them
        ALTER TABLE ""IntakeQuestions"" DROP COLUMN IF EXISTS ""Q1_Response"";
        ALTER TABLE ""IntakeQuestions"" DROP COLUMN IF EXISTS ""Q2_Response"";
        ALTER TABLE ""IntakeQuestions"" DROP COLUMN IF EXISTS ""Q3_Response"";
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""DailyLogs"" (
            ""LogID""       uuid                     NOT NULL DEFAULT gen_random_uuid(),
            ""UserID""      uuid                     NOT NULL,
            ""LogDate""     date                     NOT NULL,
            ""IsCompleted"" boolean                  NOT NULL DEFAULT false,
            ""IsLocked""    boolean                  NOT NULL DEFAULT false,
            ""Note""        text                     NULL,
            ""CreatedAt""   timestamp with time zone NOT NULL DEFAULT now(),
            CONSTRAINT ""PK_DailyLogs""      PRIMARY KEY (""LogID""),
            CONSTRAINT ""FK_DailyLogs_User"" FOREIGN KEY (""UserID"") REFERENCES ""Users""(""UserID"") ON DELETE CASCADE
        );
        CREATE UNIQUE INDEX IF NOT EXISTS ""IX_DailyLogs_UserID_LogDate"" ON ""DailyLogs""(""UserID"", ""LogDate"");
    ");

    db.Database.ExecuteSqlRaw(@"
        CREATE TABLE IF NOT EXISTS ""AuthenticationAudits"" (
            ""AuditID""          uuid                     NOT NULL DEFAULT gen_random_uuid(),
            ""UserID""           uuid                     NOT NULL,
            ""Action""           character varying(50)    NOT NULL,
            ""AttemptTimestamp"" timestamp with time zone NOT NULL DEFAULT now(),
            ""OtpHash""          character varying(255)   NULL,
            ""OtpExpiresAt""     timestamp with time zone NULL,
            CONSTRAINT ""PK_AuthenticationAudits""      PRIMARY KEY (""AuditID""),
            CONSTRAINT ""FK_AuthenticationAudits_User"" FOREIGN KEY (""UserID"") REFERENCES ""Users""(""UserID"") ON DELETE CASCADE
        );
        CREATE INDEX IF NOT EXISTS ""IX_AuthenticationAudits_UserID"" ON ""AuthenticationAudits""(""UserID"");
    ");

    // Add any columns that may be missing on tables created by older schema versions
    db.Database.ExecuteSqlRaw(@"
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""EmailHash""                   text NOT NULL DEFAULT '';
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""RefreshTokenHash""            text NULL;
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""RefreshTokenExpiresAt""       timestamp with time zone NULL;
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordResetTokenHash""      text NULL;
        ALTER TABLE ""Users"" ADD COLUMN IF NOT EXISTS ""PasswordResetTokenExpiresAt"" timestamp with time zone NULL;
    ");
}

// Seed a test account in Development so you can log in without the admin portal
if (app.Environment.IsDevelopment())
{
    using var seedScope = app.Services.CreateScope();
    var db          = seedScope.ServiceProvider.GetRequiredService<WellDbContext>();
    var encryption  = seedScope.ServiceProvider.GetRequiredService<IEncryptionService>();

    if (!db.Users.Any())
    {
        const string testEid      = "E00000001";
        const string testEmail    = "test@thewell.dev";
        const string testPassword = "TestPass123!";

        db.Users.Add(new User
        {
            UniversityEID           = encryption.Encrypt(testEid),
            UniversityEIDHash       = encryption.Hash(testEid),
            Email                   = encryption.Encrypt(testEmail),
            EmailHash               = encryption.Hash(testEmail),
            PasswordHash            = BCrypt.Net.BCrypt.HashPassword(testPassword),
            IsPasswordResetRequired = false,
            AccountStatus           = AccountStatuses.Active
        });
        db.SaveChanges();

        Console.WriteLine("=== DEV SEED ===");
        Console.WriteLine($"  E-number : {testEid}");
        Console.WriteLine($"  Password : {testPassword}");
        Console.WriteLine("================");
    }

    app.MapOpenApi();
}
else
    app.UseHsts();

app.UseMiddleware<ExceptionHandlerMiddleware>();
app.UseHttpsRedirection();
app.UseRateLimiter();
app.UseAuthentication();
app.UseAuthorization();
app.UseMiddleware<GraduationGuardMiddleware>();
app.MapControllers();
app.MapGet("/", () => Results.Ok(new { status = "TheWell API is running", docs = "/openapi/v1.json" }));

app.Run();
