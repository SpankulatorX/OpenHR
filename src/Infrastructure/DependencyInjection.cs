using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using RegionHR.Core.Contracts;
using RegionHR.Core.Domain;
using RegionHR.Payroll.Domain;
using RegionHR.Payroll.Engine;
using RegionHR.Scheduling.Optimization;
using RegionHR.SharedKernel.Abstractions;
using RegionHR.SharedKernel.Domain;
using RegionHR.Infrastructure.Persistence;
using RegionHR.Infrastructure.Persistence.Repositories;
using RegionHR.Infrastructure.Authorization;
using RegionHR.Infrastructure.BackgroundJobs;
using RegionHR.Infrastructure.Export;
using RegionHR.Infrastructure.Storage;
using RegionHR.Infrastructure.GDPR;
using RegionHR.Infrastructure.Notifications;
using RegionHR.Infrastructure.Documents;
using RegionHR.Infrastructure.Integrations;
using RegionHR.Infrastructure.Reporting;
using RegionHR.Infrastructure.Payroll;
using RegionHR.Infrastructure.Scheduling;
using RegionHR.Infrastructure.Events;
using RegionHR.Infrastructure.Services;
using RegionHR.Automation.Domain;
using RegionHR.Migration.Adapters;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using OpenTelemetry.Metrics;

namespace RegionHR.Infrastructure;

public static class DependencyInjection
{
    public static IServiceCollection AddInfrastructure(this IServiceCollection services, string connectionString, bool useInMemory = false)
    {
        // Domain event infrastructure
        services.AddScoped<IDomainEventDispatcher, DomainEventDispatcher>();
        services.AddScoped<DomainEventInterceptor>();

        // Audit interceptor
        var auditInterceptor = new AuditInterceptor();

        // DbContext
        if (useInMemory)
        {
            services.AddDbContext<RegionHRDbContext>((sp, options) =>
                options.UseInMemoryDatabase("RegionHR-Dev")
                    .AddInterceptors(auditInterceptor, sp.GetRequiredService<DomainEventInterceptor>()));

            services.AddDbContextFactory<RegionHRDbContext>(options =>
                options.UseInMemoryDatabase("RegionHR-Dev"), ServiceLifetime.Scoped);
        }
        else
        {
            // List<string>-kolumner mappas till jsonb — Npgsql kräver explicit
            // EnableDynamicJson(), annars kastar varje write InvalidCastException.
            var dataSourceBuilder = new Npgsql.NpgsqlDataSourceBuilder(connectionString);
            dataSourceBuilder.EnableDynamicJson();
            var dataSource = dataSourceBuilder.Build();

            services.AddDbContext<RegionHRDbContext>((sp, options) =>
                options.UseNpgsql(dataSource, npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                })
                .AddInterceptors(auditInterceptor, sp.GetRequiredService<DomainEventInterceptor>()));

            services.AddDbContextFactory<RegionHRDbContext>(options =>
                options.UseNpgsql(dataSource, npgsql =>
                {
                    npgsql.MigrationsHistoryTable("__EFMigrationsHistory", "public");
                }), ServiceLifetime.Scoped);
        }

        // Repositories
        services.AddScoped<IRepository<Employee, EmployeeId>, EmployeeRepository>();
        services.AddScoped<IRepository<PayrollRun, PayrollRunId>, PayrollRunRepository>();
        services.AddScoped<EmployeeRepository>();
        services.AddScoped<PayrollRunRepository>();

        // Module contracts
        services.AddScoped<ICoreHRModule, CoreHRModuleService>();

        // Payroll services
        services.AddScoped<ITaxTableProvider, TaxTableRepository>();
        services.AddScoped<ICollectiveAgreementRulesEngine, CollectiveAgreementRulesEngine>();
        services.AddScoped<PayrollCalculationEngine>();

        // Scheduling services
        services.AddSingleton<ConstraintScheduleSolver>();

        // UnitOfWork
        services.AddScoped<IUnitOfWork>(sp => new UnitOfWork(sp.GetRequiredService<RegionHRDbContext>()));

        // Export services
        services.AddSingleton<ExportService>();
        services.AddSingleton<PdfPayslipGenerator>();
        services.AddSingleton<PdfGenerator>();

        // Authorization services
        services.AddScoped<UnitScopeService>();
        services.AddScoped<UnitAccessScopeService>();

        // Notifications (EmailNotificationSender is the production MailKit impl)
        services.AddSingleton<EmailNotificationSender>();
        services.AddSingleton<SmsNotificationSender>();
        // EmailSender removed — use EmailNotificationSender instead

        // File storage (LocalFileStorageService implements IFileStorageService)
        services.AddSingleton<IFileStorageService>(new LocalFileStorageService());

        // GDPR
        services.AddScoped<RegisterutdragGenerator>();

        // Reporting
        services.AddScoped<ReportGenerator>();

        // Document template engine & e-signing
        services.AddSingleton<DocumentTemplateEngine>();
        services.AddSingleton<ISigningService, SimpleConfirmationSigningService>();

        // Swedish payroll engine
        services.AddSingleton<SwedishTaxCalculator>();
        services.AddSingleton<KollektivavtalEngine>();
        services.AddSingleton<TraktamentsCalculator>();

        // Integration adapters
        services.AddSingleton<Integrations.AGIXmlGenerator>();
        services.AddSingleton<NordeaPainGenerator>();

        // Schema optimization
        services.AddSingleton<SchemaOptimizer>();

        // Analytics
        services.AddScoped<Analytics.FlightRiskService>();
        services.AddScoped<KPICalculationService>();
        services.AddScoped<PayEquityCalculationService>();
        services.AddScoped<ScenarioCalculationService>();

        // Provisioning (lokal registrering — inga externa anrop i v1)
        services.AddScoped<Provisioning.IIdentityProvider, Provisioning.LocalRecordingProvider>();
        services.AddScoped<Provisioning.ProvisioningService>();

        // Automation engine
        services.AddScoped<ConditionEvaluator>();
        services.AddScoped<AutomationActionExecutor>();
        services.AddScoped<IAutomationEngine, AutomationEngineService>();

        // Extension package service (marketplace)
        services.AddScoped<ExtensionPackageService>();

        // Migration engine
        services.AddScoped<IMigrationAdapter, PAXmlAdapter>();
        services.AddScoped<IMigrationAdapter, HeromaAdapter>();
        services.AddScoped<IMigrationAdapter, GenericCSVAdapter>();
        services.AddScoped<MigrationEngineService>();

        // Webhook delivery (HttpClient for outbound webhook calls)
        services.AddHttpClient<WebhookDeliveryService>();

        // Knowledge base
        services.AddScoped<KnowledgeBaseService>();

        // Background services
        services.AddHostedService<NotificationReminderService>();
        services.AddHostedService<RetentionCleanupService>();
        services.AddHostedService<ScheduledReportService>();
        services.AddHostedService<CertificationReminderService>();
        services.AddHostedService<LASAlertService>();

        // OpenTelemetry
        services.AddOpenTelemetry()
            .ConfigureResource(r => r.AddService("RegionHR"))
            .WithTracing(t => t
                .AddAspNetCoreInstrumentation())
            .WithMetrics(m => m
                .AddAspNetCoreInstrumentation());

        return services;
    }
}

internal class UnitOfWork : IUnitOfWork
{
    private readonly RegionHRDbContext _db;
    public UnitOfWork(RegionHRDbContext db) => _db = db;
    public Task<int> SaveChangesAsync(CancellationToken ct = default) => _db.SaveChangesAsync(ct);
}
