using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class Intial_Db : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "CrmData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    CrmId = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false),
                    AnalysisMethod = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Type = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    ElementValues = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    IsOurOreas = table.Column<bool>(type: "bit", nullable: false, defaultValue: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_CrmData", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ProjectImportJobs",
                columns: table => new
                {
                    JobId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ResultProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    ProjectName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: true),
                    JobType = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    State = table.Column<int>(type: "int", nullable: false),
                    TotalRows = table.Column<int>(type: "int", nullable: false),
                    ProcessedRows = table.Column<int>(type: "int", nullable: false),
                    Percent = table.Column<int>(type: "int", nullable: false),
                    Message = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    TempFilePath = table.Column<string>(type: "nvarchar(2000)", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    UpdatedAt = table.Column<DateTime>(type: "datetime2", nullable: true),
                    OperationId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Attempts = table.Column<int>(type: "int", nullable: false),
                    LastError = table.Column<string>(type: "nvarchar(max)", nullable: true),
                    NextAttemptAt = table.Column<DateTime>(type: "datetime2", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectImportJobs", x => x.JobId);
                });

            migrationBuilder.CreateTable(
                name: "Projects",
                columns: table => new
                {
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ProjectName = table.Column<string>(type: "nvarchar(250)", maxLength: 250, nullable: false),
                    Owner = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false),
                    LastModifiedAt = table.Column<DateTime>(type: "datetime2", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Projects", x => x.ProjectId);
                });

            migrationBuilder.CreateTable(
                name: "ChangeLogs",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ChangeType = table.Column<string>(type: "nvarchar(50)", maxLength: 50, nullable: false),
                    SolutionLabel = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Element = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: true),
                    OldValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    NewValue = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    ChangedBy = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Details = table.Column<string>(type: "nvarchar(4000)", maxLength: 4000, nullable: true),
                    BatchId = table.Column<Guid>(type: "uniqueidentifier", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ChangeLogs", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ChangeLogs_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProcessedData",
                columns: table => new
                {
                    Id = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProcessedId = table.Column<int>(type: "int", nullable: false),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    AnalysisType = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: false),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedAt = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()")
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProcessedData", x => x.Id);
                    table.ForeignKey(
                        name: "FK_ProcessedData_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "ProjectStates",
                columns: table => new
                {
                    StateId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ParentStateId = table.Column<int>(type: "int", nullable: true),
                    VersionNumber = table.Column<int>(type: "int", nullable: false, defaultValue: 1),
                    ProcessingType = table.Column<string>(type: "nvarchar(100)", maxLength: 100, nullable: false, defaultValue: "Import"),
                    Data = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    Timestamp = table.Column<DateTime>(type: "datetime2", nullable: false, defaultValueSql: "GETUTCDATE()"),
                    Description = table.Column<string>(type: "nvarchar(500)", maxLength: 500, nullable: true),
                    IsActive = table.Column<bool>(type: "bit", nullable: false, defaultValue: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ProjectStates", x => x.StateId);
                    table.ForeignKey(
                        name: "FK_ProjectStates_ProjectStates_ParentStateId",
                        column: x => x.ParentStateId,
                        principalTable: "ProjectStates",
                        principalColumn: "StateId",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_ProjectStates_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateTable(
                name: "RawDataRows",
                columns: table => new
                {
                    DataId = table.Column<int>(type: "int", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    ProjectId = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    ColumnData = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    SampleId = table.Column<string>(type: "nvarchar(200)", maxLength: 200, nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_RawDataRows", x => x.DataId);
                    table.ForeignKey(
                        name: "FK_RawDataRows_Projects_ProjectId",
                        column: x => x.ProjectId,
                        principalTable: "Projects",
                        principalColumn: "ProjectId",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLogs_BatchId",
                table: "ChangeLogs",
                column: "BatchId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLogs_ProjectId",
                table: "ChangeLogs",
                column: "ProjectId");

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLogs_ProjectId_ChangeType",
                table: "ChangeLogs",
                columns: new[] { "ProjectId", "ChangeType" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLogs_ProjectId_SolutionLabel",
                table: "ChangeLogs",
                columns: new[] { "ProjectId", "SolutionLabel" });

            migrationBuilder.CreateIndex(
                name: "IX_ChangeLogs_Timestamp",
                table: "ChangeLogs",
                column: "Timestamp");

            migrationBuilder.CreateIndex(
                name: "IX_CrmData_AnalysisMethod",
                table: "CrmData",
                column: "AnalysisMethod");

            migrationBuilder.CreateIndex(
                name: "IX_CrmData_CrmId",
                table: "CrmData",
                column: "CrmId");

            migrationBuilder.CreateIndex(
                name: "IX_CrmData_CrmId_AnalysisMethod",
                table: "CrmData",
                columns: new[] { "CrmId", "AnalysisMethod" });

            migrationBuilder.CreateIndex(
                name: "IX_CrmData_IsOurOreas",
                table: "CrmData",
                column: "IsOurOreas");

            migrationBuilder.CreateIndex(
                name: "IX_ProcessedData_Project_ProcessedId",
                table: "ProcessedData",
                columns: new[] { "ProjectId", "ProcessedId" });

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStates_ParentStateId",
                table: "ProjectStates",
                column: "ParentStateId");

            migrationBuilder.CreateIndex(
                name: "IX_ProjectStates_ProjectId_IsActive",
                table: "ProjectStates",
                columns: new[] { "ProjectId", "IsActive" });

            migrationBuilder.CreateIndex(
                name: "IX_RawDataRows_ProjectId",
                table: "RawDataRows",
                column: "ProjectId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ChangeLogs");

            migrationBuilder.DropTable(
                name: "CrmData");

            migrationBuilder.DropTable(
                name: "ProcessedData");

            migrationBuilder.DropTable(
                name: "ProjectImportJobs");

            migrationBuilder.DropTable(
                name: "ProjectStates");

            migrationBuilder.DropTable(
                name: "RawDataRows");

            migrationBuilder.DropTable(
                name: "Projects");
        }
    }
}
