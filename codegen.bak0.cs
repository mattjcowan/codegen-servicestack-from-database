//css_args /provider:%CSSCRIPT_DIR%\lib\CSSCodeProvider.v4.6.dll
//css_nuget DatabaseSchemaReader
//css_nuget -ng:"-Version 1.7.1" Handlebars.Net
//css_nuget ServiceStack.Text
//css_nuget JsonPrettyPrinter
//css_import templates\helpers.cs;

using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.ComponentModel.DataAnnotations;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Text.RegularExpressions;

using DatabaseSchemaReader;
using DatabaseSchemaReader.DataSchema;
using HandlebarsDotNet;
using ServiceStack.Text;
using DescriptionAttribute = System.ComponentModel.DescriptionAttribute;

class Script
{

    const string CodeGenJsonFileName = "codegen.json";

    public static void Main(string[] args)
    {
        JsConfig.EmitCamelCaseNames = true;
        JsConfig.IncludeNullValues = false;
        JsConfig.IncludeNullValuesInDictionaries = true;

        try
        {
            if (args == null || args.Length == 0 || args[0].Contains("?") || args[0].ToLower().Equals("help"))
            {
                Console.WriteLine(AppArgs.HelpFor<ScriptArgs>());
                return;
            }

            var scriptArgs = args.As<ScriptArgs>();

            var xdb = GetXDb(scriptArgs);

            if (Path.GetExtension(scriptArgs.OutputDir) == ".json")
            {
                OutputJsonFile(xdb, scriptArgs);
                Console.WriteLine("File " + scriptArgs.OutputDir + " created");
            }
            else
            {
                if (!Directory.Exists(scriptArgs.OutputDir))
                {
                    throw new ArgumentException("o",
                        "The output flag must either be a *.json file, or an existing directory to dump the generated code into.");
                }

                OutputCode(scriptArgs, xdb, new DirectoryInfo(scriptArgs.OutputDir));
            }
        }
        catch (ApplicationException aex)
        {
            Console.WriteLine(aex.Message);
            if (aex.InnerException != null)
                Console.WriteLine(aex.InnerException);
        }
        catch (Exception ex)
        {
            Console.WriteLine(ex.Message);
            Console.WriteLine();
            Console.WriteLine(AppArgs.HelpFor<ScriptArgs>());
        }
    }

    private static void OutputCode(ScriptArgs args, XDb xdb, DirectoryInfo outputDir)
    {
        if (!Directory.Exists("templates"))
            return;

        Console.WriteLine("Generating code ...");

        var codeGenData = new CodeGenTemplate
        {
            Namespace = args.Namespace ?? "CodeGenerated",
            Db = xdb,
            Data =
                Directory.GetFiles("templates", "*.json", SearchOption.AllDirectories)
                    .Where(f => !f.EndsWith(CodeGenJsonFileName, true, CultureInfo.CurrentCulture))
                    .ToDictionary(
                        k => Path.GetFileNameWithoutExtension(k),
                        v => ServiceStack.DynamicJson.Deserialize(File.ReadAllText(v)))
        };

        if (args.OutputType == OutputType.Default || args.OutputType.HasFlag(OutputType.Json))
        {
            var json = ServiceStack.StringExtensions.ToJson(codeGenData);
            json = JsonPrettyPrinterPlus.PrettyPrinterExtensions.PrettyPrintJson(json);
            File.WriteAllText(Path.Combine(outputDir.FullName, CodeGenJsonFileName), json);
        }

        if (args.OutputType == OutputType.Default || args.OutputType.HasFlag(OutputType.CSharp))
        {
            var allTemplateFiles = Directory.GetFiles("templates", "*.hbs", SearchOption.AllDirectories);

            // REGISTER HELPERS
            HandlebarsUtils.Helpers.Init();

            // REGISTER PARTIAL TEMPLATES
            foreach (var partialTemplateFile in allTemplateFiles.Where(atf => atf.StartsWith("_")))
            {
                var partialFileNameWithoutExtension = Path.GetFileNameWithoutExtension(partialTemplateFile);
                if (string.IsNullOrWhiteSpace(partialFileNameWithoutExtension)) continue;

                var partialName = partialFileNameWithoutExtension.Substring(1);
                using (var reader = new StringReader(File.ReadAllText(partialTemplateFile)))
                {
                    var partialTemplate = Handlebars.Compile(reader);
                    Handlebars.RegisterTemplate(partialName, partialTemplate);
                }
            }

            // REGISTER TEMPLATES
            var templates = new Dictionary<string, Func<object, string>>();
            foreach (var templateFile in allTemplateFiles.Where(atf => !atf.StartsWith("_")))
            {
                var templateFileNameWithoutExtension = Path.GetFileNameWithoutExtension(templateFile);
                if (string.IsNullOrWhiteSpace(templateFileNameWithoutExtension)) continue;

                templates.Add(templateFileNameWithoutExtension, Handlebars.Compile(File.ReadAllText(templateFile)));
            }

            foreach (var template in templates)
            {
                codeGenData.Template = template.Key;
                var result = template.Value(codeGenData);
                File.WriteAllText(Path.Combine(outputDir.FullName, template.Key + ".cs"), result);
            }
        }
    }

    public class CodeGenTemplate
    {
        public string Namespace { get; set; }
        public string Template { get; set; }
        public XDb Db { get; set; }
        public Dictionary<string, dynamic> Data { get; set; }
    }

    public class ScriptArgs
    {
        [Required]
        [Display(ShortName = "n", Description = "The root namespace for the generated code (typically the name of your app)")]
        public string Namespace { get; set; }

        [Required]
        [Display(ShortName = "c", Description = "The connection string to your database")]
        public string ConnectionString { get; set; }

        [Required]
        [Display(ShortName = "d", Description = "The database dialect/type (eg: Db2, MySql, Oracle, PostgreSql, SQLite, SqlServer, SqlServerCe)")]
        public SqlType Dialect { get; set; }

        [Required]
        [Display(ShortName = "o", Description = "The directory to generate code in.")]
        public string OutputDir { get; set; }
        
        [Display(ShortName = "t", Description = "Restrict generated assets to: default, json, csharp.")]
        public OutputType OutputType { get; set; }
    }

    private static void OutputJsonFile(XDb xdb, ScriptArgs scriptArgs)
    {
        var json = ServiceStack.StringExtensions.ToJson(xdb);
        json = JsonPrettyPrinterPlus.PrettyPrinterExtensions.PrettyPrintJson(json);
        if (!scriptArgs.Force && File.Exists(scriptArgs.OutputDir))
        {
            scriptArgs.OutputDir = Path.GetFileNameWithoutExtension(scriptArgs.OutputDir) + "_" +
                                    DateTime.Now.ToString("u")
                                        .Replace(" ", "")
                                        .Replace(":", "")
                                        .Replace("-", "");
        }
        var dir = Path.GetDirectoryName(scriptArgs.OutputDir);

        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        File.WriteAllText(scriptArgs.OutputDir, json, Encoding.UTF8);
    }

    static XDb GetXDb(ScriptArgs scriptArgs)
    {
        DatabaseReader dr = null;
        XDb xdb = null;

        try
        {
            dr = new DatabaseReader(scriptArgs.ConnectionString, scriptArgs.Dialect);

            var db = dr.ReadAll();

            xdb = new XDb
            {
                Dialect = scriptArgs.Dialect.ToString(),
                Provider = db.Provider
            };

            var dataTypes = db.DataTypes.Select(dt => new XDataType
            {
                CreateFormat = dt.CreateFormat,
                IsDateTime = dt.IsDateTime,
                IsFloat = dt.IsFloat,
                IsInt = dt.IsInt,
                IsNumeric = dt.IsNumeric,
                IsString = dt.IsString,
                IsStringClob = dt.IsStringClob,
                LiteralPrefix = dt.LiteralPrefix,
                LiteralSuffix = dt.LiteralSuffix,
                NetDataType = dt.NetDataType,
                NetDataTypeCSharpName = dt.NetDataTypeCSharpName,
                ProviderDbType = dt.ProviderDbType,
                TypeName = dt.TypeName
            }).ToDictionary(x => x.TypeName, x => x);
            xdb.DataTypes = dataTypes;
            xdb.Users = db.Users.Select(u => u.Name).ToList();

            var schemaOwners =
                db.Tables.Select(t => t.SchemaOwner).ToList().Union(
                    db.Views.Select(t => t.SchemaOwner).ToList().Union(
                        db.StoredProcedures.Select(t => t.SchemaOwner).ToList()))
                    .OrderBy(s => s)
                    .Distinct();

            foreach (var so in schemaOwners)
            {
                var xschema = new XSchema();
                xdb.Schemas.Add(so, xschema);

                // first get all the relationships between the tables
                foreach (var dbTable in db.Tables.Where(t => t.SchemaOwner == so))
                {
                    foreach (var dbFk in dbTable.ForeignKeys.Where(fk => fk.Columns.Count == 1))
                    {
                        var columnsWithUniqueIndexes =
                            dbTable.UniqueKeys.Where(uk => uk.Columns.Count == 1).Select(x => x.Columns.First()).ToArray();
                        var fkColumnIsUnique = columnsWithUniqueIndexes.Contains(dbFk.Columns[0]);
                        var fkColumnIsNullable = dbTable.FindColumn(dbFk.Columns[0]).Nullable;

                        var xfk = new XAssociation();
                        xschema.Associations.Add(dbFk.Name, xfk);

                        xfk.Name = dbFk.Name;
                        xfk.SchemaName = so;
                        xfk.FkCardinality = fkColumnIsUnique
                            ? (fkColumnIsNullable ? XCardinality.ZeroOrOne : XCardinality.One)
                            : XCardinality.Many;
                        xfk.FkSchema = so;
                        xfk.FkTable = dbTable.Name;
                        xfk.FkColumnName = dbFk.Columns[0];
                        xfk.PkSchema = dbFk.RefersToSchema;
                        xfk.PkTable = dbFk.RefersToTable;
                        xfk.PkColumnName = dbFk.ReferencedColumns(db).FirstOrDefault();
                        xfk.PkConstraintName = dbFk.RefersToConstraint;
                        xfk.DeleteRule = dbFk.DeleteRule;
                        xfk.UpdateRule = dbFk.UpdateRule;
                    }
                }

                foreach (var dbTable in db.Tables.Where(t => t.SchemaOwner == so))
                {
                    var xtable = new XTable();
                    xschema.Tables.Add(dbTable.Name, xtable);

                    xtable.Name = dbTable.Name;
                    xtable.SchemaName = so;
                    xtable.IsManyToManyTable = dbTable.IsManyToManyTable();
                    xtable.HasCompositeKey = dbTable.HasCompositeKey;
                    xtable.HasIdentityColumn = dbTable.HasAutoNumberColumn;
                    xtable.ForeignKeyAssociationNames = xschema.Associations.Where(
                            a => a.Value.FkSchema == xschema.Name && a.Value.FkTable == dbTable.Name)
                            .Select(b => b.Key)
                            .ToArray();
                    xtable.ReverseForeignKeyAssociationNames =
                        xschema.Associations.Where(
                            a => a.Value.PkSchema == xschema.Name && a.Value.PkTable == dbTable.Name)
                            .Select(b => b.Key)
                            .ToArray();
                    xtable.Description = dbTable.Description;
                    xtable.PrimaryKey = new XPrimaryKey
                    {
                        Name = dbTable.PrimaryKey?.Name,
                        ColumnNames = dbTable.PrimaryKey?.Columns.ToArray()
                    };

                    foreach (var idx in dbTable.UniqueKeys.Where(u => u.ConstraintType != ConstraintType.PrimaryKey))
                    {
                        xtable.Indexes.Add(idx.Name, new XIndex { Name = idx.Name, ColumnNames = idx.Columns.ToArray(), IsUnique = true });
                    }

                    foreach (var idx in dbTable.Indexes.Where(u => u.Name != dbTable.PrimaryKey?.Name))
                    {
                        if (!xtable.Indexes.ContainsKey(idx.Name))
                            xtable.Indexes.Add(idx.Name, new XIndex { Name = idx.Name, ColumnNames = idx.Columns.Select(c => c.Name).ToArray(), IsUnique = idx.IsUnique });
                    }

                    foreach (var dbc in dbTable.Columns.OrderBy(c => c.Ordinal))
                    {
                        var xcolumn = new XColumn
                        {
                            Name = dbc.Name,
                            TableName = dbTable.Name,
                            Description = dbc.Description,
                            Ordinal = dbc.Ordinal,
                            DataTypeName = dbc.DataType?.TypeName ?? string.Empty,
                            DbDataTypeName = dbc.DbDataType,
                            Length = dbc.Length,
                            Precision = dbc.Precision,
                            Scale = dbc.Scale,
                            DateTimePrecision = dbc.DateTimePrecision,
                            ComputedDefinition = dbc.ComputedDefinition,
                            DefaultValue = dbc.DefaultValue,
                            IsIdentity = dbc.IsAutoNumber,
                            IsComputed = dbc.IsComputed,
                            IsForeignKey = dbc.IsForeignKey,
                            IsIndexed = dbc.IsIndexed,
                            IsPrimaryKey = dbc.IsPrimaryKey,
                            IsNullable = dbc.Nullable,
                            IsPartOfUniqueKeyIndex = dbc.IsUniqueKey,
                            IsUnique = dbc.IsUniqueKey && dbTable.UniqueKeys.Any(u => u.Columns.Count == 1 && u.Columns[0] == dbc.Name),
                            UniqueKeyIndexName = dbc.IsUniqueKey ? dbTable.UniqueKeys.FirstOrDefault(u => u.Columns.Count == 1 && u.Columns[0] == dbc.Name)?.Name : null,
                            IdentityDefinition = dbc.IsAutoNumber ? dbc.IdentityDefinition : null,
                            ForeignKeyName = dbc.IsForeignKey ? xschema.Associations.SingleOrDefault(fk => fk.Value.FkSchema == xschema.Name && fk.Value.FkTable == xtable.Name && fk.Value.FkColumnName == dbc.Name).Key : null
                        };
                        xtable.Columns.Add(dbc.Name, xcolumn);
                    }
                }

                foreach (var dbView in db.Views.Where(t => t.SchemaOwner == so))
                {
                    var xview = new XView();
                    xschema.Views.Add(dbView.Name, xview);

                    xview.Name = dbView.Name;
                    xview.ForeignKeyAssociationNames = dbView.ForeignKeys.Select(fk => fk.Name).ToArray();
                    xview.Sql = dbView.Sql;
                    foreach (var dbc in dbView.Columns.OrderBy(c => c.Ordinal))
                    {
                        var xcolumn = new XColumn
                        {
                            Name = dbc.Name,
                            ViewName = xview.Name,
                            Description = dbc.Description,
                            Ordinal = dbc.Ordinal,
                            DataTypeName = dbc.DataType?.TypeName ?? string.Empty,
                            DbDataTypeName = dbc.DbDataType,
                            Length = dbc.Length,
                            Precision = dbc.Precision,
                            Scale = dbc.Scale,
                            DateTimePrecision = dbc.DateTimePrecision,
                            IsNullable = dbc.Nullable
                        };
                        xview.Columns.Add(dbc.Name, xcolumn);
                    }
                }

                foreach (var dbProc in db.StoredProcedures.Where(t => t.SchemaOwner == so))
                {
                    var xproc = new XProcedure();
                    xschema.Procedures.Add(dbProc.Name, xproc);

                    xproc.Name = dbProc.Name;
                    xproc.Sql = dbProc.Sql;
                    foreach (var dbc in dbProc.Arguments.OrderBy(c => c.Ordinal))
                    {
                        var xarg = new XArgument
                        {
                            Name = dbc.Name,
                            ProcedureName = xproc.Name,
                            Ordinal = dbc.Ordinal,
                            DataTypeName = dbc.DataType?.TypeName,
                            DbDataTypeName = dbc.DatabaseDataType,
                            Length = dbc.Length,
                            Precision = dbc.Precision,
                            Scale = dbc.Scale,
                            In = dbc.In,
                            Out = dbc.Out
                        };
                        xproc.Arguments.Add(dbc.Name, xarg);
                    }
                }

                foreach (var dbFunc in db.Functions.Where(t => t.SchemaOwner == so))
                {
                    var xfunc = new XFunction();
                    xschema.Functions.Add(dbFunc.Name, xfunc);

                    xfunc.Name = dbFunc.Name;
                    xfunc.FullName = dbFunc.FullName;
                    xfunc.Language = dbFunc.Language;
                    xfunc.ReturnType = dbFunc.ReturnType;
                    xfunc.ResultSetCount = dbFunc.ResultSets.Count();
                    foreach (DatabaseResultSet rs in dbFunc.ResultSets)
                    {
                    }
                    xfunc.Sql = dbFunc.Sql;
                    foreach (var dbc in dbFunc.Arguments.OrderBy(c => c.Ordinal))
                    {
                        var xarg = new XArgument
                        {
                            Name = dbc.Name,
                            FunctionName = xfunc.Name,
                            Ordinal = dbc.Ordinal,
                            DataTypeName = dbc.DataType?.TypeName,
                            DbDataTypeName = dbc.DatabaseDataType,
                            Length = dbc.Length,
                            Precision = dbc.Precision,
                            Scale = dbc.Scale,
                            In = dbc.In,
                            Out = dbc.Out
                        };
                        xfunc.Arguments.Add(dbc.Name, xarg);
                    }
                }
            }
        }
        catch (Exception ex)
        {
            throw new ApplicationException(ex.Message, ex);
        }
        finally
        {
            dr?.Dispose();
        }
        return xdb;
    }
    
    public enum OutputType: int
    {
        Default = 0,
        Json = 1,
        CSharp = 2
    }
}

public static class AppArgs
{
    static Regex _pattern = new Regex("[/-](?'key'[^\\s=:]+)"
        + "([=:]("
            + "((?'open'\").+(?'value-open'\"))"
            + "|"
            + "(?'value'.+)"
        + "))?", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    static Regex _uriPattern = new Regex(@"[\\?&](?'key'[^&=]+)(=(?'value'[^&]+))?", RegexOptions.Compiled);
    static Regex _queryStringPattern = new Regex(@"(^|&)(?'key'[^&=]+)(=(?'value'[^&]+))?", RegexOptions.Compiled);

    static IEnumerable<ArgProperty> PropertiesOf<T>()
    {
        return from p in typeof(T).GetProperties(BindingFlags.Instance | BindingFlags.Public | BindingFlags.SetProperty)
               let d = p.Attribute<DescriptionAttribute>()
               let alias = p.Attribute<DisplayAttribute>()
               select new ArgProperty
               {
                   Property = p,
                   Name = alias == null || String.IsNullOrWhiteSpace(alias.GetShortName()) ? p.Name.ToLower() : alias.GetShortName(),
                   Type = p.PropertyType,
                   Required = p.Attribute<RequiredAttribute>(),
                   RequiresValue = !(p.PropertyType == typeof(bool) || p.PropertyType == typeof(bool?)),
                   Description = d != null && !String.IsNullOrWhiteSpace(d.Description) ? d.Description
                      : alias != null ? alias.GetDescription() : String.Empty
               };
    }

    /// <summary>
    /// Parses the arguments in <paramref name="args"/> and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="args"/>.</typeparam>
    /// <param name="args">Command-line arguments, usually in the form of "/name=value".</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this string[] args) where T : new()
    {
        var arguments = (from a in args
                         let match = _pattern.Match(a)
                         where match.Success
                         select new
                         {
                             Key = match.Groups["key"].Value.ToLower(),
                             match.Groups["value"].Value
                         }
                        ).ToDictionary(a => a.Key, a => a.Value);

        return arguments.As<T>();
    }

    /// <summary>
    /// Parses the arguments in the supplied string and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// The string should be in the format "key1=value1&key2=value2&key3=value3".
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="args"/>.</typeparam>
    /// <param name="args">Command-line arguments, usually in the form of "/name=value".</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this string queryString) where T : new()
    {
        var arguments = (from match in _queryStringPattern.Matches(queryString).Cast<Match>()
                         where match.Success
                         select new
                         {
                             Key = match.Groups["key"].Value.ToLower(),
                             match.Groups["value"].Value
                         }
            ).ToDictionary(a => a.Key, a => a.Value);

        return arguments.As<T>();
    }

    /// <summary>
    /// Parses the URI parameters in <paramref name="uri"/> and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="args"/>.</typeparam>
    /// <param name="args">A URI, usually a ClickOnce activation URI.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this Uri uri) where T : new()
    {
        var arguments = (from match in _uriPattern.Matches(uri.ToString()).Cast<Match>()
                         where match.Success
                         select new
                         {
                             Key = match.Groups["key"].Value.ToLower(),
                             match.Groups["value"].Value
                         }
            ).ToDictionary(a => a.Key, a => a.Value);

        return arguments.As<T>();
    }

    /// <summary>
    /// Parses the name/value pairs in <paramref name="arguments"/> and creates an instance of <typeparamref name="T"/> with the
    /// corresponding properties populated.
    /// </summary>
    /// <typeparam name="T">The custom type to be populated from <paramref name="args"/>.</typeparam>
    /// <param name="args">The key/value pairs to be parsed.</param>
    /// <returns>A new instance of <typeparamref name="T"/>.</returns>
    public static T As<T>(this Dictionary<string, string> arguments) where T : new()
    {
        T result = new T();

        var props = PropertiesOf<T>().ToList();

        foreach (var arg in arguments)
        {
            var matches = props.Where(p => p.Name.StartsWith(arg.Key, StringComparison.OrdinalIgnoreCase)).ToList();

            if (matches.Count == 0)
            {
                throw new ArgumentException("Unknown argument '" + arg.Key + "'");
            }
            else if (matches.Count > 1)
            {
                throw new ArgumentException("Ambiguous argument '" + arg.Key + "'");
            }

            var prop = matches[0];

            if (!String.IsNullOrWhiteSpace(arg.Value))
            {
                if (prop.Type.IsArray)
                {
                    string v = arg.Value;

                    if (v.StartsWith("{") && v.EndsWith("}"))
                    {
                        v = v.Substring(1, arg.Value.Length - 2);
                    }

                    var values = v.Split(',').ToArray();
                    var array = Array.CreateInstance(prop.Type.GetElementType(), values.Length);

                    for (int i = 0; i < values.Length; i++)
                    {
                        var converter = TypeDescriptor.GetConverter(prop.Type.GetElementType());
                        array.SetValue(converter.ConvertFrom(values[i]), i);
                    }

                    var arrayConverter = new ArrayConverter();
                    prop.Property.SetValue(result, array, null);
                }
                else
                {
                    var converter = TypeDescriptor.GetConverter(prop.Type);
                    prop.Property.SetValue(result, converter.ConvertFromString(arg.Value), null);
                }
            }
            else if (prop.Type == typeof(bool))
            {
                prop.Property.SetValue(result, true, null);
            }
            else
            {
                throw new ArgumentException("No value supplied for argument '" + arg.Key + "'");
            }
        }

        foreach (var p in props.Where(p => p.Required != null))
        {
            if (!p.Required.IsValid(p.Property.GetValue(result, null)) || !arguments.Keys.Where(a => p.Name.StartsWith(a)).Any())
            {
                throw new ArgumentException("Argument missing: '" + p.Name + "'");
            }
        }

        return result;
    }

    /// <summary>
    /// Returns a string describing the arguments necessary to populate an instance of <typeparamref name="T"/>.
    /// </summary>
    /// <typeparam name="T">A class representing the potential application arguments.</typeparam>
    /// <returns>A string describing the arguments necessary to populate an instance of <typeparamref name="T"/></returns>
    public static string HelpFor<T>()
    {
        var props = PropertiesOf<T>().OrderBy(p => p.RequiresValue).ThenBy(p => p.Name).ToList();

        var len = props.Max(p => p.Name.Length);

        var sb = new StringBuilder();

        sb.Append(System.IO.Path.GetFileNameWithoutExtension(Environment.GetCommandLineArgs()[0]));
        foreach (var p in props.Where(p => p.Required != null))
        {
            sb.Append(" /" + p.Name + (p.RequiresValue ? "=value" : ""));
        }

        foreach (var p in props.Where(p => p.Required == null))
        {
            sb.Append(" [/" + p.Name + (p.RequiresValue ? "=value" : "") + "]");
        }

        sb.AppendLine();
        sb.AppendLine();
        var required = true;

        foreach (var p in props.OrderByDescending(p => p.Required != null).ThenBy(p => p.Name).ToList())
        {
            if (required && p.Required == null)
            {
                required = false;
                sb.AppendLine();
                sb.AppendLine("Optional params:");
                sb.AppendLine();
            }
            sb.AppendLine(" /" + p.Name.PadRight(len) + "\t" + p.Description);
        }

        return sb.ToString();
    }

    class ArgProperty
    {
        public PropertyInfo Property { get; set; }
        public string Name { get; set; }
        public RequiredAttribute Required { get; set; }
        public bool RequiresValue { get; set; }
        public Type Type { get; set; }
        public string Description { get; set; }
    }
}

public static class PropertyInfoExtensions
{
    public static T Attribute<T>(this PropertyInfo p)
    {
        return p.GetCustomAttributes(typeof(T), true).Cast<T>().FirstOrDefault();
    }
}

#region Schema Types
public class XDb
{
    public string Dialect { get; set; }
    public string Provider { get; set; }
    public Dictionary<string, XDataType> DataTypes { get; set; } = new Dictionary<string, XDataType>();
    public List<string> Users { get; set; } = new List<string>();
    public Dictionary<string, XSchema> Schemas { get; set; } = new Dictionary<string, XSchema>();
}

public class XSchema
{
    public string Name { get; set; }
    public Dictionary<string, XTable> Tables { get; set; } = new Dictionary<string, XTable>();
    public Dictionary<string, XView> Views { get; set; } = new Dictionary<string, XView>();
    public Dictionary<string, XProcedure> Procedures { get; set; } = new Dictionary<string, XProcedure>();
    public Dictionary<string, XFunction> Functions { get; set; } = new Dictionary<string, XFunction>();
    public Dictionary<string, XAssociation> Associations { get; set; } = new Dictionary<string, XAssociation>();
}

public enum XCardinality
{
    One,
    ZeroOrOne,
    Many
}

public class XAssociation
{
    public string Name { get; set; }
    public string FkColumnName { get; set; }
    public string FkTable { get; set; }
    public string FkSchema { get; set; }
    public XCardinality FkCardinality { get; set; }
    public string PkColumnName { get; set; }
    public string PkTable { get; set; }
    public string PkSchema { get; set; }
    public string PkConstraintName { get; set; }
    public string DeleteRule { get; set; }
    public string UpdateRule { get; set; }
    public string SchemaName { get; internal set; }
}

public class XColumn
{
    public string Name { get; set; }
    public bool IsReadOnly { get; set; }
    public int Ordinal { get; set; }
    public string DataTypeName { get; set; }
    public string DbDataTypeName { get; set; }
    public int? Length { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public int? DateTimePrecision { get; set; }
    public string ComputedDefinition { get; set; }
    public string DefaultValue { get; set; }
    public string Description { get; set; }
    public bool IsIdentity { get; set; }
    public bool IsComputed { get; set; }
    public bool IsForeignKey { get; set; }
    public bool IsIndexed { get; set; }
    public bool IsPrimaryKey { get; set; }
    public bool IsNullable { get; set; }
    public bool IsPartOfUniqueKeyIndex { get; set; }
    public bool IsUnique { get; set; }
    public string UniqueKeyIndexName { get; set; }
    public DatabaseColumnIdentity IdentityDefinition { get; set; }
    public string ForeignKeyName { get; set; }
    public string TableName { get; internal set; }
    public string ViewName { get; internal set; }
}

public class XArgument
{
    public string Name { get; set; }
    public string DataTypeName { get; set; }
    public string DbDataTypeName { get; set; }
    public bool In { get; set; }
    public int? Length { get; set; }
    public decimal Ordinal { get; set; }
    public bool Out { get; set; }
    public int? Precision { get; set; }
    public int? Scale { get; set; }
    public string ProcedureName { get; internal set; }
    public string FunctionName { get; internal set; }
}

public class XPrimaryKey
{
    public string Name { get; set; }
    public string[] ColumnNames { get; set; }
}

public class XIndex
{
    public string Name { get; set; }
    public string[] ColumnNames { get; set; }
    public bool IsUnique { get; set; }
}

public class XTable
{
    public string Name { get; set; }
    public bool IsManyToManyTable { get; set; }
    public bool HasCompositeKey { get; set; }
    public Dictionary<string, XColumn> Columns { get; set; } = new Dictionary<string, XColumn>();
    public bool HasIdentityColumn { get; set; }
    public string[] ForeignKeyAssociationNames { get; set; }
    public string[] ReverseForeignKeyAssociationNames { get; set; }
    public string Description { get; set; }
    public XPrimaryKey PrimaryKey { get; set; }
    public Dictionary<string, XIndex> Indexes { get; set; } = new Dictionary<string, XIndex>();
    public string SchemaName { get; internal set; }
}

public class XView
{
    public string Name { get; set; }
    public string[] ForeignKeyAssociationNames { get; set; }
    public Dictionary<string, XColumn> Columns { get; set; } = new Dictionary<string, XColumn>();
    public string Sql { get; set; }
}

public class XProcedure
{
    public string Name { get; set; }
    public Dictionary<string, XArgument> Arguments { get; set; } = new Dictionary<string, XArgument>();
    public string Sql { get; set; }
}

public class XFunction
{
    public string Name { get; set; }
    public string FullName { get; set; }
    public string Language { get; set; }
    public string ReturnType { get; set; }
    public int ResultSetCount { get; set; }

    public Dictionary<string, XArgument> Arguments { get; set; } = new Dictionary<string, XArgument>();
    public string Sql { get; set; }
}

public class XResultSet
{
}

public class XDataType
{
    public string CreateFormat { get; set; }
    public bool IsDateTime { get; set; }
    public bool IsFloat { get; set; }
    public bool IsInt { get; set; }
    public bool IsNumeric { get; set; }
    public bool IsString { get; set; }
    public bool IsStringClob { get; set; }
    public object LiteralPrefix { get; set; }
    public object LiteralSuffix { get; set; }
    public string NetDataType { get; set; }
    public string NetDataTypeCSharpName { get; set; }
    public int ProviderDbType { get; set; }
    public string TypeName { get; set; }
}
#endregion