using Microsoft.EntityFrameworkCore;

namespace MetaGrow.Api.Data;

/// <summary>One optimistic-concurrency gate covers template selection, report edits and QA transitions.</summary>
public sealed class SoilReportState
{
    public int SurveyId { get; set; }
    public int TemplateId { get; set; }
    public int RegionId { get; set; } = 1;
    public long Version { get; set; }
    public int RevisionNumber { get; set; }
    public Guid? CurrentRevisionId { get; set; }
    public string Status { get; set; } = "Editing";
    // These exceptions do not change survey items or laboratory status in totalgs.
    public string NotSampledJson { get; set; } = "[]";
    // Choices stay in MetaGrow and are rechecked when either record's identifying fields change.
    public string SampleMatchesJson { get; set; } = "[]";
    // Material-specific choices prevent overlapping soil/tissue result IDs from sharing a manual match.
    public string TissueTemplatesJson { get; set; } = "[]";
    public string TissueMatchesJson { get; set; } = "[]";
}

/// <summary>Append-only input and output snapshots. QA status is separate so these bytes never change.</summary>
public sealed class SoilReportRevision
{
    public Guid Id { get; set; }
    public int SurveyId { get; set; }
    public int Number { get; set; }
    public string InputHash { get; set; } = "";
    public string SourceJson { get; set; } = "";
    public string DocumentJson { get; set; } = "";
    public DateTime CreatedUtc { get; set; }
    public string CreatedBy { get; set; } = "";
}

public sealed class SoilReportAudit
{
    public long Id { get; set; }
    public int SurveyId { get; set; }
    public Guid? RevisionId { get; set; }
    public DateTime OccurredUtc { get; set; }
    public string ActorId { get; set; } = "";
    public string Actor { get; set; } = "";
    public string Action { get; set; } = "";
    public string Reason { get; set; } = "";
    public string PreviousStatus { get; set; } = "";
    public string Status { get; set; } = "";
    public double QaHours { get; set; }
}

internal static class SoilReportMapping
{
    public static void Configure(ModelBuilder builder)
    {
        builder.Entity<SoilReportState>(state =>
        {
            state.ToTable("SoilReportStates"); state.HasKey(item => item.SurveyId);
            state.Property(item => item.SurveyId).ValueGeneratedNever();
            state.Property(item => item.Version).IsConcurrencyToken();
            state.Property(item => item.Status).HasMaxLength(20);
            state.Property(item => item.NotSampledJson).HasDefaultValue("[]");
            state.Property(item => item.SampleMatchesJson).HasDefaultValue("[]");
            state.Property(item => item.TissueTemplatesJson).HasDefaultValue("[]");
            state.Property(item => item.TissueMatchesJson).HasDefaultValue("[]");
        });
        builder.Entity<SoilReportRevision>(revision =>
        {
            revision.ToTable("SoilReportRevisions"); revision.HasKey(item => item.Id);
            revision.HasIndex(item => new { item.SurveyId, item.Number }).IsUnique();
            revision.Property(item => item.InputHash).HasMaxLength(64);
            revision.Property(item => item.CreatedBy).HasMaxLength(256);
            revision.HasOne<SoilReportState>().WithMany().HasForeignKey(item => item.SurveyId).OnDelete(DeleteBehavior.Restrict);
        });
        builder.Entity<SoilReportAudit>(audit =>
        {
            audit.ToTable("SoilReportAudits"); audit.HasKey(item => item.Id);
            audit.HasIndex(item => new { item.SurveyId, item.OccurredUtc });
            audit.Property(item => item.ActorId).HasMaxLength(450);
            audit.Property(item => item.Actor).HasMaxLength(256);
            audit.Property(item => item.Action).HasMaxLength(40);
            audit.Property(item => item.Reason).HasMaxLength(1000);
            audit.Property(item => item.PreviousStatus).HasMaxLength(20);
            audit.Property(item => item.Status).HasMaxLength(20);
            audit.HasOne<SoilReportState>().WithMany().HasForeignKey(item => item.SurveyId).OnDelete(DeleteBehavior.Restrict);
        });
    }
}
