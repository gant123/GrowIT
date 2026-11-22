using GrowIT.Shared.Enums;

namespace GrowIT.Shared.DTOs;

public class InvestmentDetailDto : InvestmentListDto
{
    public Guid ClientId { get; set; }
    public Guid? FamilyMemberId { get; set; }
    public Guid ProgramId { get; set; }

    /// <summary>The money source (fund) that paid for this service.</summary>
    public Guid FundId { get; set; }

    /// <summary>Who was paid (vendor, landlord, utility...). Empty when nobody was recorded.</summary>
    public string PayeeName { get; set; } = "";

    /// <summary>
    /// True once the record is approved: the money is set aside (and later paid), so the cost,
    /// money source and program can no longer be changed.
    /// </summary>
    public bool MoneyLocked => ServiceRecordRules.MoneyLocked(Status);
}

/// <summary>Status rules for editing a service record, shared by the API and the UI.</summary>
public static class ServiceRecordRules
{
    /// <summary>
    /// Draft and pending records have not touched a money source yet, so everything can change.
    /// Approval sets the cost aside from the money source and payment records it leaving, so from
    /// then on only the description, who was paid and the household member can be corrected.
    /// </summary>
    public static bool MoneyLocked(InvestmentStatus status) =>
        status is not (InvestmentStatus.Draft or InvestmentStatus.Pending);

    /// <summary>The word the UI shows for a status ("Disbursed" is shown as "Paid").</summary>
    public static string StatusLabel(InvestmentStatus status) => status switch
    {
        InvestmentStatus.Disbursed => "Paid",
        _ => status.ToString()
    };
}
