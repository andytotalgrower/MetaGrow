-- REVIEW COPY: MetaGrow database only. Generated offline; not executed.
-- Normal API startup applies this EF migration after deployment approval.
BEGIN TRANSACTION;
IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'NitrogenPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'NitrogenPc',N'Nitrogen %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'PhosphorousPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'PhosphorousPc',N'Phosphorous %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'PotassiumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'PotassiumPc',N'Potassium %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SulphurPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SulphurPc',N'Sulphur %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'CalciumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'CalciumPc',N'Calcium %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'MagnesiumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'MagnesiumPc',N'Magnesium %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'ChloridePc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'ChloridePc',N'Chloride %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SiliconPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SiliconPc',N'Silicon %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'BoronPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'BoronPpm',N'Boron ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'CopperPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'CopperPpm',N'Copper ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'IronPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'IronPpm',N'Iron ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'ManganesePpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'ManganesePpm',N'Manganese ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'ZincPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'ZincPpm',N'Zinc ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SodiumPc') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SodiumPc',N'Sodium %');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'MolybdenumPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'MolybdenumPpm',N'Molybdenum ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'CobaltPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'CobaltPpm',N'Cobalt ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'SeleniumPpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'SeleniumPpm',N'Selenium ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF NOT EXISTS (SELECT 1 FROM [SoilReportDefinitionFields] WHERE [Key]=N'NitratePpm') INSERT INTO [SoilReportDefinitionFields] ([Key],[Label]) VALUES (N'NitratePpm',N'Nitrate ppm');
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Key', N'Name', N'Version') AND [object_id] = OBJECT_ID(N'[SoilReportDefinitions]'))
        SET IDENTITY_INSERT [SoilReportDefinitions] ON;
    EXEC(N'INSERT INTO [SoilReportDefinitions] ([Key], [Name], [Version])
    VALUES (N''LabReportDefinitionTissue.xml'', N''Tissue · Nutrients'', ''eaa69ca1-5ac1-44e9-93ac-d9099edcd3c7'')');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'Key', N'Name', N'Version') AND [object_id] = OBJECT_ID(N'[SoilReportDefinitions]'))
        SET IDENTITY_INSERT [SoilReportDefinitions] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'DefinitionKey', N'Id', N'Name', N'Colour', N'Order', N'SampleSurveyVisible') AND [object_id] = OBJECT_ID(N'[SoilReportDefinitionSections]'))
        SET IDENTITY_INSERT [SoilReportDefinitionSections] ON;
    EXEC(N'INSERT INTO [SoilReportDefinitionSections] ([DefinitionKey], [Id], [Name], [Colour], [Order], [SampleSurveyVisible])
    VALUES (N''LabReportDefinitionTissue.xml'', 1, N''Tissue Nutrients'', N''#0077c1'', 10, CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'DefinitionKey', N'Id', N'Name', N'Colour', N'Order', N'SampleSurveyVisible') AND [object_id] = OBJECT_ID(N'[SoilReportDefinitionSections]'))
        SET IDENTITY_INSERT [SoilReportDefinitionSections] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'DefinitionKey', N'SectionId', N'Id', N'LegacyItemId', N'Field', N'Label', N'Order', N'NumberFormat', N'ShowIfZero') AND [object_id] = OBJECT_ID(N'[SoilReportDefinitionItems]'))
        SET IDENTITY_INSERT [SoilReportDefinitionItems] ON;
    EXEC(N'INSERT INTO [SoilReportDefinitionItems] ([DefinitionKey], [SectionId], [Id], [LegacyItemId], [Field], [Label], [Order], [NumberFormat], [ShowIfZero])
    VALUES (N''LabReportDefinitionTissue.xml'', 1, 10, 10, N''NitrogenPc'', N''Nitrogen %'', 10, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 20, 20, N''PhosphorousPc'', N''Phosphorous %'', 20, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 30, 30, N''PotassiumPc'', N''Potassium %'', 30, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 40, 40, N''SulphurPc'', N''Sulphur %'', 40, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 50, 50, N''CalciumPc'', N''Calcium %'', 50, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 60, 60, N''MagnesiumPc'', N''Magnesium %'', 60, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 65, 65, N''ChloridePc'', N''Chloride %'', 65, N''#,##0.00'', CAST(0 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 67, 67, N''SiliconPc'', N''Silicon %'', 67, N''#,##0.00'', CAST(0 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 70, 70, N''BoronPpm'', N''Boron ppm'', 70, N''#,##0.0'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 80, 80, N''CopperPpm'', N''Copper ppm'', 80, N''#,##0.0'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 90, 90, N''IronPpm'', N''Iron ppm'', 90, N''#,##0.0'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 100, 100, N''ManganesePpm'', N''Manganese ppm'', 100, N''#,##0.0'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 110, 110, N''ZincPpm'', N''Zinc ppm'', 110, N''#,##0.0'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 200, NULL, N''SodiumPc'', N''Sodium %'', 200, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 210, NULL, N''MolybdenumPpm'', N''Molybdenum ppm'', 210, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 220, NULL, N''CobaltPpm'', N''Cobalt ppm'', 220, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 230, NULL, N''SeleniumPpm'', N''Selenium ppm'', 230, N''#,##0.00'', CAST(1 AS bit)),
    (N''LabReportDefinitionTissue.xml'', 1, 240, NULL, N''NitratePpm'', N''Nitrate ppm'', 240, N''#,##0.0'', CAST(1 AS bit))');
    IF EXISTS (SELECT * FROM [sys].[identity_columns] WHERE [name] IN (N'DefinitionKey', N'SectionId', N'Id', N'LegacyItemId', N'Field', N'Label', N'Order', N'NumberFormat', N'ShowIfZero') AND [object_id] = OBJECT_ID(N'[SoilReportDefinitionItems]'))
        SET IDENTITY_INSERT [SoilReportDefinitionItems] OFF;
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    ALTER TABLE [SoilReportStates] ADD [TissueMatchesJson] nvarchar(max) NOT NULL DEFAULT N'[]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    ALTER TABLE [SoilReportStates] ADD [TissueTemplatesJson] nvarchar(max) NOT NULL DEFAULT N'[]';
END;

IF NOT EXISTS (
    SELECT * FROM [__EFMigrationsHistory]
    WHERE [MigrationId] = N'20261015041615_AddSoilTissueReports'
)
BEGIN
    INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
    VALUES (N'20261015041615_AddSoilTissueReports', N'10.0.13');
END;

COMMIT;
GO

