-- A keyless table
CREATE TABLE [NullableValueConversionTest]
(
    -- Tests nullable int.
    [Id] INT NULL,

    -- Tests nullable enum conversion in two different ways.
    [Ev] CHAR(2) NULL CHECK([Ev] IN ('A1', 'B1', 'A2', 'B2')),

    -- For testing stored procedures
    [Fv] REAL NOT NULL,

)
GO

CREATE TABLE [EntityConversionsTest]
(
    [Id1] INT NOT NULL,
    [Id2] INT NOT NULL,
    [Ev] CHAR(2) NOT NULL,
    [Fv] REAL NOT NULL,

    PRIMARY KEY([Id1], [Id2]),
)
GO

CREATE TYPE [SelectorList] AS TABLE
(
    [Id] INT,
    [Ev] CHAR(2)
)
GO

-- Combined output parameters with result set.
CREATE PROCEDURE [ProcQuery]
    @Selectors [SelectorList] READONLY,
    @Sum REAL OUTPUT
AS
    SET @Sum =
    (
        SELECT SUM([Fv])
        FROM [EntityConversionsTest] AS nv
        WHERE EXISTS (SELECT 1 FROM @Selectors WHERE [Id] = nv.[Id1] AND [Ev] = nv.[Ev])
    );

    SELECT *
    FROM [EntityConversionsTest] AS nv
    WHERE EXISTS (SELECT 1 FROM @Selectors WHERE [Id] = nv.[Id1] AND [Ev] = nv.[Ev])
RETURN 1
GO

CREATE PROCEDURE [TypeTest]
    @BOOL BIT OUTPUT,
    @I8 TINYINT OUTPUT,
    @I16 SMALLINT OUTPUT,
    @I32 INT OUTPUT,
    @I64 BIGINT OUTPUT,
    @BIN1 BINARY(16) OUTPUT,
    @BIN2 VARBINARY(16) OUTPUT,
    @BIN3 VARBINARY(MAX) OUTPUT,
    @CH1 CHAR(16) OUTPUT,
    @CH2 VARCHAR(16) OUTPUT,
    @CH3 VARCHAR(MAX) OUTPUT,
    @NC1 NCHAR(16) OUTPUT,
    @NC2 NVARCHAR(16) OUTPUT,
    @NC3 NVARCHAR(MAX) OUTPUT,
    @DT DATETIME2 OUTPUT,
    @DTO DATETIMEOFFSET OUTPUT,
    @F64 FLOAT OUTPUT,
    @F32 REAL OUTPUT,
    @TM TIME OUTPUT,
    @G UNIQUEIDENTIFIER
AS
    SET @BOOL = 1;
    SET @I8 = @I8 + 1;
    SET @I16 = @I16 + 1;
    SET @I32 = @I32 + 1;
    SET @I64 = @I64 + 1;
    SET @BIN1 = CAST(65537 AS BINARY(16));
    SET @BIN2 = CAST(65537 AS VARBINARY(16));
    SET @BIN3 = CAST(@G AS VARBINARY(16));
    SET @CH1 = CONCAT('ASDF', @CH1);
    SET @CH2 = CONCAT('ASDF', @CH2);
    SET @CH3 = CONCAT('ASDF', @CH3);
    SET @NC1 = CONCAT('ÆAD', @NC1);
    SET @NC2 = CONCAT('ÆAD', @NC2);
    SET @NC3 = CONCAT('ÆADXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXXX', @NC3);
    SET @DT = DATEADD(DAY, 1, @DT);
    SET @DTO = DATEADD(DAY, 1, @DTO);
    SET @F64 = @F64 + 1;
    SET @F32 = @F32 + 1;
    SET @TM = DATEADD(HOUR, 1, @TM);
RETURN -12
GO


