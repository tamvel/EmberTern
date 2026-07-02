using System.Collections.Generic;
using EmberTern.Core.Sql.Templates.Psql;
using EmberTern.Core.Sql.Templates.Routines;
using EmberTern.Core.Sql.Templates.Tables;

namespace EmberTern.Core.Sql.Templates;

/// <summary>
/// The built-in template set and the one place that lists them. The App layer calls
/// <see cref="CreateRegistry"/> once at startup. User/plugin templates will be appended
/// to <see cref="BuiltIns"/>'s output before constructing the registry — the only change
/// needed to make the feature extensible.
/// </summary>
public static class SqlTemplateCatalog
{
    public static IReadOnlyList<ISqlTemplate> BuiltIns() => new ISqlTemplate[]
    {
        // Tables / Views
        new TableSelectAllTemplate(),
        new TableSelectColumnsTemplate(),
        new TableFieldListTemplate(),
        new TableParameterListTemplate(),
        new TableInsertTemplate(),
        new TableUpdateTemplate(),
        new TableDeleteTemplate(),
        new TableUpsertTemplate(),

        // Procedures
        new ProcedureExecuteTemplate(),
        new ProcedureSelectFromTemplate(),

        // Functions
        new FunctionCallTemplate(),

        // Generators
        new GeneratorNextValueTemplate(),
        new GeneratorGenIdTemplate(),

        // PSQL scaffolds (body-only): loops, variable declarations, exception raises.
        new TableForSelectTemplate(),
        new TableDeclareVariablesTemplate(),
        new ProcedureForSelectFromTemplate(),
        new ExceptionRaiseTemplate(),
        new ExceptionRaiseMessageTemplate(),
    };

    public static SqlTemplateRegistry CreateRegistry() => new(BuiltIns());
}
