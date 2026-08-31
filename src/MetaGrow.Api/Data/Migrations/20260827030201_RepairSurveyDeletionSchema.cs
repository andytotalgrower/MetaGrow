using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace MetaGrow.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class RepairSurveyDeletionSchema : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                """
                IF OBJECT_ID(N'dbo.SampleSurveyDeletionRequests', N'U') IS NULL
                BEGIN
                    CREATE TABLE dbo.SampleSurveyDeletionRequests
                    (
                        Id uniqueidentifier NOT NULL,
                        SurveyType int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_SurveyType DEFAULT (2),
                        SurveyId int NOT NULL,
                        PropertyId int NOT NULL,
                        PropertyName nvarchar(250) NOT NULL,
                        SurveyDate datetime2 NOT NULL,
                        ExpectedModificationDate datetime2 NULL,
                        SampleCount int NOT NULL,
                        PhotoCount int NOT NULL,
                        ActionCount int NOT NULL,
                        LinkedSoilResultCount int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_LinkedSoilResultCount DEFAULT (0),
                        LinkedTissueResultCount int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_LinkedTissueResultCount DEFAULT (0),
                        LinkedSapResultCount int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_LinkedSapResultCount DEFAULT (0),
                        LinkedQuickSoilResultCount int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_LinkedQuickSoilResultCount DEFAULT (0),
                        LinkedLegacyResultCount int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_LinkedLegacyResultCount DEFAULT (0),
                        DeleteLinkedLabResults bit NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_DeleteLinkedLabResults DEFAULT (0),
                        Status int NOT NULL,
                        RequestedByUserId nvarchar(450) NOT NULL,
                        RequestedByEmail nvarchar(256) NOT NULL,
                        RequestedUtc datetime2 NOT NULL,
                        ReviewedByUserId nvarchar(450) NULL,
                        ReviewedByEmail nvarchar(256) NULL,
                        ReviewedUtc datetime2 NULL,
                        ReviewNote nvarchar(500) NULL,
                        LastError nvarchar(1000) NULL,
                        CONSTRAINT PK_SampleSurveyDeletionRequests PRIMARY KEY (Id)
                    );
                END;

                IF COL_LENGTH(N'dbo.SampleSurveyDeletionRequests', N'SurveyType') IS NULL
                BEGIN
                    ALTER TABLE dbo.SampleSurveyDeletionRequests
                        ADD SurveyType int NOT NULL
                            CONSTRAINT DF_SampleSurveyDeletionRequests_SurveyType
                            DEFAULT (2) WITH VALUES;
                END;

                IF EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.SampleSurveyDeletionRequests')
                      AND name = N'IX_SampleSurveyDeletionRequests_SurveyId'
                )
                BEGIN
                    DROP INDEX IX_SampleSurveyDeletionRequests_SurveyId
                        ON dbo.SampleSurveyDeletionRequests;
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.SampleSurveyDeletionRequests')
                      AND name = N'IX_SampleSurveyDeletionRequests_Status_RequestedUtc'
                )
                BEGIN
                    CREATE NONCLUSTERED INDEX IX_SampleSurveyDeletionRequests_Status_RequestedUtc
                        ON dbo.SampleSurveyDeletionRequests (Status, RequestedUtc);
                END;

                IF NOT EXISTS
                (
                    SELECT 1
                    FROM sys.indexes
                    WHERE object_id = OBJECT_ID(N'dbo.SampleSurveyDeletionRequests')
                      AND name = N'IX_SampleSurveyDeletionRequests_SurveyType_SurveyId'
                )
                BEGIN
                    CREATE UNIQUE NONCLUSTERED INDEX IX_SampleSurveyDeletionRequests_SurveyType_SurveyId
                        ON dbo.SampleSurveyDeletionRequests (SurveyType, SurveyId)
                        WHERE Status IN (0, 1);
                END;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // This migration repairs schema promised by SyncModelChanges. Rolling it back
            // must not remove that schema from databases where the earlier migration worked.
        }
    }
}
