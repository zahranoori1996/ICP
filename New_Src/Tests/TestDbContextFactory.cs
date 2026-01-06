using System;
using Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Domain.Entities;

namespace Tests.Helpers;

public static class TestDbContextFactory
{
    public static IsatisDbContext Create()
    {
        var options = new DbContextOptionsBuilder<IsatisDbContext>()
            .UseInMemoryDatabase(databaseName: Guid.NewGuid().ToString())
            .Options;

        var context = new IsatisDbContext(options);
        context.Database.EnsureCreated();
        return context;
    }

    public static IsatisDbContext CreateWithData()
    {
        var context = Create();
        SeedTestData(context);
        return context;
    }

    public static readonly Guid TestProjectId = Guid.Parse("11111111-1111-1111-1111-111111111111");

    private static void SeedTestData(IsatisDbContext context)
    {
        // Create project
        var project = new Project
        {
            ProjectId = TestProjectId,
            ProjectName = "Test Project",
            Owner = "TestUser",
            CreatedAt = DateTime.UtcNow
        };

        context.Projects.Add(project);

        // Add raw data rows (DataId is auto-generated int, don't set it)
        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "S1",
            ColumnData = "{\"Solution Label\": \"S1\", \"Type\": \"Samp\", \"Element\": \"Fe\", \"Corr Con\": 1.2, \"Int\": 1.2}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "S1",
            ColumnData = "{\"Solution Label\": \"S1\", \"Type\": \"Samp\", \"Element\": \"Cu\", \"Corr Con\": 0.5, \"Int\": 0.5}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "S2",
            ColumnData = "{\"Solution Label\": \"S2\", \"Type\": \"Samp\", \"Element\": \"Fe\", \"Corr Con\": 2.3, \"Int\": 2.3}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "S2",
            ColumnData = "{\"Solution Label\": \"S2\", \"Type\": \"Samp\", \"Element\": \"Cu\", \"Corr Con\": 0.8, \"Int\": 0.8}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "OREAS 258",
            ColumnData = "{\"Solution Label\": \"OREAS 258\", \"Type\": \"Samp\", \"Element\": \"Fe\", \"Corr Con\": 10.5, \"Int\": 10.5}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "OREAS 258",
            ColumnData = "{\"Solution Label\": \"OREAS 258\", \"Type\": \"Samp\", \"Element\": \"Cu\", \"Corr Con\": 2.1, \"Int\": 2.1}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "S1-DUP",
            ColumnData = "{\"Solution Label\": \"S1-DUP\", \"Type\": \"Samp\", \"Element\": \"Fe\", \"Corr Con\": 1.25, \"Int\": 1.25}"
        });

        context.RawDataRows.Add(new RawDataRow
        {
            ProjectId = project.ProjectId,
            SampleId = "S1-DUP",
            ColumnData = "{\"Solution Label\": \"S1-DUP\", \"Type\": \"Samp\", \"Element\": \"Cu\", \"Corr Con\": 0.52, \"Int\": 0.52}"
        });

        // Add CRM data (Id is auto-generated int, don't set it)
        context.CrmData.Add(new CrmData
        {
            CrmId = "OREAS 258",
            AnalysisMethod = "4-Acid Digestion",
            ElementValues = "{\"Fe\": 10.2, \"Cu\": 2.0}",
            IsOurOreas = true,
            CreatedAt = DateTime.UtcNow
        });

        context.CrmData.Add(new CrmData
        {
            CrmId = "OREAS 252",
            AnalysisMethod = "Aqua Regia Digestion",
            ElementValues = "{\"Fe\": 15.5, \"Cu\": 3.2}",
            IsOurOreas = true,
            CreatedAt = DateTime.UtcNow
        });

        context.SaveChanges();
    }
}
