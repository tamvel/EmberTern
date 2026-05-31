using System;
using System.Collections.Generic;
using System.Linq;

namespace EmberTern.Core.Sql;

// Keyword list surfaced by the SQL editor's autocomplete. Uppercase so the
// completion display matches the IBExpert / SQL convention (the formatter
// lowercases on demand, but the completion list reads better in caps).
//
// Single-token only — multi-word phrases (CHARACTER SET, NEXT VALUE FOR) are
// completed one keyword at a time, the same way they're tokenized.
public static class SqlKeywords
{
    private static readonly string[] Raw =
    {
        // DML
        "SELECT", "FROM", "WHERE", "HAVING", "GROUP", "BY", "ORDER", "ASC", "DESC",
        "INSERT", "INTO", "VALUES", "UPDATE", "SET", "DELETE", "MERGE", "USING",
        "RETURNING", "DISTINCT", "ALL", "ANY", "SOME", "AS", "ON",
        // JOIN
        "JOIN", "LEFT", "RIGHT", "INNER", "OUTER", "CROSS", "FULL", "NATURAL",
        // Logical / comparison
        "AND", "OR", "NOT", "IN", "IS", "NULL", "LIKE", "BETWEEN", "EXISTS",
        "CONTAINING", "STARTING", "WITH", "SIMILAR", "TO", "ESCAPE",
        // CTE / set ops
        "RECURSIVE", "UNION", "INTERSECT", "EXCEPT",
        // PSQL / control flow
        "BEGIN", "END", "DECLARE", "VARIABLE", "EXECUTE", "BLOCK", "STATEMENT",
        "IF", "THEN", "ELSE", "WHEN", "CASE", "WHILE", "DO", "FOR",
        "LEAVE", "BREAK", "CONTINUE", "SUSPEND", "EXIT",
        // DDL
        "CREATE", "ALTER", "DROP", "RECREATE", "TABLE", "VIEW", "INDEX",
        "PROCEDURE", "TRIGGER", "FUNCTION", "GENERATOR", "SEQUENCE", "DOMAIN",
        "EXCEPTION", "ROLE", "PACKAGE", "BODY",
        // Constraints
        "PRIMARY", "KEY", "FOREIGN", "REFERENCES", "UNIQUE", "CHECK", "DEFAULT",
        "CONSTRAINT", "CASCADE", "RESTRICT", "ACTION",
        // Pagination
        "FIRST", "SKIP", "ROWS", "ROW", "ONLY", "FETCH", "OFFSET", "LIMIT", "NEXT",
        // Transaction
        "COMMIT", "ROLLBACK", "SAVEPOINT", "RELEASE", "TRANSACTION", "READ",
        "WRITE", "ISOLATION", "LEVEL", "SNAPSHOT", "WAIT",
        // Types
        "INTEGER", "INT", "SMALLINT", "BIGINT", "FLOAT", "DOUBLE", "PRECISION",
        "NUMERIC", "DECIMAL", "CHAR", "VARCHAR", "NCHAR", "DATE", "TIME",
        "TIMESTAMP", "BLOB", "BOOLEAN", "TRUE", "FALSE", "CHARACTER", "COLLATE",
        // Datetime / context constants
        "CURRENT_DATE", "CURRENT_TIME", "CURRENT_TIMESTAMP", "CURRENT_USER",
        "CURRENT_ROLE", "CURRENT_CONNECTION", "CURRENT_TRANSACTION",
        // Common built-in functions
        "COUNT", "SUM", "AVG", "MIN", "MAX", "COALESCE", "NULLIF", "CAST",
        "EXTRACT", "SUBSTRING", "TRIM", "UPPER", "LOWER", "GEN_ID",
        "IIF", "ABS", "MOD", "POWER", "SQRT", "ROUND", "CEILING", "FLOOR",
    };

    public static IReadOnlyList<string> All { get; } =
        Raw.Distinct(StringComparer.OrdinalIgnoreCase)
           .OrderBy(s => s, StringComparer.OrdinalIgnoreCase)
           .ToArray();
}
