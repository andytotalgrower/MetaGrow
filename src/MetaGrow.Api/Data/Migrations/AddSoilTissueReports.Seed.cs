// One-time conversion of the legacy measured tissue rows, with the remaining supported lab measurements.
// Calculated Fe/P and Mn/P ratios are deliberately omitted: this phase displays laboratory readings only.
using Microsoft.EntityFrameworkCore.Migrations;
#nullable disable

namespace MetaGrow.Api.Data.Migrations;

public partial class AddSoilTissueReports
{
    private static void SeedTissueDefinition(MigrationBuilder migrationBuilder)
    {
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'NitrogenPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'NitrogenPc',N'Nitrogen %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'PhosphorousPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'PhosphorousPc',N'Phosphorous %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'PotassiumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'PotassiumPc',N'Potassium %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SulphurPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SulphurPc',N'Sulphur %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'CalciumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'CalciumPc',N'Calcium %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'MagnesiumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'MagnesiumPc',N'Magnesium %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'ChloridePc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'ChloridePc',N'Chloride %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SiliconPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SiliconPc',N'Silicon %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'BoronPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'BoronPpm',N'Boron ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'CopperPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'CopperPpm',N'Copper ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'IronPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'IronPpm',N'Iron ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'ManganesePpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'ManganesePpm',N'Manganese ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'ZincPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'ZincPpm',N'Zinc ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SodiumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SodiumPc',N'Sodium %');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'MolybdenumPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'MolybdenumPpm',N'Molybdenum ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'CobaltPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'CobaltPpm',N'Cobalt ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SeleniumPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SeleniumPpm',N'Selenium ppm');");
        migrationBuilder.Sql("IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'NitratePpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'NitratePpm',N'Nitrate ppm');");
        migrationBuilder.InsertData("SoilReportDefinitions", new[] { "Key", "Name", "Version" },
            new object[] { "LabReportDefinitionTissue.xml", "Tissue · Nutrients", new Guid("eaa69ca1-5ac1-44e9-93ac-d9099edcd3c7") });
        migrationBuilder.InsertData("SoilReportDefinitionSections", new[] { "DefinitionKey", "Id", "Name", "Colour", "Order", "SampleSurveyVisible" },
            new object[] { "LabReportDefinitionTissue.xml", 1, "Tissue Nutrients", "#0077c1", 10, true });
        migrationBuilder.InsertData("SoilReportDefinitionItems",
            new[] { "DefinitionKey", "SectionId", "Id", "LegacyItemId", "Field", "Label", "Order", "NumberFormat", "ShowIfZero" },
            new object[,]
            {
                { "LabReportDefinitionTissue.xml", 1, 10, 10, "NitrogenPc", "Nitrogen %", 10, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 20, 20, "PhosphorousPc", "Phosphorous %", 20, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 30, 30, "PotassiumPc", "Potassium %", 30, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 40, 40, "SulphurPc", "Sulphur %", 40, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 50, 50, "CalciumPc", "Calcium %", 50, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 60, 60, "MagnesiumPc", "Magnesium %", 60, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 65, 65, "ChloridePc", "Chloride %", 65, "#,##0.00", false },
                { "LabReportDefinitionTissue.xml", 1, 67, 67, "SiliconPc", "Silicon %", 67, "#,##0.00", false },
                { "LabReportDefinitionTissue.xml", 1, 70, 70, "BoronPpm", "Boron ppm", 70, "#,##0.0", true },
                { "LabReportDefinitionTissue.xml", 1, 80, 80, "CopperPpm", "Copper ppm", 80, "#,##0.0", true },
                { "LabReportDefinitionTissue.xml", 1, 90, 90, "IronPpm", "Iron ppm", 90, "#,##0.0", true },
                { "LabReportDefinitionTissue.xml", 1, 100, 100, "ManganesePpm", "Manganese ppm", 100, "#,##0.0", true },
                { "LabReportDefinitionTissue.xml", 1, 110, 110, "ZincPpm", "Zinc ppm", 110, "#,##0.0", true },
                { "LabReportDefinitionTissue.xml", 1, 200, null, "SodiumPc", "Sodium %", 200, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 210, null, "MolybdenumPpm", "Molybdenum ppm", 210, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 220, null, "CobaltPpm", "Cobalt ppm", 220, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 230, null, "SeleniumPpm", "Selenium ppm", 230, "#,##0.00", true },
                { "LabReportDefinitionTissue.xml", 1, 240, null, "NitratePpm", "Nitrate ppm", 240, "#,##0.0", true },
            });
    }
}
