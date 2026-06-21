using System.Text.Json;
using GrowIT.Core.Entities;
using GrowIT.Core.Interfaces;
using GrowIT.Infrastructure.Data;
using GrowIT.Shared.DTOs;
using GrowIT.Shared.Enums;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Stripe;
using Stripe.Checkout;
using StripeSubscriptionService = Stripe.SubscriptionService;
using StripeCheckoutSessionService = Stripe.Checkout.SessionService;
using StripePortalSessionService = Stripe.BillingPortal.SessionService;

namespace GrowIT.Backend.Controllers;

[Authorize]
[ApiController]
[Route("api/[controller]")]
public class BillingController : ControllerBase
{
    private readonly ApplicationDbContext _context;
    private readonly ICurrentTenantService _tenantService;
    private readonly ICurrentUserService _currentUserService;
    private readonly IConfiguration _configuration;
    private readonly GrowIT.Backend.Services.IPlanLimitService _planLimits;

    public BillingController(
        ApplicationDbContext context,
        ICurrentTenantService tenantService,
        ICurrentUserService currentUserService,
        IConfiguration configuration,
        GrowIT.Backend.Services.IPlanLimitService planLimits)
    {
        _context = context;
        _tenantService = tenantService;
        _currentUserService = currentUserService;
        _configuration = configuration;
        _planLimits = planLimits;
    }

    [HttpGet("usage")]
    public async Task<ActionResult<PlanUsageDto>> GetUsage(CancellationToken cancellationToken)
    {
        await EnsureDefaultPlansAsync();
        return Ok(await _planLimits.GetUsageAsync(cancellationToken));
    }

    [HttpGet("overview")]
    public async Task<ActionResult<BillingOverviewDto>> GetOverview()
    {
        await EnsureDefaultPlansAsync();

        var subscription = await GetCurrentSubscriptionQuery()
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                PlanId = s.PlanId,
                PlanName = s.Plan != null ? s.Plan.Name : "Unknown",
                Status = s.Status,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                TrialEndsAt = s.TrialEndsAt,
                ExternalSubscriptionId = s.ExternalSubscriptionId
            })
            .FirstOrDefaultAsync();

        return Ok(new BillingOverviewDto
        {
            Plans = await BuildPlanDtosAsync(),
            CurrentSubscription = subscription,
            Invoices = await BuildInvoiceDtosAsync(),
            Payments = await BuildPaymentDtosAsync(),
            Events = await BuildBillingEventDtosAsync(),
            StripeConfigured = IsStripeConfigured(),
            SetupMessage = BuildStripeSetupMessage()
        });
    }

    [HttpGet("plans")]
    public async Task<ActionResult<List<SubscriptionPlanDto>>> GetPlans()
    {
        await EnsureDefaultPlansAsync();
        return Ok(await BuildPlanDtosAsync());
    }

    [HttpPost("plans")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<SubscriptionPlanDto>> CreatePlan(CreateSubscriptionPlanRequest request)
    {
        var plan = new SubscriptionPlan
        {
            Name = request.Name.Trim(),
            PriceMonthly = request.PriceMonthly,
            PriceYearly = request.PriceYearly,
            MaxUsers = request.MaxUsers,
            MaxClients = request.MaxClients,
            FeaturesJson = NormalizeFeaturesJson(request.FeaturesJson)
        };

        _context.SubscriptionPlans.Add(plan);
        await _context.SaveChangesAsync();

        return Ok((await BuildPlanDtosAsync()).First(p => p.Id == plan.Id));
    }

    [HttpPut("plans/{id:guid}")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<IActionResult> UpdatePlan(Guid id, UpdateSubscriptionPlanRequest request)
    {
        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == id);
        if (plan is null)
        {
            return NotFound("Plan not found.");
        }

        plan.Name = request.Name.Trim();
        plan.PriceMonthly = request.PriceMonthly;
        plan.PriceYearly = request.PriceYearly;
        plan.MaxUsers = request.MaxUsers;
        plan.MaxClients = request.MaxClients;
        plan.FeaturesJson = NormalizeFeaturesJson(request.FeaturesJson);

        await _context.SaveChangesAsync();
        return Ok();
    }

    [HttpGet("subscriptions")]
    public async Task<ActionResult<List<SubscriptionDto>>> GetSubscriptions()
    {
        var subscriptions = await _context.Subscriptions
            .Include(s => s.Plan)
            .OrderByDescending(s => s.StartDate)
            .Select(s => new SubscriptionDto
            {
                Id = s.Id,
                PlanId = s.PlanId,
                PlanName = s.Plan != null ? s.Plan.Name : "Unknown",
                Status = s.Status,
                StartDate = s.StartDate,
                EndDate = s.EndDate,
                TrialEndsAt = s.TrialEndsAt,
                ExternalSubscriptionId = s.ExternalSubscriptionId
            })
            .ToListAsync();

        return Ok(subscriptions);
    }

    [HttpGet("invoices")]
    public async Task<ActionResult<List<InvoiceDto>>> GetInvoices() => Ok(await BuildInvoiceDtosAsync());

    [HttpGet("payments")]
    public async Task<ActionResult<List<PaymentDto>>> GetPayments() => Ok(await BuildPaymentDtosAsync());

    [HttpGet("events")]
    public async Task<ActionResult<List<BillingEventDto>>> GetEvents() => Ok(await BuildBillingEventDtosAsync());

    [HttpPost("checkout-session")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<BillingRedirectResponse>> CreateCheckoutSession(CreateCheckoutSessionRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var secretKey = GetStripeSecretKey();
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return BadRequest("Stripe secret key is not configured. Set Stripe__SecretKey.");
        }

        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == request.PlanId);
        if (plan is null)
        {
            return NotFound("Plan not found.");
        }

        if (plan.PriceMonthly <= 0 && plan.PriceYearly <= 0)
        {
            await ActivatePlanAsync(plan, tenantId.Value);
            return Ok(new BillingRedirectResponse { Url = BuildAbsoluteUrl("/billing") });
        }

        var priceId = GetStripePriceId(plan, request.Interval);
        if (string.IsNullOrWhiteSpace(priceId))
        {
            var intervalKey = request.Interval == BillingInterval.Monthly ? "MonthlyPriceId" : "YearlyPriceId";
            return BadRequest($"Stripe price ID is missing for {plan.Name} {request.Interval}. Set Stripe__Plans__{GetPlanKey(plan)}__{intervalKey} or store it in the plan FeaturesJson.");
        }

        var tenant = await _context.Tenants.FirstOrDefaultAsync(t => t.Id == tenantId.Value);
        var user = _currentUserService.UserId.HasValue
            ? await _context.Users.FirstOrDefaultAsync(u => u.Id == _currentUserService.UserId.Value)
            : null;

        var metadata = new Dictionary<string, string>
        {
            ["tenantId"] = tenantId.Value.ToString(),
            ["planId"] = plan.Id.ToString(),
            ["billingInterval"] = request.Interval.ToString()
        };

        var options = new SessionCreateOptions
        {
            Mode = "subscription",
            SuccessUrl = BuildAbsoluteUrl("/billing?checkout=success"),
            CancelUrl = BuildAbsoluteUrl("/billing?checkout=cancelled"),
            ClientReferenceId = tenantId.Value.ToString(),
            CustomerEmail = FirstNonEmpty(user?.Email, tenant?.ContactEmail),
            LineItems =
            [
                new SessionLineItemOptions
                {
                    Price = priceId,
                    Quantity = 1
                }
            ],
            Metadata = metadata,
            SubscriptionData = new SessionSubscriptionDataOptions
            {
                Metadata = metadata
            }
        };

        var session = await new StripeCheckoutSessionService()
            .CreateAsync(options, requestOptions: BuildStripeRequestOptions(secretKey));

        return Ok(new BillingRedirectResponse { Url = session.Url });
    }

    // Switches the tenant to a plan without going through Stripe. Always allowed for free
    // plans; for paid plans this is only permitted when Stripe is not configured (e.g. the
    // demo environment), so production deployments still collect payment via checkout.
    [HttpPost("activate-plan")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<PlanChangeResultDto>> ActivatePlan(ActivatePlanRequest request)
    {
        var tenantId = _tenantService.TenantId;
        if (!tenantId.HasValue || tenantId == Guid.Empty)
        {
            return Unauthorized("No valid tenant context found.");
        }

        var plan = await _context.SubscriptionPlans.FirstOrDefaultAsync(p => p.Id == request.PlanId);
        if (plan is null)
        {
            return NotFound("Plan not found.");
        }

        var isFree = plan.PriceMonthly <= 0 && plan.PriceYearly <= 0;
        if (!isFree && IsStripeConfigured())
        {
            return BadRequest("This plan requires payment. Use the checkout flow to subscribe.");
        }

        await ActivatePlanAsync(plan, tenantId.Value);

        var message = isFree
            ? $"Activated the {plan.Name} plan."
            : $"Switched to the {plan.Name} plan (demo mode — no payment was collected).";
        return Ok(new PlanChangeResultDto { PlanName = plan.Name, Message = message });
    }

    [HttpPost("portal-session")]
    [Authorize(Policy = "AdminOnly")]
    public async Task<ActionResult<BillingRedirectResponse>> CreatePortalSession()
    {
        var secretKey = GetStripeSecretKey();
        if (string.IsNullOrWhiteSpace(secretKey))
        {
            return BadRequest("Stripe secret key is not configured. Set Stripe__SecretKey.");
        }

        var subscription = await GetCurrentSubscriptionQuery().FirstOrDefaultAsync();
        if (subscription is null || string.IsNullOrWhiteSpace(subscription.ExternalSubscriptionId))
        {
            return BadRequest("No Stripe-backed subscription is available for this organization.");
        }

        var stripeSubscription = await new StripeSubscriptionService()
            .GetAsync(subscription.ExternalSubscriptionId, requestOptions: BuildStripeRequestOptions(secretKey));

        if (string.IsNullOrWhiteSpace(stripeSubscription.CustomerId))
        {
            return BadRequest("Stripe customer could not be resolved for the current subscription.");
        }

        var portalSession = await new StripePortalSessionService()
            .CreateAsync(new Stripe.BillingPortal.SessionCreateOptions
            {
                Customer = stripeSubscription.CustomerId,
                ReturnUrl = BuildAbsoluteUrl("/billing")
            }, requestOptions: BuildStripeRequestOptions(secretKey));

        return Ok(new BillingRedirectResponse { Url = portalSession.Url });
    }

    [AllowAnonymous]
    [HttpPost("stripe-webhook")]
    public async Task<IActionResult> HandleStripeWebhook()
    {
        var webhookSecret = _configuration["Stripe:WebhookSecret"];
        if (string.IsNullOrWhiteSpace(webhookSecret))
        {
            return BadRequest("Stripe webhook secret is not configured.");
        }

        var payload = await new StreamReader(HttpContext.Request.Body).ReadToEndAsync();
        var signature = Request.Headers["Stripe-Signature"].ToString();

        try
        {
            EventUtility.ConstructEvent(payload, signature, webhookSecret);
        }
        catch (StripeException)
        {
            return BadRequest("Invalid Stripe webhook signature.");
        }

        using var document = JsonDocument.Parse(payload);
        var root = document.RootElement;
        var eventType = GetString(root, "type") ?? string.Empty;
        var eventId = GetString(root, "id") ?? string.Empty;
        var dataObject = root.GetProperty("data").GetProperty("object");

        switch (eventType)
        {
            case "checkout.session.completed":
                await ReconcileCheckoutSessionAsync(dataObject, eventId);
                break;
            case "customer.subscription.created":
            case "customer.subscription.updated":
            case "customer.subscription.deleted":
                await ReconcileSubscriptionAsync(dataObject, eventType, eventId);
                break;
            case "invoice.created":
            case "invoice.finalized":
            case "invoice.paid":
            case "invoice.payment_failed":
            case "invoice.voided":
                await ReconcileInvoiceAsync(dataObject, eventType, eventId);
                break;
        }

        await _context.SaveChangesAsync();
        return Ok();
    }

    private IQueryable<GrowIT.Core.Entities.Subscription> GetCurrentSubscriptionQuery() =>
        _context.Subscriptions
            .Include(s => s.Plan)
            .Where(s => s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing)
            .OrderByDescending(s => s.StartDate);

    private async Task<List<SubscriptionPlanDto>> BuildPlanDtosAsync()
    {
        var plans = await _context.SubscriptionPlans
            .OrderBy(p => p.PriceMonthly)
            .ThenBy(p => p.Name)
            .ToListAsync();

        return plans.Select(p => new SubscriptionPlanDto
        {
            Id = p.Id,
            Name = p.Name,
            PriceMonthly = p.PriceMonthly,
            PriceYearly = p.PriceYearly,
            MaxUsers = p.MaxUsers,
            MaxClients = p.MaxClients,
            FeaturesJson = p.FeaturesJson,
            Features = GetFeatureList(p.FeaturesJson),
            CanCheckoutMonthly = !string.IsNullOrWhiteSpace(GetStripePriceId(p, BillingInterval.Monthly)),
            CanCheckoutYearly = !string.IsNullOrWhiteSpace(GetStripePriceId(p, BillingInterval.Yearly))
        }).ToList();
    }

    private async Task<List<InvoiceDto>> BuildInvoiceDtosAsync() =>
        await _context.Invoices
            .OrderByDescending(i => i.DueDate)
            .Select(i => new InvoiceDto
            {
                Id = i.Id,
                SubscriptionId = i.SubscriptionId,
                InvoiceNumber = i.InvoiceNumber,
                AmountDue = i.AmountDue,
                AmountPaid = i.AmountPaid,
                Status = i.Status,
                DueDate = i.DueDate,
                PaidAt = i.PaidAt
            })
            .ToListAsync();

    private async Task<List<PaymentDto>> BuildPaymentDtosAsync() =>
        await _context.Payments
            .Include(p => p.Invoice)
            .OrderByDescending(p => p.CreatedAt)
            .Select(p => new PaymentDto
            {
                Id = p.Id,
                InvoiceId = p.InvoiceId,
                InvoiceNumber = p.Invoice != null ? p.Invoice.InvoiceNumber : string.Empty,
                Amount = p.Amount,
                PaymentMethod = p.PaymentMethod,
                PaymentProvider = p.PaymentProvider,
                ExternalPaymentId = p.ExternalPaymentId,
                Status = p.Status,
                PaidAt = p.PaidAt,
                CreatedAt = p.CreatedAt
            })
            .ToListAsync();

    private async Task<List<BillingEventDto>> BuildBillingEventDtosAsync() =>
        await _context.BillingEvents
            .OrderByDescending(e => e.CreatedAt)
            .Take(100)
            .Select(e => new BillingEventDto
            {
                Id = e.Id,
                UserId = e.UserId,
                EventType = e.EventType,
                ReferenceTable = e.ReferenceTable,
                ReferenceId = e.ReferenceId,
                Metadata = e.Metadata,
                CreatedAt = e.CreatedAt
            })
            .ToListAsync();

    private async Task EnsureDefaultPlansAsync()
    {
        if (await _context.SubscriptionPlans.AnyAsync())
        {
            return;
        }

        _context.SubscriptionPlans.AddRange(
            BuildDefaultPlan("Free", 0, 0, 2, 25, "Basic client records", "Tasks", "Impact snapshots"),
            BuildDefaultPlan("Pro", 49, 490, 10, 500, "Full case management", "Financial tracking", "Reports", "Documents"),
            BuildDefaultPlan("Enterprise", 149, 1490, 100, 5000, "Advanced reporting", "Priority support", "Multi-team operations", "Audit visibility"));

        await _context.SaveChangesAsync();
    }

    private static SubscriptionPlan BuildDefaultPlan(
        string name,
        decimal monthly,
        decimal yearly,
        int maxUsers,
        int maxClients,
        params string[] features)
    {
        var json = JsonSerializer.Serialize(new
        {
            features,
            stripeMonthlyPriceId = string.Empty,
            stripeYearlyPriceId = string.Empty
        });

        return new SubscriptionPlan
        {
            Name = name,
            PriceMonthly = monthly,
            PriceYearly = yearly,
            MaxUsers = maxUsers,
            MaxClients = maxClients,
            FeaturesJson = json
        };
    }

    // Cancels the tenant's current active/trialing subscription and activates the given plan.
    // Used for free-plan activation and for direct (non-Stripe) plan changes.
    private async Task ActivatePlanAsync(SubscriptionPlan plan, Guid tenantId)
    {
        var existing = await _context.Subscriptions.FirstOrDefaultAsync(s =>
            s.TenantId == tenantId &&
            (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing));

        if (existing is not null)
        {
            existing.Status = SubscriptionStatus.Canceled;
            existing.EndDate = DateTime.UtcNow;
        }

        var subscription = new GrowIT.Core.Entities.Subscription
        {
            TenantId = tenantId,
            PlanId = plan.Id,
            Status = SubscriptionStatus.Active,
            StartDate = DateTime.UtcNow
        };

        _context.Subscriptions.Add(subscription);
        await AddBillingEventAsync(tenantId, "subscription.activated", "Subscriptions", subscription.Id, new
        {
            planId = plan.Id,
            planName = plan.Name
        });

        await _context.SaveChangesAsync();
    }

    private async Task ReconcileCheckoutSessionAsync(JsonElement session, string eventId)
    {
        var tenantId = TryParseGuid(GetString(session, "client_reference_id"))
            ?? TryParseGuid(GetMetadataValue(session, "tenantId"));
        var planId = TryParseGuid(GetMetadataValue(session, "planId"));
        var stripeSubscriptionId = GetString(session, "subscription");

        if (!tenantId.HasValue || !planId.HasValue || string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            return;
        }

        var subscription = await _context.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ExternalSubscriptionId == stripeSubscriptionId);

        if (subscription is null)
        {
            subscription = new GrowIT.Core.Entities.Subscription
            {
                TenantId = tenantId.Value,
                ExternalSubscriptionId = stripeSubscriptionId,
                StartDate = DateTime.UtcNow
            };
            _context.Subscriptions.Add(subscription);
        }

        subscription.PlanId = planId.Value;
        subscription.Status = SubscriptionStatus.Active;
        subscription.EndDate = null;

        await CancelOtherActiveSubscriptionsAsync(tenantId.Value, subscription);
        await AddBillingEventAsync(tenantId.Value, "stripe.checkout.session.completed", "Subscriptions", subscription.Id, new
        {
            stripeEventId = eventId,
            stripeSubscriptionId,
            stripeCustomerId = GetString(session, "customer")
        });
    }

    private async Task ReconcileSubscriptionAsync(JsonElement stripeSubscription, string eventType, string eventId)
    {
        var stripeSubscriptionId = GetString(stripeSubscription, "id");
        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            return;
        }

        var subscription = await _context.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ExternalSubscriptionId == stripeSubscriptionId);

        var tenantId = TryParseGuid(GetMetadataValue(stripeSubscription, "tenantId")) ?? subscription?.TenantId;
        var planId = TryParseGuid(GetMetadataValue(stripeSubscription, "planId")) ?? subscription?.PlanId;

        if (!tenantId.HasValue || !planId.HasValue)
        {
            return;
        }

        if (subscription is null)
        {
            subscription = new GrowIT.Core.Entities.Subscription
            {
                TenantId = tenantId.Value,
                ExternalSubscriptionId = stripeSubscriptionId,
                StartDate = FromUnixSeconds(stripeSubscription, "created") ?? DateTime.UtcNow
            };
            _context.Subscriptions.Add(subscription);
        }

        subscription.PlanId = planId.Value;
        subscription.Status = eventType == "customer.subscription.deleted"
            ? SubscriptionStatus.Canceled
            : MapSubscriptionStatus(GetString(stripeSubscription, "status"));
        subscription.TrialEndsAt = FromUnixSeconds(stripeSubscription, "trial_end");
        subscription.EndDate = eventType == "customer.subscription.deleted"
            ? DateTime.UtcNow
            : FromUnixSeconds(stripeSubscription, "current_period_end");

        await CancelOtherActiveSubscriptionsAsync(tenantId.Value, subscription);
        await AddBillingEventAsync(tenantId.Value, $"stripe.{eventType}", "Subscriptions", subscription.Id, new
        {
            stripeEventId = eventId,
            stripeSubscriptionId,
            status = GetString(stripeSubscription, "status")
        });
    }

    private async Task ReconcileInvoiceAsync(JsonElement stripeInvoice, string eventType, string eventId)
    {
        var stripeSubscriptionId = GetStringOrObjectId(stripeInvoice, "subscription");
        if (string.IsNullOrWhiteSpace(stripeSubscriptionId))
        {
            return;
        }

        var subscription = await _context.Subscriptions
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(s => s.ExternalSubscriptionId == stripeSubscriptionId);

        if (subscription is null)
        {
            return;
        }

        var stripeInvoiceId = GetString(stripeInvoice, "id") ?? string.Empty;
        var invoiceNumber = FirstNonEmpty(GetString(stripeInvoice, "number"), stripeInvoiceId) ?? stripeInvoiceId;

        var invoice = await _context.Invoices
            .IgnoreQueryFilters()
            .FirstOrDefaultAsync(i => i.TenantId == subscription.TenantId && i.InvoiceNumber == invoiceNumber);

        if (invoice is null)
        {
            invoice = new GrowIT.Core.Entities.Invoice
            {
                TenantId = subscription.TenantId,
                SubscriptionId = subscription.Id,
                InvoiceNumber = invoiceNumber
            };
            _context.Invoices.Add(invoice);
        }

        invoice.AmountDue = GetStripeAmount(stripeInvoice, "amount_due");
        invoice.AmountPaid = GetStripeAmount(stripeInvoice, "amount_paid");
        invoice.Status = MapInvoiceStatus(GetString(stripeInvoice, "status"));
        invoice.DueDate = FromUnixSeconds(stripeInvoice, "due_date") ?? DateTime.UtcNow;
        invoice.PaidAt = GetInvoicePaidAt(stripeInvoice);

        var paymentIntentId = GetStringOrObjectId(stripeInvoice, "payment_intent");
        if (!string.IsNullOrWhiteSpace(paymentIntentId))
        {
            var payment = await _context.Payments
                .IgnoreQueryFilters()
                .FirstOrDefaultAsync(p => p.TenantId == subscription.TenantId && p.ExternalPaymentId == paymentIntentId);

            if (payment is null)
            {
                payment = new Payment
                {
                    TenantId = subscription.TenantId,
                    InvoiceId = invoice.Id,
                    ExternalPaymentId = paymentIntentId,
                    PaymentProvider = "Stripe",
                    PaymentMethod = GrowIT.Shared.Enums.PaymentMethod.Card,
                    CreatedAt = DateTime.UtcNow
                };
                _context.Payments.Add(payment);
            }

            payment.Amount = invoice.AmountPaid > 0 ? invoice.AmountPaid : invoice.AmountDue;
            payment.Status = invoice.Status == InvoiceStatus.Paid ? PaymentStatus.Succeeded : PaymentStatus.Pending;
            payment.PaidAt = invoice.PaidAt;
        }

        await AddBillingEventAsync(subscription.TenantId, $"stripe.{eventType}", "Invoices", invoice.Id, new
        {
            stripeEventId = eventId,
            stripeInvoiceId,
            stripeSubscriptionId
        });
    }

    private async Task CancelOtherActiveSubscriptionsAsync(Guid tenantId, GrowIT.Core.Entities.Subscription current)
    {
        var activeSubscriptions = await _context.Subscriptions
            .IgnoreQueryFilters()
            .Where(s => s.TenantId == tenantId &&
                s.Id != current.Id &&
                (s.Status == SubscriptionStatus.Active || s.Status == SubscriptionStatus.Trialing))
            .ToListAsync();

        foreach (var subscription in activeSubscriptions)
        {
            subscription.Status = SubscriptionStatus.Canceled;
            subscription.EndDate ??= DateTime.UtcNow;
        }
    }

    private async Task AddBillingEventAsync(Guid tenantId, string eventType, string referenceTable, Guid referenceId, object metadata)
    {
        _context.BillingEvents.Add(new BillingEvent
        {
            TenantId = tenantId,
            UserId = _currentUserService.UserId ?? Guid.Empty,
            EventType = eventType,
            ReferenceTable = referenceTable,
            ReferenceId = referenceId,
            Metadata = JsonSerializer.Serialize(metadata),
            CreatedAt = DateTime.UtcNow
        });

        await Task.CompletedTask;
    }

    private bool IsStripeConfigured() =>
        !string.IsNullOrWhiteSpace(GetStripeSecretKey()) &&
        !string.IsNullOrWhiteSpace(_configuration["Stripe:WebhookSecret"]);

    private string? BuildStripeSetupMessage()
    {
        var missing = new List<string>();
        if (string.IsNullOrWhiteSpace(GetStripeSecretKey()))
        {
            missing.Add("Stripe__SecretKey");
        }

        if (string.IsNullOrWhiteSpace(_configuration["Stripe:WebhookSecret"]))
        {
            missing.Add("Stripe__WebhookSecret");
        }

        return missing.Count == 0 ? null : $"Missing billing configuration: {string.Join(", ", missing)}.";
    }

    private string? GetStripeSecretKey() => _configuration["Stripe:SecretKey"];

    private RequestOptions BuildStripeRequestOptions(string secretKey) => new()
    {
        ApiKey = secretKey
    };

    private string? GetStripePriceId(SubscriptionPlan plan, BillingInterval interval)
    {
        var planKey = GetPlanKey(plan);
        var configKey = interval == BillingInterval.Monthly
            ? $"Stripe:Plans:{planKey}:MonthlyPriceId"
            : $"Stripe:Plans:{planKey}:YearlyPriceId";
        var jsonKey = interval == BillingInterval.Monthly
            ? "stripeMonthlyPriceId"
            : "stripeYearlyPriceId";

        return FirstNonEmpty(GetJsonString(plan.FeaturesJson, jsonKey), _configuration[configKey]);
    }

    private static string GetPlanKey(SubscriptionPlan plan)
    {
        var chars = plan.Name.Where(char.IsLetterOrDigit).ToArray();
        return chars.Length == 0 ? plan.Id.ToString("N") : new string(chars);
    }

    private static List<string> GetFeatureList(string json)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.TryGetProperty("features", out var features) &&
                features.ValueKind == JsonValueKind.Array)
            {
                return features.EnumerateArray()
                    .Select(f => f.ValueKind == JsonValueKind.String ? f.GetString() : null)
                    .Where(f => !string.IsNullOrWhiteSpace(f))
                    .Select(f => f!)
                    .ToList();
            }
        }
        catch (JsonException)
        {
        }

        return [];
    }

    private static string? GetJsonString(string json, string propertyName)
    {
        try
        {
            using var document = JsonDocument.Parse(string.IsNullOrWhiteSpace(json) ? "{}" : json);
            if (document.RootElement.TryGetProperty(propertyName, out var value) &&
                value.ValueKind == JsonValueKind.String)
            {
                return value.GetString();
            }
        }
        catch (JsonException)
        {
        }

        return null;
    }

    private static string NormalizeFeaturesJson(string? json)
    {
        if (string.IsNullOrWhiteSpace(json))
        {
            return "{}";
        }

        try
        {
            using var document = JsonDocument.Parse(json);
            return JsonSerializer.Serialize(document.RootElement);
        }
        catch (JsonException)
        {
            return JsonSerializer.Serialize(new { features = new[] { json.Trim() } });
        }
    }

    private string BuildAbsoluteUrl(string path)
    {
        var clientUrl = _configuration["ClientUrl"]?.TrimEnd('/');
        if (!string.IsNullOrWhiteSpace(clientUrl))
        {
            return $"{clientUrl}{path}";
        }

        return $"{Request.Scheme}://{Request.Host}{path}";
    }

    private static string? FirstNonEmpty(params string?[] values) =>
        values.FirstOrDefault(v => !string.IsNullOrWhiteSpace(v));

    private static Guid? TryParseGuid(string? value) =>
        Guid.TryParse(value, out var parsed) ? parsed : null;

    private static string? GetString(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property))
        {
            return null;
        }

        return property.ValueKind switch
        {
            JsonValueKind.String => property.GetString(),
            JsonValueKind.Number => property.GetRawText(),
            _ => null
        };
    }

    private static string? GetStringOrObjectId(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || property.ValueKind == JsonValueKind.Null)
        {
            return null;
        }

        if (property.ValueKind == JsonValueKind.String)
        {
            return property.GetString();
        }

        return property.ValueKind == JsonValueKind.Object ? GetString(property, "id") : null;
    }

    private static string? GetMetadataValue(JsonElement element, string key)
    {
        if (element.TryGetProperty("metadata", out var metadata) &&
            metadata.ValueKind == JsonValueKind.Object &&
            metadata.TryGetProperty(key, out var value) &&
            value.ValueKind == JsonValueKind.String)
        {
            return value.GetString();
        }

        return null;
    }

    private static DateTime? FromUnixSeconds(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) ||
            property.ValueKind == JsonValueKind.Null ||
            !property.TryGetInt64(out var seconds) ||
            seconds <= 0)
        {
            return null;
        }

        return DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime;
    }

    private static DateTime? GetInvoicePaidAt(JsonElement invoice)
    {
        var paidAt = FromUnixSeconds(invoice, "paid_at");
        if (paidAt.HasValue)
        {
            return paidAt;
        }

        if (invoice.TryGetProperty("status_transitions", out var transitions) &&
            transitions.ValueKind == JsonValueKind.Object)
        {
            return FromUnixSeconds(transitions, "paid_at");
        }

        return null;
    }

    private static decimal GetStripeAmount(JsonElement element, string propertyName)
    {
        if (!element.TryGetProperty(propertyName, out var property) || !property.TryGetDecimal(out var cents))
        {
            return 0m;
        }

        return cents / 100m;
    }

    private static SubscriptionStatus MapSubscriptionStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "active" => SubscriptionStatus.Active,
            "trialing" => SubscriptionStatus.Trialing,
            "canceled" => SubscriptionStatus.Canceled,
            _ => SubscriptionStatus.PastDue
        };

    private static InvoiceStatus MapInvoiceStatus(string? status) =>
        status?.ToLowerInvariant() switch
        {
            "draft" => InvoiceStatus.Draft,
            "open" => InvoiceStatus.Open,
            "paid" => InvoiceStatus.Paid,
            "void" => InvoiceStatus.Void,
            "voided" => InvoiceStatus.Void,
            "uncollectible" => InvoiceStatus.Uncollectible,
            _ => InvoiceStatus.Open
        };
}
