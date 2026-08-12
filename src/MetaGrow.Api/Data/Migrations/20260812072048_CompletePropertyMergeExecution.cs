using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class CompletePropertyMergeExecution : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "CompletedUtc",
                table: "PropertyMergeRequests",
                type: "datetime2",
                nullable: true);

            migrationBuilder.AddColumn<long>(
                name: "PropertyMergeId",
                table: "PropertyMergeRequests",
                type: "bigint",
                nullable: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "CompletedUtc",
                table: "PropertyMergeRequests");

            migrationBuilder.DropColumn(
                name: "PropertyMergeId",
                table: "PropertyMergeRequests");
        }
    }
}
