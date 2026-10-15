using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class AddSoilReportRevisions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "SoilRevisionId",
                table: "ReportShares",
                type: "uniqueidentifier",
                nullable: true);

            migrationBuilder.CreateTable(
                name: "SoilReportStates",
                columns: table => new
                {
                    SurveyId = table.Column<int>(type: "int", nullable: false),
                    TemplateId = table.Column<int>(type: "int", nullable: false),
                    RegionId = table.Column<int>(type: "int", nullable: false),
                    Version = table.Column<long>(type: "bigint", nullable: false),
                    RevisionNumber = table.Column<int>(type: "int", nullable: false),
                    CurrentRevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportStates", x => x.SurveyId);
                });

            migrationBuilder.CreateTable(
                name: "SoilReportAudits",
                columns: table => new
                {
                    Id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("SqlServer:Identity", "1, 1"),
                    SurveyId = table.Column<int>(type: "int", nullable: false),
                    RevisionId = table.Column<Guid>(type: "uniqueidentifier", nullable: true),
                    OccurredUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    ActorId = table.Column<string>(type: "nvarchar(450)", maxLength: 450, nullable: false),
                    Actor = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false),
                    Action = table.Column<string>(type: "nvarchar(40)", maxLength: 40, nullable: false),
                    Reason = table.Column<string>(type: "nvarchar(1000)", maxLength: 1000, nullable: false),
                    PreviousStatus = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    Status = table.Column<string>(type: "nvarchar(20)", maxLength: 20, nullable: false),
                    QaHours = table.Column<double>(type: "float", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportAudits", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoilReportAudits_SoilReportStates_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "SoilReportStates",
                        principalColumn: "SurveyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "SoilReportRevisions",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uniqueidentifier", nullable: false),
                    SurveyId = table.Column<int>(type: "int", nullable: false),
                    Number = table.Column<int>(type: "int", nullable: false),
                    InputHash = table.Column<string>(type: "nvarchar(64)", maxLength: 64, nullable: false),
                    SourceJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    DocumentJson = table.Column<string>(type: "nvarchar(max)", nullable: false),
                    CreatedUtc = table.Column<DateTime>(type: "datetime2", nullable: false),
                    CreatedBy = table.Column<string>(type: "nvarchar(256)", maxLength: 256, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_SoilReportRevisions", x => x.Id);
                    table.ForeignKey(
                        name: "FK_SoilReportRevisions_SoilReportStates_SurveyId",
                        column: x => x.SurveyId,
                        principalTable: "SoilReportStates",
                        principalColumn: "SurveyId",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_SoilReportAudits_SurveyId_OccurredUtc",
                table: "SoilReportAudits",
                columns: new[] { "SurveyId", "OccurredUtc" });

            migrationBuilder.CreateIndex(
                name: "IX_SoilReportRevisions_SurveyId_Number",
                table: "SoilReportRevisions",
                columns: new[] { "SurveyId", "Number" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "SoilReportAudits");

            migrationBuilder.DropTable(
                name: "SoilReportRevisions");

            migrationBuilder.DropTable(
                name: "SoilReportStates");

            migrationBuilder.DropColumn(
                name: "SoilRevisionId",
                table: "ReportShares");
        }
    }
}
