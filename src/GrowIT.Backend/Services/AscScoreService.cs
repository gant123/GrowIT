using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.EntityFrameworkCore;

namespace GrowIT.Backend.Services;

public interface IAscScoreService
{
    Task<AscScoreDto> GetScoreAsync(CancellationToken cancellationToken);
}

public sealed class AscScoreService : IAscScoreService
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;

    public AscScoreService(ApplicationDbContext context, ICurrentTenantService tenantService)
    {
        _context = context;
        _tenantService = tenantService;
    }

    public async Task<AscScoreDto> GetScoreAsync(CancellationToken cancellationToken)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            throw new UnauthorizedAccessException("No valid tenant context found.");
        }

        var tenant = await _context.Tenants
            .IgnoreQueryFilters()
            .AsNoTracking()
            .FirstOrDefaultAsync(t => t.Id == tenantId.Value, cancellationToken)
            ?? throw new KeyNotFoundException("Organization was not found.");

        var evidence = await BuildEvidenceAsync(tenant, cancellationToken);
        var pillars = BuildPillars(tenant, evidence);
        var rawScore = pillars.Sum(p => p.Score);
        var cappedScore = ApplyReadinessCaps(rawScore, evidence);
        var isScored = ShouldScore(evidence);
        var score = isScored ? Math.Round(cappedScore, 1, MidpointRounding.AwayFromZero) : (decimal?)null;
        var tier = ResolveTier(score);

        return new AscScoreDto
        {
            IsScored = isScored,
            Score = score,
            ScoreStatus = ResolveScoreStatus(isScored, evidence),
            TierName = tier.Name,
            TierRange = tier.Range,
            TierDescription = tier.Description,
            CalculatedAtUtc = DateTime.UtcNow,
            Evidence = evidence,
            Pillars = pillars,
            MissingItems = BuildMissingItems(evidence, pillars),
            NextSteps = BuildNextSteps(score, evidence, pillars),
            Tiers = BuildTiers()
        };
    }

    private async Task<AscScoreEvidenceDto> BuildEvidenceAsync(GrowIT.Core.Entities.Tenant tenant, CancellationToken cancellationToken)
    {
        var fundedStatuses = new[]
        {
            InvestmentStatus.Approved,
            InvestmentStatus.Disbursed,
            InvestmentStatus.Completed
        };

        var investments = await _context.Investments
            .AsNoTracking()
            .Select(i => new
            {
                i.Status,
                i.Amount,
                i.CreatedAt,
                i.FamilyMemberId
            })
            .ToListAsync(cancellationToken);

        var imprints = await _context.Imprints
            .AsNoTracking()
            .Select(i => new
            {
                i.DateOccurred,
                i.Outcome
            })
            .ToListAsync(cancellationToken);

        var growthPlans = await _context.GrowthPlans
            .AsNoTracking()
            .Select(p => new
            {
                p.CreatedAt,
                p.StartDate
            })
            .ToListAsync(cancellationToken);

        var tasks = await _context.Tasks
            .AsNoTracking()
            .Select(t => new
            {
                t.CreatedAt,
                t.CompletedAt,
                t.Status
            })
            .ToListAsync(cancellationToken);

        var reports = await _context.ReportRuns
            .AsNoTracking()
            .Select(r => new
            {
                r.GeneratedAt,
                r.LastDownloadedAt,
                r.Status
            })
            .ToListAsync(cancellationToken);

        var activityDates = new List<DateTime> { tenant.CreatedAt };
        activityDates.AddRange(investments.Select(i => i.CreatedAt));
        activityDates.AddRange(imprints.Select(i => i.DateOccurred));
        activityDates.AddRange(growthPlans.Select(p => p.CreatedAt == default ? p.StartDate : p.CreatedAt));
        activityDates.AddRange(tasks.Select(t => t.CreatedAt));
        activityDates.AddRange(reports.Select(r => r.GeneratedAt));

        var firstActivity = activityDates.Count > 0 ? activityDates.Min() : (DateTime?)null;
        var lastActivity = activityDates.Count > 0 ? activityDates.Max() : (DateTime?)null;
        var activityMonths = firstActivity.HasValue
            ? Math.Max(0, ((DateTime.UtcNow.Year - firstActivity.Value.Year) * 12) + DateTime.UtcNow.Month - firstActivity.Value.Month)
            : 0;

        var programStats = await _context.Programs
            .AsNoTracking()
            .Select(p => new
            {
                HasDescription = !string.IsNullOrWhiteSpace(p.Description),
                HasUnitCost = p.DefaultUnitCost > 0,
                HasCapacity = p.CapacityLimit.HasValue && p.CapacityLimit.Value > 0
            })
            .ToListAsync(cancellationToken);

        var fundStats = await _context.Funds
            .AsNoTracking()
            .Select(f => new
            {
                f.TotalAmount,
                f.AvailableAmount
            })
            .ToListAsync(cancellationToken);

        var documentStats = await _context.Documents
            .AsNoTracking()
            .Select(d => d.Category)
            .ToListAsync(cancellationToken);

        return new AscScoreEvidenceDto
        {
            HasOrganizationProfile = HasOrganizationProfile(tenant),
            ActiveUsers = await _context.Users.AsNoTracking().CountAsync(u => u.IsActive, cancellationToken),
            Clients = await _context.Clients.AsNoTracking().CountAsync(cancellationToken),
            HouseholdMembers = await _context.FamilyMembers.AsNoTracking().CountAsync(cancellationToken),
            Programs = programStats.Count,
            ProgramsWithUnitCosts = programStats.Count(p => p.HasUnitCost),
            ProgramsWithCapacity = programStats.Count(p => p.HasCapacity),
            Funds = fundStats.Count,
            TotalFunds = fundStats.Sum(f => f.TotalAmount),
            FundsAvailable = fundStats.Sum(f => f.AvailableAmount),
            Investments = investments.Count,
            FundedInvestments = investments.Count(i => fundedStatuses.Contains(i.Status)),
            InvestmentsLinkedToPeople = investments.Count(i => i.FamilyMemberId.HasValue),
            Outcomes = imprints.Count,
            PositiveOrMaintainedOutcomes = imprints.Count(i => i.Outcome is ImpactOutcome.Improved or ImpactOutcome.Maintained),
            Documents = documentStats.Count,
            DocumentCategories = documentStats.Distinct().Count(),
            GrowthPlans = growthPlans.Count,
            Tasks = tasks.Count,
            CompletedTasks = tasks.Count(t => t.Status == GrowIT.Shared.Enums.TaskStatus.Completed),
            ReportsGenerated = reports.Count(r => string.Equals(r.Status, "Generated", StringComparison.OrdinalIgnoreCase) || string.IsNullOrWhiteSpace(r.Status)),
            ReportsDownloaded = reports.Count(r => r.LastDownloadedAt.HasValue),
            ReportSchedules = await _context.ReportSchedules.AsNoTracking().CountAsync(s => s.IsActive, cancellationToken),
            VerifiedActivityMonths = activityMonths,
            FirstVerifiedActivityAtUtc = firstActivity,
            LastVerifiedActivityAtUtc = lastActivity
        };
    }

    private static List<AscScorePillarDto> BuildPillars(GrowIT.Core.Entities.Tenant tenant, AscScoreEvidenceDto evidence)
    {
        return
        [
            BuildPillar(
                "legal-compliance",
                "Legal & Compliance",
                "bi bi-shield-check",
                1.1m,
                [
                    (evidence.HasOrganizationProfile, 0.35m, "Organization profile has core contact and type details.", "Complete organization name, type, contact email, and address."),
                    (!string.IsNullOrWhiteSpace(tenant.OrganizationSize), 0.15m, "Organization size is saved.", "Add your organization size."),
                    (evidence.Documents >= 1, 0.25m, "At least one document is uploaded.", "Upload key legal or operating documents."),
                    (evidence.ReportsGenerated >= 1, 0.20m, "At least one report has been generated.", "Generate a basic readiness or impact report."),
                    (evidence.ReportSchedules >= 1, 0.15m, "At least one recurring report is scheduled.", "Schedule a recurring report when reporting is consistent.")
                ]),
            BuildPillar(
                "governance-leadership",
                "Governance & Leadership",
                "bi bi-people",
                1.0m,
                [
                    (evidence.ActiveUsers >= 2, 0.35m, "More than one active team user is in the workspace.", "Add at least one more board, staff, or volunteer user."),
                    (evidence.ActiveUsers >= 3, 0.20m, "Multiple users can share responsibility.", "Invite enough team members to show role depth."),
                    (evidence.Tasks >= 1, 0.20m, "Team follow-up work is being tracked.", "Create follow-ups for next steps and document collection."),
                    (evidence.CompletedTasks >= 2, 0.15m, "Completed follow-ups show operating follow-through.", "Close follow-ups as work is finished."),
                    (evidence.Documents >= 3, 0.10m, "Documents support governance review.", "Upload board, policy, or authorization documents.")
                ]),
            BuildPillar(
                "document-hub",
                "Document Hub Completion",
                "bi bi-folder2-open",
                1.0m,
                [
                    (evidence.Documents >= 1, 0.25m, "Documents are being stored in Grow.IT.", "Upload the first key document."),
                    (evidence.Documents >= 3, 0.25m, "Several documents are available.", "Upload at least three core documents."),
                    (evidence.Documents >= 8, 0.25m, "The document hub has depth.", "Keep adding legal, program, budget, and reporting records."),
                    (evidence.DocumentCategories >= 2, 0.15m, "Documents cover more than one category.", "Organize documents across multiple categories."),
                    (evidence.ReportsDownloaded >= 1, 0.10m, "Generated reports have been exported.", "Export a generated report for external use.")
                ]),
            BuildPillar(
                "financial-stability",
                "Financial Stability",
                "bi bi-currency-dollar",
                1.4m,
                [
                    (evidence.Funds >= 1, 0.20m, "At least one money source is tracked.", "Add at least one money source."),
                    (evidence.Funds >= 2, 0.20m, "More than one money source is tracked.", "Track more than one donor, grant, or budget source."),
                    (evidence.TotalFunds > 0, 0.25m, $"${evidence.TotalFunds:N0} in total money sources is recorded.", "Enter the dollar amount for available funding sources."),
                    (evidence.FundsAvailable > 0, 0.20m, $"${evidence.FundsAvailable:N0} remains available.", "Keep fund balances updated."),
                    (evidence.Investments >= 3, 0.20m, "Service records show how money is requested or used.", "Create several service records tied to programs and funds."),
                    (evidence.FundedInvestments >= 3, 0.25m, "Approved or completed support is documented.", "Approve or complete support records after review."),
                    (evidence.VerifiedActivityMonths >= 2, 0.10m, "Financial activity has some history.", "Keep records current over multiple months.")
                ]),
            BuildPillar(
                "program-structure",
                "Program / Service Structure",
                "bi bi-diagram-3",
                1.3m,
                [
                    (evidence.Programs >= 1, 0.25m, "At least one program is defined.", "Add your first program or service."),
                    (evidence.Programs >= 3, 0.20m, "Several programs or services are defined.", "Add each major service as its own program."),
                    (evidence.ProgramsWithUnitCosts >= evidence.Programs && evidence.Programs > 0, 0.25m, "Programs include expected unit costs.", "Add a default cost to every program."),
                    (evidence.ProgramsWithCapacity >= 1, 0.20m, "At least one program includes capacity.", "Add program capacity so Grow.IT can show scale limits."),
                    (evidence.Investments >= 3, 0.20m, "Service records are tied to programs.", "Use program-linked service records."),
                    (evidence.GrowthPlans >= 1, 0.20m, "Growth plans connect services to next steps.", "Create growth plans for the people or households served.")
                ]),
            BuildPillar(
                "impact-outcome",
                "Impact & Outcome Evidence",
                "bi bi-graph-up-arrow",
                1.4m,
                [
                    (evidence.Clients + evidence.HouseholdMembers >= 3, 0.20m, "People and households served are tracked.", "Add the people or households being helped."),
                    (evidence.Outcomes >= 1, 0.25m, "At least one outcome is recorded.", "Record the first outcome or milestone."),
                    (evidence.Outcomes >= 5, 0.25m, "Multiple outcomes show repeated impact tracking.", "Keep recording outcomes over time."),
                    (evidence.PositiveOrMaintainedOutcomes >= 3, 0.20m, "Positive or maintained outcomes are documented.", "Mark outcomes that improved or maintained stability."),
                    (evidence.FundedInvestments >= 3, 0.20m, "Funded support can be connected to impact.", "Approve or complete support records connected to outcomes."),
                    (evidence.InvestmentsLinkedToPeople >= 3, 0.15m, "Some dollars are tied directly to individuals helped.", "Link more service records to the child or person helped."),
                    (evidence.ReportsGenerated >= 1, 0.15m, "Impact can be converted into a report.", "Generate an impact or program report.")
                ]),
            BuildPillar(
                "donor-funder-readiness",
                "Donor / Funder Readiness",
                "bi bi-heart",
                1.0m,
                [
                    (evidence.Funds >= 2, 0.20m, "Multiple money sources are tracked.", "Track donor, grant, partner, and budget sources separately."),
                    (evidence.ReportsGenerated >= 1, 0.25m, "Reports can be produced from saved data.", "Generate a funder-ready report."),
                    (evidence.ReportsDownloaded >= 1, 0.15m, "At least one report has been exported.", "Export a report for sharing."),
                    (evidence.Outcomes >= 5, 0.15m, "Outcome history supports funding conversations.", "Add enough outcomes to support the ask."),
                    (evidence.TotalFunds >= 10000m, 0.15m, "Meaningful funding activity is recorded.", "Record current and past funding sources."),
                    (evidence.ReportSchedules >= 1, 0.10m, "Recurring reporting is configured.", "Schedule recurring funder or board reports.")
                ]),
            BuildPillar(
                "operational-consistency",
                "Operational Consistency",
                "bi bi-arrow-repeat",
                0.9m,
                [
                    (evidence.Tasks >= 3, 0.20m, "Operational follow-ups are being tracked.", "Track recurring follow-ups and administrative work."),
                    (evidence.CompletedTasks >= 2, 0.20m, "Completed follow-ups show follow-through.", "Close follow-ups when work is done."),
                    (evidence.GrowthPlans >= 2, 0.20m, "Growth plans show repeatable service planning.", "Create more growth plans for active cases."),
                    (evidence.Investments >= 5, 0.15m, "Service records show repeat activity.", "Keep entering service records as work happens."),
                    (evidence.VerifiedActivityMonths >= 3, 0.15m, "Activity spans multiple months.", "Build a longer record of consistent use.")
                ]),
            BuildPillar(
                "time-verified-history",
                "Time & Verified History",
                "bi bi-clock-history",
                0.9m,
                [
                    (evidence.VerifiedActivityMonths >= 2, 0.20m, "At least two months of activity are visible.", "Keep using Grow.IT across the first two months."),
                    (evidence.VerifiedActivityMonths >= 6, 0.20m, "At least six months of activity are visible.", "Build a six-month operating record."),
                    (evidence.VerifiedActivityMonths >= 12, 0.20m, "At least one year of activity is visible.", "Build a twelve-month operating record."),
                    (evidence.Documents >= 3, 0.15m, "Documents support verification.", "Upload enough records to support the score."),
                    (evidence.ReportsDownloaded >= 1, 0.15m, "Exported reports show data can leave the platform.", "Download reports for board, donor, or lender review.")
                ])
        ];
    }

    private static AscScorePillarDto BuildPillar(
        string key,
        string label,
        string icon,
        decimal maxScore,
        IReadOnlyList<(bool Met, decimal Points, string Evidence, string Missing)> rules)
    {
        var score = rules.Where(r => r.Met).Sum(r => r.Points);
        return new AscScorePillarDto
        {
            Key = key,
            Label = label,
            Icon = icon,
            MaxScore = maxScore,
            Score = Math.Min(maxScore, score),
            Status = score / maxScore >= 0.75m ? "Strong" : score / maxScore >= 0.45m ? "Building" : "Needs Work",
            Evidence = rules.Where(r => r.Met).Select(r => r.Evidence).ToList(),
            MissingItems = rules.Where(r => !r.Met).Select(r => r.Missing).Distinct().ToList()
        };
    }

    private static bool HasOrganizationProfile(GrowIT.Core.Entities.Tenant tenant) =>
        !string.IsNullOrWhiteSpace(tenant.Name) &&
        !string.IsNullOrWhiteSpace(tenant.ContactEmail) &&
        !string.IsNullOrWhiteSpace(tenant.OrganizationType) &&
        !string.IsNullOrWhiteSpace(tenant.Address);

    private static bool ShouldScore(AscScoreEvidenceDto evidence) =>
        evidence.HasOrganizationProfile &&
        (evidence.Programs > 0 || evidence.Funds > 0 || evidence.Clients > 0 || evidence.Documents > 0);

    private static decimal ApplyReadinessCaps(decimal rawScore, AscScoreEvidenceDto evidence)
    {
        var cap = 10.0m;

        if (!ShouldScore(evidence))
        {
            return 0m;
        }

        if (evidence.Programs == 0 || evidence.Funds == 0)
        {
            cap = Math.Min(cap, 4.9m);
        }

        if (evidence.FundedInvestments == 0 || evidence.Outcomes == 0)
        {
            cap = Math.Min(cap, 7.4m);
        }

        if (evidence.Documents < 2 || evidence.ReportsGenerated == 0 || evidence.ActiveUsers < 2)
        {
            cap = Math.Min(cap, 8.9m);
        }

        if (evidence.VerifiedActivityMonths < 12 || evidence.Documents < 5 || evidence.ReportsDownloaded == 0)
        {
            cap = Math.Min(cap, 8.9m);
        }

        return Math.Min(rawScore, cap);
    }

    private static string ResolveScoreStatus(bool isScored, AscScoreEvidenceDto evidence)
    {
        if (!isScored)
        {
            return "Unscored";
        }

        var verified = evidence.Documents >= 3 &&
            evidence.ReportsGenerated >= 1 &&
            evidence.FundedInvestments >= 3 &&
            evidence.Outcomes >= 3 &&
            evidence.ActiveUsers >= 2;

        return verified ? "Verified" : "Provisional";
    }

    private static AscScoreTierDto ResolveTier(decimal? score)
    {
        if (!score.HasValue)
        {
            return BuildTiers()[0];
        }

        return score.Value switch
        {
            < 2.0m => BuildTiers()[1],
            < 5.0m => BuildTiers()[2],
            < 7.5m => BuildTiers()[3],
            < 9.0m => BuildTiers()[4],
            _ => BuildTiers()[5]
        };
    }

    private static List<AscScoreTierDto> BuildTiers() =>
    [
        new() { Name = "Unscored", Range = "Not enough information", Description = "Grow.IT needs more saved data before calculating ASC.", Color = "#64748b", Icon = "bi bi-dash-circle" },
        new() { Name = "Organizing Stability", Range = "0.0 - 1.9", Description = "The organization is building its foundation.", Color = "#dc2626", Icon = "bi bi-flower1" },
        new() { Name = "Foundation Stage", Range = "2.0 - 4.9", Description = "Basic structure, systems, and documentation are being built.", Color = "#f97316", Icon = "bi bi-bricks" },
        new() { Name = "Funding Readiness", Range = "5.0 - 7.4", Description = "Programs, evidence, and early impact are strong enough for grants and donors.", Color = "#16a34a", Icon = "bi bi-bar-chart-line" },
        new() { Name = "Donor & Institutional Ready", Range = "7.5 - 8.9", Description = "Records, reports, and impact support larger funder conversations.", Color = "#0284c7", Icon = "bi bi-people-fill" },
        new() { Name = "Capital Ready", Range = "9.0 - 10.0", Description = "Verified stability, governance, and financial depth support major capital opportunities.", Color = "#6d28d9", Icon = "bi bi-bank" }
    ];

    private static List<string> BuildMissingItems(AscScoreEvidenceDto evidence, IReadOnlyList<AscScorePillarDto> pillars)
    {
        var blockers = new List<string>();

        if (!evidence.HasOrganizationProfile)
        {
            blockers.Add("Complete the organization profile with name, type, contact email, and address.");
        }

        if (evidence.Programs == 0)
        {
            blockers.Add("Add at least one program or service.");
        }

        if (evidence.Funds == 0)
        {
            blockers.Add("Add at least one money source with a dollar amount.");
        }

        if (evidence.Outcomes == 0)
        {
            blockers.Add("Record at least one impact outcome or milestone.");
        }

        if (evidence.Documents < 3)
        {
            blockers.Add("Upload more documents so the score is supported by records.");
        }

        if (evidence.ReportsGenerated == 0)
        {
            blockers.Add("Generate a report from saved data.");
        }

        blockers.AddRange(pillars
            .OrderBy(p => p.Score / p.MaxScore)
            .SelectMany(p => p.MissingItems.Take(1))
            .Take(4));

        return blockers.Distinct().Take(8).ToList();
    }

    private static List<string> BuildNextSteps(decimal? score, AscScoreEvidenceDto evidence, IReadOnlyList<AscScorePillarDto> pillars)
    {
        if (!score.HasValue)
        {
            return
            [
                "Complete the organization profile.",
                "Add the first program or service.",
                "Add the first money source.",
                "Add people, outcomes, or documents so Grow.IT has enough data to score."
            ];
        }

        var weakest = pillars
            .OrderBy(p => p.Score / p.MaxScore)
            .Take(3)
            .SelectMany(p => p.MissingItems.Take(1))
            .ToList();

        if (score < 5.0m)
        {
            weakest.Insert(0, "Build the foundation: programs, funds, documents, and a few outcomes.");
        }
        else if (score < 7.5m)
        {
            weakest.Insert(0, "Strengthen readiness by connecting dollars, programs, people helped, and outcomes.");
        }
        else if (score < 9.0m)
        {
            weakest.Insert(0, "Add verification depth: more documents, exported reports, and longer operating history.");
        }
        else
        {
            weakest.Insert(0, "Keep records current and review the capital-ready package before sharing externally.");
        }

        if (evidence.VerifiedActivityMonths < 2)
        {
            weakest.Add("Use Grow.IT consistently across the first two months before treating the score as mature.");
        }

        return weakest.Distinct().Take(5).ToList();
    }
}
