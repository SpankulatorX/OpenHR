using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Metadata.Builders;
using RegionHR.Documents.Domain;

namespace RegionHR.Infrastructure.Persistence.Configurations.Documents;

public class ArchivedDocumentConfiguration : IEntityTypeConfiguration<ArchivedDocument>
{
    public void Configure(EntityTypeBuilder<ArchivedDocument> builder)
    {
        builder.ToTable("archived_documents", "documents");
        builder.HasKey(x => x.Id);

        builder.Property(x => x.Diarienummer).HasMaxLength(60).IsRequired();
        builder.Property(x => x.Titel).HasMaxLength(500).IsRequired();
        builder.Property(x => x.Kategori).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Arkivklass).HasConversion<string>().HasMaxLength(30);
        builder.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
        builder.Property(x => x.Ansvarig).HasMaxLength(200).IsRequired();
        builder.Property(x => x.ArkiveratAv).HasMaxLength(200).IsRequired();
        builder.Property(x => x.IntegritetsHash).HasMaxLength(128).IsRequired();
        builder.Property(x => x.HashAlgoritm).HasMaxLength(20).IsRequired();
        builder.Property(x => x.StoragePath).HasMaxLength(1000);
        builder.Property(x => x.ContentType).HasMaxLength(200);
        builder.Property(x => x.GallradAv).HasMaxLength(200);
        builder.Property(x => x.GallringsSparrOrsak).HasMaxLength(1000);
        builder.Property(x => x.GallringsSparrAv).HasMaxLength(200);

        builder.HasIndex(x => x.SourceDocumentId);
        builder.HasIndex(x => x.AnstallId);
        builder.HasIndex(x => x.Diarienummer);
        builder.HasIndex(x => x.GallringsFrist);
        builder.HasIndex(x => x.Status);
    }
}
